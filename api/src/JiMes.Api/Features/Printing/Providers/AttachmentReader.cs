using Dapper;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Printing.Providers;

/// <summary>공통 첨부 (attachment) 읽기 — DB 보관(file_content) 또는 서버 저장소(file.storage_root 상대경로).</summary>
public sealed class AttachmentReader(SettingsCache settings)
{
    private sealed class Row
    {
        public byte[]? FileContent { get; init; }
        public string? StoragePath { get; init; }
    }

    public async Task<byte[]?> ReadFirstAsync(MySqlConnection conn, string ownerTable, long ownerId, string kind)
    {
        var row = await conn.QueryFirstOrDefaultAsync<Row>(
            """
            SELECT file_content, storage_path FROM attachment
             WHERE owner_table = @ownerTable AND owner_id = @ownerId AND attachment_kind = @kind
             ORDER BY sort_order, attachment_id LIMIT 1
            """, new { ownerTable, ownerId, kind });
        if (row?.FileContent is { Length: > 0 } content)
            return content;
        if (row?.StoragePath is { Length: > 0 } relative)
        {
            var root = Path.GetFullPath(settings.GetString(SettingKeys.FileStorageRoot));
            var path = Path.GetFullPath(Path.Combine(root, relative));
            // 저장소 밖을 가리키는 경로는 읽지 않는다
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                return await File.ReadAllBytesAsync(path);
        }
        return null;
    }
}
