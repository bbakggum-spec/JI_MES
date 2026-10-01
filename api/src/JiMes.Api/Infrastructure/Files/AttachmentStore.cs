using System.Security.Cryptography;
using Dapper;
using JiMes.Api.Features.Master.Parts;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Infrastructure.Files;

/// <summary>
/// 공통 첨부(attachment) 저장·조회·삭제 — 품목 도면, 보전 사진, 교정 성적서 등 (구 PC 로컬 폴더 대체, 설계 §15.4).
/// 첨부 종류는 공통코드 ATTACHMENT_KIND, attr.owner 가 있으면 그 소유 테이블에만 허용. 크기 상한 = 설정 file.max_attachment_mb.
/// </summary>
public sealed class AttachmentStore(
    IDbConnectionFactory db, CommonCodeCache codes, SettingsCache settings, AuditWriter audit, ICurrentUser currentUser)
{
    /// <param name="owner">attachment.owner_table — 소유 테이블 이름 (PK = {owner}_id)</param>
    public async Task<long> AddAsync(string owner, long ownerId, string kind, string fileName, string? contentType, byte[] content, string? caption, CancellationToken ct)
    {
        var maxMb = settings.GetInt(SettingKeys.FileMaxAttachmentMb);
        if (content.Length == 0 || content.Length > maxMb * 1024L * 1024L)
            throw new RequestValidationException("file", $"첨부 파일은 {maxMb}MB 이하여야 합니다.");
        var kindCode = codes.GetGroup("ATTACHMENT_KIND").Codes.FirstOrDefault(c => c.Code == kind && c.IsActive)
            ?? throw new RequestValidationException("kind", "허용되지 않는 첨부 종류입니다.");
        if (kindCode.AttrJson is { } attr && attr.Contains("\"owner\"") && !attr.Contains($"\"owner\":\"{owner}\""))
            throw new RequestValidationException("kind", $"'{kindCode.CodeName}' 은 이 화면의 첨부가 아닙니다.");

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // owner 는 코드 상수만 넘어온다 (사용자 입력 아님)
        if (await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM `{owner}` WHERE `{owner}_id` = @ownerId", new { ownerId }, tx) == 0)
            throw new NotFoundException(owner, ownerId);
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO attachment (owner_table, owner_id, attachment_kind, file_name, content_type, file_content, file_hash, file_size, caption,
                                    sort_order, created_by)
            VALUES (@owner, @ownerId, @kind, @fileName, @contentType, @content, @hash, @size, @caption,
                    (SELECT COALESCE(MAX(a.sort_order), 0) + 1 FROM attachment a WHERE a.owner_table = @owner AND a.owner_id = @ownerId), @userId);
            SELECT LAST_INSERT_ID();
            """,
            new
            {
                owner, ownerId, kind, fileName = Path.GetFileName(fileName), contentType, content, hash, size = content.Length,
                caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(), userId = currentUser.UserId,
            }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, "attachment", id, null,
            new { owner_table = owner, owner_id = ownerId, attachment_kind = kind, file_name = fileName, file_size = content.Length, file_hash = hash });
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<(byte[] Content, string FileName, string ContentType)> GetAsync(string owner, long ownerId, long attachmentId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(byte[]? Content, string FileName, string? ContentType)>(
            "SELECT file_content, file_name, content_type FROM attachment WHERE attachment_id = @attachmentId AND owner_table = @owner AND owner_id = @ownerId",
            new { attachmentId, ownerId, owner });
        if (row.Content is null)
            throw new NotFoundException("attachment", attachmentId);
        return (row.Content, row.FileName, row.ContentType ?? "application/octet-stream");
    }

    public async Task DeleteAsync(string owner, long ownerId, long attachmentId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await conn.QuerySingleOrDefaultAsync<AttachmentDto>(
            """
            SELECT attachment_id, attachment_kind, file_name, content_type, file_size, caption, created_at
              FROM attachment WHERE attachment_id = @attachmentId AND owner_table = @owner AND owner_id = @ownerId
            """, new { attachmentId, ownerId, owner }, tx) ?? throw new NotFoundException("attachment", attachmentId);
        await conn.ExecuteAsync("DELETE FROM attachment WHERE attachment_id = @attachmentId", new { attachmentId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Delete, "attachment", attachmentId, before, null);
        await tx.CommitAsync(ct);
    }

    public static async Task<List<AttachmentDto>> ListAsync(MySqlConnection conn, MySqlTransaction? tx, string owner, long ownerId) =>
        (await conn.QueryAsync<AttachmentDto>(
            """
            SELECT attachment_id, attachment_kind, file_name, content_type, file_size, caption, created_at
              FROM attachment WHERE owner_table = @owner AND owner_id = @ownerId ORDER BY attachment_kind, sort_order
            """, new { owner, ownerId }, tx)).ToList();
}
