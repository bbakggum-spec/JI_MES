using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Features.Master.Process;

public sealed class InspectionStandardDto
{
    public long InspectionStandardId { get; init; }
    public long PartId { get; init; }
    public string? PartCode { get; init; }
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
    public long? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public bool IsActive { get; init; }
    public int? CurrentVersionNo { get; init; }
    public long CriteriaCount { get; init; }
}

public sealed class InspectionVersionDto
{
    public long InspectionStandardVersionId { get; init; }
    public int VersionNo { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public bool IsCurrent { get; init; }
    public string? Remark { get; init; }
    public string? CreatedByName { get; init; }
    public long UsageCount { get; init; }
}

public sealed class CriteriaDto
{
    public long InspectionCriteriaId { get; init; }
    public int SequenceNo { get; init; }
    public string? ItemType { get; init; }
    public string ItemName { get; init; } = "";
    public string? Location { get; init; }
    public string? SpecificationValue { get; init; }
    public string? ToolName { get; init; }
    public string? TestValue { get; init; }
    public string? Scale { get; init; }
    public string? RangeType { get; init; }
    public decimal? LowerLimit { get; init; }
    public decimal? UpperLimit { get; init; }
    public int? HardnessLimit { get; init; }
    public string? UnitCode { get; init; }
    public int SampleCount { get; init; }
    public int TestCount { get; init; }
    public List<string?> Points { get; } = [];
}

public sealed record CriteriaInput(
    string? ItemType, string? ItemName, string? Location, string? SpecificationValue, string? ToolName, string? TestValue, string? Scale,
    string? RangeType, decimal? LowerLimit, decimal? UpperLimit, int? HardnessLimit, string? UnitCode, int SampleCount, int TestCount, string?[]? Points);

public sealed record InspectionStandardCreateRequest(long PartId, long? CustomerId, CriteriaInput[]? Criteria, string? Remark);
public sealed record CriteriaSaveRequest(CriteriaInput[]? Criteria, string? Remark);
public sealed record ActiveRequest(bool IsActive);

/// <summary>
/// 검사기준 (구 F_InspectionCriteriaForm) — 품목(+업체) × Version × 항목(+측정 위치). 성적서 C 좌표 키·검사 자동 판정의 원천.
/// 검사(inspection)가 쓴 Version 은 고치지 않고 새 Version, 아니면 현재 Version 을 고친다 (§1.3).
/// </summary>
public static class InspectionStandardEndpoints
{
    // 측정 위치 수 상한 (서버 보호용 — 구 P1~P10 보다 넉넉히)
    private const int MaxPoints = 50;

    public static void MapInspectionStandardEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/inspection-standards").WithTags("InspectionStandard");
        const string key = MenuKeys.MasterInspectionStandard;
        g.MapGet("/", ListAsync).RequirePermission(key, PermissionAction.Read);
        g.MapGet("/{id:long}", DetailAsync).RequirePermission(key, PermissionAction.Read);
        g.MapPost("/", CreateAsync).RequirePermission(key, PermissionAction.Create);
        g.MapPut("/{id:long}", async (long id, ActiveRequest r, IDbConnectionFactory db, AuditWriter audit, CancellationToken ct) =>
            {
                await using var conn = await db.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                var before = await conn.ExecuteScalarAsync<bool?>("SELECT is_active FROM inspection_standard WHERE inspection_standard_id = @id FOR UPDATE", new { id }, tx)
                    ?? throw new NotFoundException("inspection_standard", id);
                await conn.ExecuteAsync("UPDATE inspection_standard SET is_active = @IsActive WHERE inspection_standard_id = @id", new { id, r.IsActive }, tx);
                await audit.WriteAsync(conn, tx, AuditAction.StatusChange, "inspection_standard", id, new { is_active = before }, new { is_active = r.IsActive });
                await tx.CommitAsync(ct);
                return Results.NoContent();
            })
            .RequirePermission(key, PermissionAction.Update);
        g.MapPut("/{id:long}/criteria", SaveCriteriaAsync).RequirePermission(key, PermissionAction.Update);
    }

    private const string ListSql =
        """
        SELECT s.inspection_standard_id, s.part_id, p.part_code, p.part_name, p.part_number, s.customer_id, c.customer_name, s.is_active,
               v.version_no AS current_version_no,
               (SELECT COUNT(*) FROM inspection_criteria ic WHERE ic.inspection_standard_version_id = v.inspection_standard_version_id) AS criteria_count
          FROM inspection_standard s
          JOIN part p ON p.part_id = s.part_id
          LEFT JOIN customer c ON c.customer_id = s.customer_id
          LEFT JOIN inspection_standard_version v ON v.inspection_standard_id = s.inspection_standard_id AND v.is_current = 1
        """;

    private static async Task<IResult> ListAsync(IDbConnectionFactory db, CancellationToken ct, string? search = null, long? customerId = null, bool includeInactive = false)
    {
        await using var conn = await db.OpenAsync(ct);
        return Results.Ok(await conn.QueryAsync<InspectionStandardDto>(
            ListSql + """
             WHERE (@includeInactive OR s.is_active = 1) AND (@customerId IS NULL OR s.customer_id = @customerId)
               AND (@search IS NULL OR p.part_code LIKE @like OR p.part_name LIKE @like OR p.part_number LIKE @like)
             ORDER BY p.part_name, c.customer_name
             LIMIT 500
            """, new { includeInactive, customerId, search = string.IsNullOrWhiteSpace(search) ? null : search, like = $"%{search?.Trim()}%" }));
    }

    private static async Task<IResult> DetailAsync(long id, IDbConnectionFactory db, CancellationToken ct, long? versionId = null)
    {
        await using var conn = await db.OpenAsync(ct);
        var header = await conn.QuerySingleOrDefaultAsync<InspectionStandardDto>(ListSql + " WHERE s.inspection_standard_id = @id", new { id })
            ?? throw new NotFoundException("inspection_standard", id);
        var versions = await VersionsAsync(conn, null, id);
        var version = versionId is null ? versions.FirstOrDefault(v => v.IsCurrent)
            : versions.FirstOrDefault(v => v.InspectionStandardVersionId == versionId) ?? throw new NotFoundException("inspection_standard_version", versionId.Value);
        return Results.Ok(new { header, versions, version, criteria = version is null ? [] : await CriteriaAsync(conn, null, version.InspectionStandardVersionId) });
    }

    private static async Task<List<InspectionVersionDto>> VersionsAsync(MySqlConnection conn, MySqlTransaction? tx, long id) =>
        (await conn.QueryAsync<InspectionVersionDto>(
            """
            SELECT v.inspection_standard_version_id, v.version_no, v.effective_from, v.effective_to, v.is_current, v.remark, u.user_name AS created_by_name,
                   (SELECT COUNT(*) FROM inspection i WHERE i.inspection_standard_version_id = v.inspection_standard_version_id) AS usage_count
              FROM inspection_standard_version v LEFT JOIN app_user u ON u.app_user_id = v.created_by
             WHERE v.inspection_standard_id = @id ORDER BY v.version_no DESC
            """, new { id }, tx)).ToList();

    internal static async Task<List<CriteriaDto>> CriteriaAsync(MySqlConnection conn, MySqlTransaction? tx, long versionId)
    {
        var criteria = (await conn.QueryAsync<CriteriaDto>(
            """
            SELECT inspection_criteria_id, sequence_no, item_type, item_name, location, specification_value, tool_name, test_value, scale,
                   range_type, lower_limit, upper_limit, hardness_limit, unit_code, sample_count, test_count
              FROM inspection_criteria WHERE inspection_standard_version_id = @versionId ORDER BY sequence_no
            """, new { versionId }, tx)).ToList();
        var points = await conn.QueryAsync<(long CriteriaId, int PointNo, string? Label)>(
            """
            SELECT CAST(p.inspection_criteria_id AS SIGNED), p.point_no, p.point_label FROM inspection_criteria_point p
              JOIN inspection_criteria c ON c.inspection_criteria_id = p.inspection_criteria_id
             WHERE c.inspection_standard_version_id = @versionId ORDER BY p.point_no
            """, new { versionId }, tx);
        var byId = criteria.ToDictionary(c => c.InspectionCriteriaId);
        foreach (var p in points)
            byId[p.CriteriaId].Points.Add(p.Label);
        return criteria;
    }

    private static async Task<IResult> CreateAsync(InspectionStandardCreateRequest r, IDbConnectionFactory db, CommonCodeCache codes, AuditWriter audit,
        ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var criteria = Validate(r.Criteria, codes);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM part WHERE part_id = @PartId", r, tx) == 0)
            throw new RequestValidationException("partId", "품목을 선택하세요.");
        var existing = await conn.ExecuteScalarAsync<long?>(
            "SELECT inspection_standard_id FROM inspection_standard WHERE part_id = @PartId AND customer_id <=> @CustomerId", r, tx);
        if (existing is not null)
            throw new DuplicateInspectionStandardException(existing.Value);

        var id = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO inspection_standard (part_id, customer_id) VALUES (@PartId, @CustomerId); SELECT LAST_INSERT_ID();", r, tx);
        var versionId = await InsertVersionAsync(conn, tx, id, 1, VersionClock.NextFrom(time, null), r.Remark ?? "최초 등록", user.UserId);
        await InsertCriteriaAsync(conn, tx, versionId, criteria);
        await audit.WriteAsync(conn, tx, AuditAction.Create, "inspection_standard", id, null, new { r.PartId, r.CustomerId, criteria });
        await tx.CommitAsync(ct);
        return Results.Ok(new { inspectionStandardId = id });
    }

    private static async Task<IResult> SaveCriteriaAsync(long id, CriteriaSaveRequest r, IDbConnectionFactory db, CommonCodeCache codes, AuditWriter audit,
        ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var criteria = Validate(r.Criteria, codes);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM inspection_standard WHERE inspection_standard_id = @id FOR UPDATE", new { id }, tx) == 0)
            throw new NotFoundException("inspection_standard", id);
        var versions = await VersionsAsync(conn, tx, id);
        var current = versions.FirstOrDefault(v => v.IsCurrent);
        var before = current is null ? null : await CriteriaAsync(conn, tx, current.InspectionStandardVersionId);

        bool created;
        int versionNo;
        if (current is null || current.UsageCount > 0)
        {
            var from = VersionClock.NextFrom(time, current?.EffectiveFrom);
            versionNo = (versions.Count == 0 ? 0 : versions.Max(v => v.VersionNo)) + 1;
            if (current is not null)
                await conn.ExecuteAsync("UPDATE inspection_standard_version SET is_current = 0, effective_to = @from WHERE inspection_standard_version_id = @Id",
                    new { from, Id = current.InspectionStandardVersionId }, tx);
            var versionId = await InsertVersionAsync(conn, tx, id, versionNo, from, r.Remark, user.UserId);
            await InsertCriteriaAsync(conn, tx, versionId, criteria);
            created = true;
        }
        else
        {
            versionNo = current.VersionNo;
            await conn.ExecuteAsync(
                """
                DELETE p FROM inspection_criteria_point p JOIN inspection_criteria c ON c.inspection_criteria_id = p.inspection_criteria_id
                 WHERE c.inspection_standard_version_id = @Id
                """, new { Id = current.InspectionStandardVersionId }, tx);
            await conn.ExecuteAsync("DELETE FROM inspection_criteria WHERE inspection_standard_version_id = @Id", new { Id = current.InspectionStandardVersionId }, tx);
            await InsertCriteriaAsync(conn, tx, current.InspectionStandardVersionId, criteria);
            if (r.Remark is not null)
                await conn.ExecuteAsync("UPDATE inspection_standard_version SET remark = @Remark WHERE inspection_standard_version_id = @Id", new { r.Remark, Id = current.InspectionStandardVersionId }, tx);
            created = false;
        }
        await audit.WriteAsync(conn, tx, AuditAction.Update, "inspection_standard", id,
            new { version_no = current?.VersionNo, criteria = before }, new { version_no = versionNo, new_version = created, criteria }, r.Remark);
        await tx.CommitAsync(ct);
        return Results.Ok(new { versionNo, newVersion = created });
    }

    private static List<CriteriaInput> Validate(CriteriaInput[]? input, CommonCodeCache codes)
    {
        var list = (input ?? []).ToList();
        if (list.Count == 0)
            throw new RequestValidationException("criteria", "검사 항목을 1개 이상 입력하세요.");
        var errors = new List<string>();
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            var row = $"{i + 1}행";
            if (string.IsNullOrWhiteSpace(c.ItemName) || c.ItemName.Trim().Length > 100) errors.Add($"{row}: 항목명은 1~100자");
            if (c.ItemType is not null && codes.GetGroup("INSPECTION_ITEM_TYPE").Codes.All(x => x.Code != c.ItemType)) errors.Add($"{row}: 없는 항목 유형");
            var range = codes.GetGroup("RANGE_TYPE").Codes.FirstOrDefault(x => x.Code == (c.RangeType ?? "NONE"));
            if (range is null) errors.Add($"{row}: 없는 판정 방식");
            else
            {
                var needLower = range.AttrJson?.Contains("\"lower\":true") == true;
                var needUpper = range.AttrJson?.Contains("\"upper\":true") == true;
                if (needLower && c.LowerLimit is null) errors.Add($"{row}: {range.CodeName} 은 하한이 필요");
                if (needUpper && c.UpperLimit is null) errors.Add($"{row}: {range.CodeName} 은 상한이 필요");
            }
            if (c.LowerLimit is { } lo && c.UpperLimit is { } hi && lo > hi) errors.Add($"{row}: 하한이 상한보다 큼");
            if (c.SampleCount < 1 || c.TestCount < 1) errors.Add($"{row}: 시료수·시험수는 1 이상");
            if ((c.Points?.Length ?? 0) > MaxPoints) errors.Add($"{row}: 측정 위치는 {MaxPoints}개 이하");
            foreach (var (value, max, name) in new[] { (c.Location, 100, "위치"), (c.SpecificationValue, 255, "요구사항"), (c.ToolName, 100, "측정기"),
                         (c.TestValue, 100, "시험값"), (c.Scale, 50, "스케일"), (c.UnitCode, 20, "단위") })
                if (value?.Trim().Length > max) errors.Add($"{row}: {name}은 {max}자 이하");
        }
        if (errors.Count > 0)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["criteria"] = errors.ToArray() });
        return list;
    }

    private static Task<long> InsertVersionAsync(MySqlConnection conn, MySqlTransaction tx, long id, int versionNo, DateTime from, string? remark, long? userId) =>
        conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO inspection_standard_version (inspection_standard_id, version_no, effective_from, is_current, remark, created_by)
            VALUES (@id, @versionNo, @from, 1, @remark, @userId); SELECT LAST_INSERT_ID();
            """, new { id, versionNo, from, remark = remark?.Trim(), userId }, tx);

    private static async Task InsertCriteriaAsync(MySqlConnection conn, MySqlTransaction tx, long versionId, List<CriteriaInput> criteria)
    {
        static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        for (var i = 0; i < criteria.Count; i++)
        {
            var c = criteria[i];
            var criteriaId = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO inspection_criteria (inspection_standard_version_id, sequence_no, item_type, item_name, location, specification_value, tool_name,
                                                 test_value, scale, range_type, lower_limit, upper_limit, hardness_limit, unit_code, sample_count, test_count)
                VALUES (@versionId, @seq, @type, @name, @location, @spec, @tool, @test, @scale, @range, @LowerLimit, @UpperLimit, @HardnessLimit, @unit,
                        @SampleCount, @TestCount); SELECT LAST_INSERT_ID();
                """,
                new
                {
                    versionId, seq = i + 1, type = c.ItemType, name = c.ItemName!.Trim(), location = T(c.Location), spec = T(c.SpecificationValue),
                    tool = T(c.ToolName), test = T(c.TestValue), scale = T(c.Scale), range = c.RangeType ?? "NONE", c.LowerLimit, c.UpperLimit,
                    c.HardnessLimit, unit = T(c.UnitCode), c.SampleCount, c.TestCount,
                }, tx);
            var points = c.Points ?? [];
            for (var p = 0; p < points.Length; p++)
                await conn.ExecuteAsync("INSERT INTO inspection_criteria_point (inspection_criteria_id, point_no, point_label) VALUES (@criteriaId, @no, @label)",
                    new { criteriaId, no = p + 1, label = T(points[p]) }, tx);
        }
    }
}

public sealed class DuplicateInspectionStandardException(long existingId)
    : AppException(StatusCodes.Status422UnprocessableEntity, "DUPLICATE_INSPECTION_STANDARD", "이 품목·거래처의 검사기준이 이미 있습니다. 그 기준을 수정하세요.")
{
    public override IReadOnlyDictionary<string, object?> Extra => new Dictionary<string, object?> { ["inspectionStandardId"] = existingId };
}
