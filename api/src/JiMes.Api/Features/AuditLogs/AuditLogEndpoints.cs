using Dapper;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.AuditLogs;

public sealed class AuditLogDto
{
    public long AuditLogId { get; init; }
    public DateTime OccurredAt { get; init; }
    public long? AppUserId { get; init; }
    public string? UserName { get; init; }
    public string ActionType { get; init; } = "";
    public string TableName { get; init; } = "";
    public long RecordId { get; init; }
    public string? BeforeJson { get; init; }
    public string? AfterJson { get; init; }
    public string? Reason { get; init; }
    public string? ClientIp { get; init; }
}

public sealed record AuditLogPage(IReadOnlyList<AuditLogDto> Items, long Total, int Page, int PageSize);

public static class AuditLogEndpoints
{
    // 한 번에 돌려주는 최대 행 수 (서버 보호용 기술 상한 — 업무 값 아님)
    private const int MaxPageSize = 500;

    public static void MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit-logs", SearchAsync)
            .WithTags("AuditLogs")
            .RequirePermission(MenuKeys.SystemAudit, PermissionAction.Read);
    }

    private static async Task<IResult> SearchAsync(
        IDbConnectionFactory db, CancellationToken ct,
        string? tableName = null, long? recordId = null, long? appUserId = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        const string where =
            """
             WHERE (@tableName IS NULL OR a.table_name = @tableName)
               AND (@recordId  IS NULL OR a.record_id = @recordId)
               AND (@appUserId IS NULL OR a.app_user_id = @appUserId)
               AND (@from      IS NULL OR a.occurred_at >= @from)
               AND (@to        IS NULL OR a.occurred_at <  @to)
            """;
        var args = new { tableName, recordId, appUserId, from, to, offset = (page - 1) * pageSize, pageSize };

        await using var conn = await db.OpenAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM audit_log a" + where, args);
        var items = await conn.QueryAsync<AuditLogDto>(
            """
            SELECT a.audit_log_id, a.occurred_at, a.app_user_id, u.user_name, a.action_type, a.table_name, a.record_id,
                   a.before_json, a.after_json, a.reason, a.client_ip
              FROM audit_log a
              LEFT JOIN app_user u ON u.app_user_id = a.app_user_id
            """ + where + " ORDER BY a.audit_log_id DESC LIMIT @pageSize OFFSET @offset", args);

        return Results.Ok(new AuditLogPage(items.ToList(), total, page, pageSize));
    }
}
