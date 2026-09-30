using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Features.Master.Process;

public sealed class HeatProcessDto
{
    public long HeatProcessId { get; init; }
    public string HeatProcessCode { get; init; } = "";
    public string HeatProcessName { get; init; } = "";
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public int? CurrentVersionNo { get; init; }
    public string? RouteSummary { get; init; }
}

public sealed class HeatProcessVersionDto
{
    public long HeatProcessVersionId { get; init; }
    public int VersionNo { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public bool IsCurrent { get; init; }
    public string? Remark { get; init; }
    public long UsageCount { get; init; }
    public List<OperationDto> Operations { get; } = [];
}

public sealed class OperationDto
{
    public long HeatProcessVersionId { get; init; }
    public int SequenceNo { get; init; }
    public long UnitProcessId { get; init; }
    public string? UnitProcessName { get; init; }
    public bool IsMainProcess { get; init; }
    public bool IsRequired { get; init; }
}

public sealed record OperationInput(long UnitProcessId, bool IsMainProcess, bool IsRequired = true);
public sealed record HeatProcessCreateRequest(string? HeatProcessCode, string? HeatProcessName, string? Description, OperationInput[]? Operations);
public sealed record HeatProcessUpdateRequest(string? HeatProcessName, string? Description, bool IsActive);
public sealed record RouteRequest(OperationInput[]? Operations, string? Remark);

/// <summary>
/// 공정 경로 (설계 §2.1, 구 F_UnitProcess 의 t_heatprocess Subp1~16). 경로 = 단위공정 순서 + 주공정(Version 당 최대 1개).
/// 거래(수주 품목·작업 LOT)가 참조한 Version 은 고치지 않고 새 Version 을 만든다 (§1.3). 참조가 없으면 현재 Version 을 고친다.
/// </summary>
public static class HeatProcessEndpoints
{
    public static void MapHeatProcessEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/heat-processes").WithTags("HeatProcess");
        const string key = MenuKeys.MasterHeatProcess;
        g.MapGet("/", ListAsync).RequirePermission(key, PermissionAction.Read);
        g.MapGet("/{id:long}", DetailAsync).RequirePermission(key, PermissionAction.Read);
        g.MapPost("/", CreateAsync).RequirePermission(key, PermissionAction.Create);
        g.MapPut("/{id:long}", UpdateAsync).RequirePermission(key, PermissionAction.Update);
        g.MapPut("/{id:long}/route", SaveRouteAsync).RequirePermission(key, PermissionAction.Update);
    }

    private const string ListSql =
        """
        SELECT h.heat_process_id, h.heat_process_code, h.heat_process_name, h.description, h.is_active, v.version_no AS current_version_no,
               (SELECT GROUP_CONCAT(CONCAT(u.unit_process_name, IF(o.is_main_process = 1, '*', '')) ORDER BY o.sequence_no SEPARATOR ' > ')
                  FROM heat_process_operation o JOIN unit_process u ON u.unit_process_id = o.unit_process_id
                 WHERE o.heat_process_version_id = v.heat_process_version_id) AS route_summary
          FROM heat_process h
          LEFT JOIN heat_process_version v ON v.heat_process_id = h.heat_process_id AND v.is_current = 1
        """;

    private static async Task<IResult> ListAsync(IDbConnectionFactory db, CancellationToken ct, bool includeInactive = false, string? search = null)
    {
        await using var conn = await db.OpenAsync(ct);
        return Results.Ok(await conn.QueryAsync<HeatProcessDto>(
            ListSql + """
             WHERE (@includeInactive OR h.is_active = 1)
               AND (@search IS NULL OR h.heat_process_code LIKE @like OR h.heat_process_name LIKE @like)
             ORDER BY h.heat_process_name
            """, new { includeInactive, search = string.IsNullOrWhiteSpace(search) ? null : search, like = $"%{search?.Trim()}%" }));
    }

    private static async Task<IResult> DetailAsync(long id, IDbConnectionFactory db, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var header = await conn.QuerySingleOrDefaultAsync<HeatProcessDto>(ListSql + " WHERE h.heat_process_id = @id", new { id })
            ?? throw new NotFoundException("heat_process", id);
        return Results.Ok(new { header, versions = await VersionsAsync(conn, null, id) });
    }

    private static async Task<List<HeatProcessVersionDto>> VersionsAsync(MySqlConnection conn, MySqlTransaction? tx, long id)
    {
        var versions = (await conn.QueryAsync<HeatProcessVersionDto>(
            """
            SELECT v.heat_process_version_id, v.version_no, v.effective_from, v.effective_to, v.is_current, v.remark,
                   (SELECT COUNT(*) FROM sales_order_item s WHERE s.heat_process_version_id = v.heat_process_version_id)
                 + (SELECT COUNT(*) FROM production_work w WHERE w.heat_process_version_id = v.heat_process_version_id) AS usage_count
              FROM heat_process_version v WHERE v.heat_process_id = @id ORDER BY v.version_no DESC
            """, new { id }, tx)).ToList();
        var ops = await conn.QueryAsync<OperationDto>(
            """
            SELECT o.heat_process_version_id, o.sequence_no, o.unit_process_id, u.unit_process_name, o.is_main_process, o.is_required
              FROM heat_process_operation o
              JOIN heat_process_version v ON v.heat_process_version_id = o.heat_process_version_id
              JOIN unit_process u ON u.unit_process_id = o.unit_process_id
             WHERE v.heat_process_id = @id ORDER BY o.sequence_no
            """, new { id }, tx);
        var byVersion = versions.ToDictionary(v => v.HeatProcessVersionId);
        foreach (var op in ops)
            byVersion[op.HeatProcessVersionId].Operations.Add(op);
        return versions;
    }

    private static async Task<IResult> CreateAsync(HeatProcessCreateRequest r, IDbConnectionFactory db, AuditWriter audit, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var code = Required(r.HeatProcessCode, "heatProcessCode", 50);
        var name = Required(r.HeatProcessName, "heatProcessName", 100);
        var ops = ValidateOperations(r.Operations);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM heat_process WHERE heat_process_code = @code", new { code }, tx) > 0)
            throw new RequestValidationException("heatProcessCode", "이미 사용 중인 공정 코드입니다.");
        await EnsureUnitProcessesAsync(conn, tx, ops);

        var id = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO heat_process (heat_process_code, heat_process_name, description, created_by, updated_by) VALUES (@code, @name, @d, @u, @u); SELECT LAST_INSERT_ID();",
            new { code, name, d = r.Description?.Trim(), u = user.UserId }, tx);
        var versionId = await InsertVersionAsync(conn, tx, id, 1, VersionClock.NextFrom(time, null), "최초 등록", user.UserId);
        await InsertOperationsAsync(conn, tx, versionId, ops);
        await audit.WriteAsync(conn, tx, AuditAction.Create, "heat_process", id, null, new { heat_process_code = code, heat_process_name = name, operations = ops });
        await tx.CommitAsync(ct);
        return Results.Ok(new { heatProcessId = id });
    }

    private static async Task<IResult> UpdateAsync(long id, HeatProcessUpdateRequest r, IDbConnectionFactory db, AuditWriter audit, ICurrentUser user, CancellationToken ct)
    {
        var name = Required(r.HeatProcessName, "heatProcessName", 100);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await conn.QuerySingleOrDefaultAsync<HeatProcessDto>(ListSql + " WHERE h.heat_process_id = @id FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException("heat_process", id);
        await conn.ExecuteAsync("UPDATE heat_process SET heat_process_name = @name, description = @d, is_active = @IsActive, updated_by = @u WHERE heat_process_id = @id",
            new { id, name, d = r.Description?.Trim(), r.IsActive, u = user.UserId }, tx);
        await audit.WriteAsync(conn, tx, before.IsActive != r.IsActive ? AuditAction.StatusChange : AuditAction.Update, "heat_process", id,
            new { before.HeatProcessName, before.Description, before.IsActive }, new { heat_process_name = name, r.Description, r.IsActive });
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    /// <summary>경로 저장 — 현재 Version 이 거래에서 쓰였으면 새 Version, 아니면 현재 Version 을 고친다.</summary>
    private static async Task<IResult> SaveRouteAsync(long id, RouteRequest r, IDbConnectionFactory db, AuditWriter audit, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var ops = ValidateOperations(r.Operations);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM heat_process WHERE heat_process_id = @id FOR UPDATE", new { id }, tx) == 0)
            throw new NotFoundException("heat_process", id);
        await EnsureUnitProcessesAsync(conn, tx, ops);
        var versions = await VersionsAsync(conn, tx, id);
        var current = versions.FirstOrDefault(v => v.IsCurrent);
        var now = VersionClock.NextFrom(time, current?.EffectiveFrom);   // 같은 초 연속 저장에도 종료 > 시작

        bool created;
        int versionNo;
        if (current is null || current.UsageCount > 0)
        {
            versionNo = (versions.Count == 0 ? 0 : versions.Max(v => v.VersionNo)) + 1;
            if (current is not null)
                await conn.ExecuteAsync("UPDATE heat_process_version SET is_current = 0, effective_to = @now WHERE heat_process_version_id = @Id",
                    new { now, Id = current.HeatProcessVersionId }, tx);
            var versionId = await InsertVersionAsync(conn, tx, id, versionNo, now, r.Remark, user.UserId);
            await InsertOperationsAsync(conn, tx, versionId, ops);
            created = true;
        }
        else
        {
            versionNo = current.VersionNo;
            await conn.ExecuteAsync("DELETE FROM heat_process_operation WHERE heat_process_version_id = @Id", new { Id = current.HeatProcessVersionId }, tx);
            await InsertOperationsAsync(conn, tx, current.HeatProcessVersionId, ops);
            if (r.Remark is not null)
                await conn.ExecuteAsync("UPDATE heat_process_version SET remark = @Remark WHERE heat_process_version_id = @Id", new { r.Remark, Id = current.HeatProcessVersionId }, tx);
            created = false;
        }
        await audit.WriteAsync(conn, tx, AuditAction.Update, "heat_process", id,
            new { version_no = current?.VersionNo, operations = current?.Operations.Select(o => new { o.UnitProcessId, o.IsMainProcess, o.IsRequired }) },
            new { version_no = versionNo, new_version = created, operations = ops }, r.Remark);
        await tx.CommitAsync(ct);
        return Results.Ok(new { versionNo, newVersion = created });
    }

    private static OperationInput[] ValidateOperations(OperationInput[]? ops)
    {
        if (ops is null || ops.Length == 0)
            throw new RequestValidationException("operations", "단위공정을 1개 이상 지정하세요.");
        if (ops.Count(o => o.IsMainProcess) > 1)
            throw new RequestValidationException("operations", "주공정은 1개만 지정합니다 (주 LOT 기준 공정).");
        return ops;
    }

    private static async Task EnsureUnitProcessesAsync(MySqlConnection conn, MySqlTransaction tx, OperationInput[] ops)
    {
        var ids = ops.Select(o => o.UnitProcessId).Distinct().ToArray();
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM unit_process WHERE unit_process_id IN @ids AND is_active = 1", new { ids }, tx) != ids.Length)
            throw new RequestValidationException("operations", "없거나 사용 중지된 단위공정이 있습니다.");
    }

    private static Task<long> InsertVersionAsync(MySqlConnection conn, MySqlTransaction tx, long id, int versionNo, DateTime from, string? remark, long? userId) =>
        conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO heat_process_version (heat_process_id, version_no, effective_from, is_current, remark, created_by)
            VALUES (@id, @versionNo, @from, 1, @remark, @userId); SELECT LAST_INSERT_ID();
            """, new { id, versionNo, from, remark = remark?.Trim(), userId }, tx);

    private static async Task InsertOperationsAsync(MySqlConnection conn, MySqlTransaction tx, long versionId, OperationInput[] ops)
    {
        for (var i = 0; i < ops.Length; i++)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO heat_process_operation (heat_process_version_id, sequence_no, unit_process_id, is_main_process, is_required)
                VALUES (@versionId, @seq, @UnitProcessId, @IsMainProcess, @IsRequired)
                """, new { versionId, seq = (i + 1) * 10, ops[i].UnitProcessId, ops[i].IsMainProcess, ops[i].IsRequired }, tx);
        }
    }

    internal static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v) || v.Length > max)
            throw new RequestValidationException(field, $"1~{max}자여야 합니다.");
        return v;
    }
}
