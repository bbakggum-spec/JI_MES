using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Realtime;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.CommonCodes;

public sealed record CommonCodeDto(
    long CommonCodeId, string Code, string CodeName, int SortOrder, string? AttrJson,
    bool IsSystem, bool IsActive, string? Remark);

public sealed record CommonCodeGroupDto(
    string GroupCode, string GroupName, string? Description, bool IsActive, IReadOnlyList<CommonCodeDto> Codes);

public sealed record UpdateCommonCodeRequest(
    string? CodeName, int SortOrder, string? AttrJson, bool IsActive, string? Remark, string? Reason);

/// <summary>
/// 공통코드 (설계 §15.4). 조회는 로그인 사용자 전체 (웹이 표시명 캐시로 사용),
/// 수정은 권한자만 — is_system 코드는 code 변경·삭제·비활성화 불가, 표시명·순서·속성·비고만 수정.
/// </summary>
public static class CommonCodeEndpoints
{
    public static void MapCommonCodeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/common-codes").WithTags("CommonCodes");

        group.MapGet("/", (CommonCodeCache cache) => Results.Ok(cache.All.Select(g => new CommonCodeGroupDto(
                g.Group.GroupCode, g.Group.GroupName, g.Group.Description, g.Group.IsActive,
                g.Codes.Select(ToDto).ToList()))))
            .RequireLogin();

        group.MapPut("/{id:long}", UpdateAsync)
            .RequirePermission(MenuKeys.SystemCode, PermissionAction.Update);
    }

    private static CommonCodeDto ToDto(CommonCodeRow c) =>
        new(c.CommonCodeId, c.Code, c.CodeName, c.SortOrder, c.AttrJson, c.IsSystem, c.IsActive, c.Remark);

    private static async Task<IResult> UpdateAsync(
        long id, UpdateCommonCodeRequest request, IDbConnectionFactory db, AuditWriter audit,
        CommonCodeCache cache, EventPublisher events, CancellationToken ct)
    {
        var codeName = request.CodeName?.Trim();
        if (string.IsNullOrEmpty(codeName) || codeName.Length > 100)
            throw new RequestValidationException(nameof(request.CodeName), "표시명은 1~100자여야 합니다.");
        var attrJson = string.IsNullOrWhiteSpace(request.AttrJson) ? null : request.AttrJson.Trim();
        if (attrJson is not null)
        {
            try { using var _ = JsonDocument.Parse(attrJson); }
            catch (JsonException) { throw new RequestValidationException(nameof(request.AttrJson), "올바른 JSON 이 아닙니다."); }
        }

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await conn.QuerySingleOrDefaultAsync<CommonCodeRow>(
            CommonCodeCache.SelectCodeSql + " WHERE common_code_id = @id FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException("common_code", id);
        if (before.IsSystem && before.IsActive != request.IsActive)
            throw new BusinessRuleException("SYSTEM_CODE_LOCKED", $"시스템 코드 '{before.Code}' 는 사용 여부를 바꿀 수 없습니다.");

        await conn.ExecuteAsync(
            """
            UPDATE common_code
               SET code_name = @codeName, sort_order = @SortOrder, attr_json = @attrJson, is_active = @IsActive, remark = @remark
             WHERE common_code_id = @id
            """,
            new { id, codeName, request.SortOrder, attrJson, request.IsActive, remark = request.Remark?.Trim() }, tx);
        var after = await conn.QuerySingleAsync<CommonCodeRow>(
            CommonCodeCache.SelectCodeSql + " WHERE common_code_id = @id", new { id }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, "common_code", id, before, after, request.Reason);
        await tx.CommitAsync(ct);

        await cache.ReloadAsync(ct);
        var groupCode = cache.All.First(g => g.Group.CommonCodeGroupId == after.CommonCodeGroupId).Group.GroupCode;
        await events.PublishAsync(RealtimeEvents.CommonCodeChanged, new { groupCode, code = after.Code }, ct);
        return Results.Ok(ToDto(after));
    }
}
