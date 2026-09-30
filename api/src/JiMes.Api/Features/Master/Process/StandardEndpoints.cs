using System.Globalization;
using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Numbering;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Master.Process;

public sealed class StandardDto
{
    public long StandardId { get; init; }
    public string StandardCode { get; init; } = "";
    public string StandardName { get; init; } = "";
    public long PartId { get; init; }
    public string? PartCode { get; init; }
    public string? PartName { get; init; }
    public long? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public long? HeatProcessId { get; init; }
    public string? HeatProcessName { get; init; }
    public long UnitProcessId { get; init; }
    public string? UnitProcessName { get; init; }
    public long? EquipmentTypeId { get; init; }
    public string? EquipmentTypeName { get; init; }
    public long? EquipmentId { get; init; }
    public string? EquipmentName { get; init; }
    public bool IsActive { get; init; }
    public int? CurrentVersionNo { get; init; }
    public decimal? RunningTimeMin { get; init; }
    public decimal? ChargeQty { get; init; }
    public string? ChargeUnit { get; init; }
    public string? StepTemplateName { get; init; }
    public DateTime? EffectiveFrom { get; init; }
}

public sealed class StandardVersionDto
{
    public long StandardVersionId { get; init; }
    public int VersionNo { get; init; }
    public long? StepTemplateId { get; init; }
    public decimal ChargeQty { get; init; }
    public string ChargeUnit { get; init; } = "";
    public decimal? RunningTimeMin { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public bool IsCurrent { get; init; }
    public string? Remark { get; init; }
    public string? CreatedByName { get; init; }
    public long UsageCount { get; init; }
}

public sealed class ConditionValueDto
{
    public long? StepTemplateItemId { get; init; }
    public long ConditionItemId { get; init; }
    public string? ConditionValue { get; init; }
}

public sealed record ConditionInput(long? StepTemplateItemId, long ConditionItemId, string? ConditionValue);

public sealed record StandardVersionInput(
    long? StepTemplateId, decimal ChargeQty, string? ChargeUnit, decimal? RunningTimeMin, string? Remark, ConditionInput[]? Conditions);

public sealed record StandardCreateRequest(
    long PartId, long? CustomerId, long? HeatProcessId, long UnitProcessId, long? EquipmentTypeId, long? EquipmentId,
    string? StandardName, StandardVersionInput Version);

public sealed record StandardUpdateRequest(string? StandardName, bool IsActive);

/// <summary>
/// 작업표준 (구 F_WorkStandardAddForm) — 품목 × 단위공정 × 설비(유형) × (거래처·공정). 조건 = 관리항목 × [공통 + 단계] 행렬.
/// 저장할 때마다 새 Version (구 "새 행 INSERT, 기존 보존"과 같음, §1.3·T2). 작업 LOT 이 쓴 Version 은 그대로 남는다.
/// 설비 "All" 복사 대신 설비 유형 지정 = 유형 공통 (T3). 작업시간 단위 = 분 (T6).
/// </summary>
public static class StandardEndpoints
{
    public static void MapStandardEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/standards").WithTags("Standard");
        const string key = MenuKeys.MasterStandard;
        g.MapGet("/", ListAsync).RequirePermission(key, PermissionAction.Read);
        g.MapGet("/{id:long}", DetailAsync).RequirePermission(key, PermissionAction.Read);
        g.MapPost("/", CreateAsync).RequirePermission(key, PermissionAction.Create);
        g.MapPut("/{id:long}", UpdateAsync).RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/versions", AddVersionAsync).RequirePermission(key, PermissionAction.Update);
    }

    private const string ListSql =
        """
        SELECT s.standard_id, s.standard_code, s.standard_name, s.part_id, p.part_code, p.part_name, s.customer_id, c.customer_name,
               s.heat_process_id, h.heat_process_name, s.unit_process_id, u.unit_process_name, s.equipment_type_id, et.equipment_type_name,
               s.equipment_id, e.equipment_name, s.is_active, v.version_no AS current_version_no, v.running_time_min, v.charge_qty, v.charge_unit,
               st.step_template_name, v.effective_from
          FROM standard s
          JOIN part p               ON p.part_id = s.part_id
          JOIN unit_process u       ON u.unit_process_id = s.unit_process_id
          LEFT JOIN customer c      ON c.customer_id = s.customer_id
          LEFT JOIN heat_process h  ON h.heat_process_id = s.heat_process_id
          LEFT JOIN equipment_type et ON et.equipment_type_id = s.equipment_type_id
          LEFT JOIN equipment e     ON e.equipment_id = s.equipment_id
          LEFT JOIN standard_version v ON v.standard_id = s.standard_id AND v.is_current = 1
          LEFT JOIN step_template st ON st.step_template_id = v.step_template_id
        """;

    private static async Task<IResult> ListAsync(IDbConnectionFactory db, CancellationToken ct,
        string? search = null, long? partId = null, long? unitProcessId = null, bool includeInactive = false)
    {
        await using var conn = await db.OpenAsync(ct);
        return Results.Ok(await conn.QueryAsync<StandardDto>(
            ListSql + """
             WHERE (@includeInactive OR s.is_active = 1) AND (@partId IS NULL OR s.part_id = @partId)
               AND (@unitProcessId IS NULL OR s.unit_process_id = @unitProcessId)
               AND (@search IS NULL OR s.standard_code LIKE @like OR s.standard_name LIKE @like OR p.part_code LIKE @like
                    OR p.part_name LIKE @like OR p.part_number LIKE @like)
             ORDER BY p.part_name, u.sort_order, et.equipment_type_name
             LIMIT 500
            """, new { includeInactive, partId, unitProcessId, search = string.IsNullOrWhiteSpace(search) ? null : search, like = $"%{search?.Trim()}%" }));
    }

    /// <param name="versionId">과거 Version 보기. 없으면 현재 Version</param>
    private static async Task<IResult> DetailAsync(long id, IDbConnectionFactory db, CancellationToken ct, long? versionId = null)
    {
        await using var conn = await db.OpenAsync(ct);
        var header = await conn.QuerySingleOrDefaultAsync<StandardDto>(ListSql + " WHERE s.standard_id = @id", new { id })
            ?? throw new NotFoundException("standard", id);
        var versions = (await conn.QueryAsync<StandardVersionDto>(
            """
            SELECT v.standard_version_id, v.version_no, v.step_template_id, v.charge_qty, v.charge_unit, v.running_time_min, v.effective_from,
                   v.effective_to, v.is_current, v.remark, u.user_name AS created_by_name,
                   (SELECT COUNT(*) FROM production_work w WHERE w.standard_version_id = v.standard_version_id) AS usage_count
              FROM standard_version v LEFT JOIN app_user u ON u.app_user_id = v.created_by
             WHERE v.standard_id = @id ORDER BY v.version_no DESC
            """, new { id })).ToList();
        var version = versionId is null ? versions.FirstOrDefault(v => v.IsCurrent) : versions.FirstOrDefault(v => v.StandardVersionId == versionId)
            ?? throw new NotFoundException("standard_version", versionId.Value);
        var template = version?.StepTemplateId is { } tid ? await StepTemplateEndpoints.DetailAsync(conn, null, tid) : null;
        var conditions = version is null ? [] : (await conn.QueryAsync<ConditionValueDto>(
            "SELECT step_template_item_id, condition_item_id, condition_value FROM standard_condition WHERE standard_version_id = @Id",
            new { Id = version.StandardVersionId })).ToList();
        return Results.Ok(new { header, versions, version, template, conditions });
    }

    private static async Task<IResult> CreateAsync(StandardCreateRequest r, IDbConnectionFactory db, AuditWriter audit, SettingsCache settings,
        ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var part = await conn.QuerySingleOrDefaultAsync<(string Code, string Name)>("SELECT part_code, part_name FROM part WHERE part_id = @PartId", r, tx);
        if (part.Code is null) throw new RequestValidationException("partId", "품목을 선택하세요.");
        var unit = await conn.QuerySingleOrDefaultAsync<(string Code, string Name)>(
            "SELECT unit_process_code, unit_process_name FROM unit_process WHERE unit_process_id = @UnitProcessId", r, tx);
        if (unit.Code is null) throw new RequestValidationException("unitProcessId", "단위공정을 선택하세요.");

        // 설비를 지정하면 설비 유형은 그 설비의 유형
        var equipmentTypeId = r.EquipmentTypeId;
        if (r.EquipmentId is { } eqId)
            equipmentTypeId = await conn.ExecuteScalarAsync<long?>("SELECT equipment_type_id FROM equipment WHERE equipment_id = @eqId", new { eqId }, tx)
                ?? throw new RequestValidationException("equipmentId", "없는 설비입니다.");

        var existing = await conn.ExecuteScalarAsync<long?>(
            """
            SELECT standard_id FROM standard
             WHERE part_id = @PartId AND unit_process_id = @UnitProcessId AND customer_id <=> @CustomerId AND heat_process_id <=> @HeatProcessId
               AND equipment_type_id <=> @equipmentTypeId AND equipment_id <=> @EquipmentId AND is_active = 1
             LIMIT 1
            """, new { r.PartId, r.UnitProcessId, r.CustomerId, r.HeatProcessId, equipmentTypeId, r.EquipmentId }, tx);
        if (existing is not null)
            throw new DuplicateStandardException(existing.Value);

        var code = await NextCodeAsync(conn, tx, settings, r.PartId, r.UnitProcessId, part.Code, unit.Code);
        var name = string.IsNullOrWhiteSpace(r.StandardName) ? $"{part.Name} {unit.Name}" : r.StandardName.Trim();
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO standard (standard_code, standard_name, part_id, customer_id, heat_process_id, unit_process_id, equipment_type_id, equipment_id,
                                  created_by, updated_by)
            VALUES (@code, @name, @PartId, @CustomerId, @HeatProcessId, @UnitProcessId, @equipmentTypeId, @EquipmentId, @u, @u); SELECT LAST_INSERT_ID();
            """, new { code, name, r.PartId, r.CustomerId, r.HeatProcessId, r.UnitProcessId, equipmentTypeId, r.EquipmentId, u = user.UserId }, tx);
        var (versionNo, _) = await InsertVersionAsync(conn, tx, id, r.UnitProcessId, r.Version, user.UserId, VersionClock.NextFrom(time, null));
        await audit.WriteAsync(conn, tx, AuditAction.Create, "standard", id, null,
            new { standard_code = code, standard_name = name, r.PartId, r.CustomerId, r.HeatProcessId, r.UnitProcessId, equipment_type_id = equipmentTypeId, r.EquipmentId, version_no = versionNo, version = r.Version });
        await tx.CommitAsync(ct);
        return Results.Ok(new { standardId = id, standardCode = code });
    }

    private static async Task<IResult> UpdateAsync(long id, StandardUpdateRequest r, IDbConnectionFactory db, AuditWriter audit, ICurrentUser user, CancellationToken ct)
    {
        var name = HeatProcessEndpoints.Required(r.StandardName, "standardName", 100);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await conn.QuerySingleOrDefaultAsync<StandardDto>(ListSql + " WHERE s.standard_id = @id FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException("standard", id);
        await conn.ExecuteAsync("UPDATE standard SET standard_name = @name, is_active = @IsActive, updated_by = @u WHERE standard_id = @id",
            new { id, name, r.IsActive, u = user.UserId }, tx);
        await audit.WriteAsync(conn, tx, before.IsActive != r.IsActive ? AuditAction.StatusChange : AuditAction.Update, "standard", id,
            new { before.StandardName, before.IsActive }, new { standard_name = name, r.IsActive });
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    /// <summary>조건·투입 기준 수정 = 새 Version (현재 Version 은 종료일을 넣고 보존)</summary>
    private static async Task<IResult> AddVersionAsync(long id, StandardVersionInput r, IDbConnectionFactory db, AuditWriter audit, ICurrentUser user,
        TimeProvider time, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var unitProcessId = await conn.ExecuteScalarAsync<long?>("SELECT unit_process_id FROM standard WHERE standard_id = @id FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException("standard", id);
        var previous = await conn.QuerySingleOrDefaultAsync<(int VersionNo, DateTime From)>(
            "SELECT version_no, effective_from FROM standard_version WHERE standard_id = @id AND is_current = 1", new { id }, tx);
        var from = VersionClock.NextFrom(time, previous.VersionNo == 0 ? null : previous.From);

        // 검증·삽입이 먼저 (잘못된 입력이면 이전 버전을 건드리지 않음), 그다음 이전 버전 종료 → 새 버전 현재
        var (versionNo, versionId) = await InsertVersionAsync(conn, tx, id, unitProcessId, r, user.UserId, from, isCurrent: false);
        await conn.ExecuteAsync("UPDATE standard_version SET is_current = 0, effective_to = @from WHERE standard_id = @id AND is_current = 1", new { id, from }, tx);
        await conn.ExecuteAsync("UPDATE standard_version SET is_current = 1 WHERE standard_version_id = @versionId", new { versionId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, "standard", id, new { version_no = previous.VersionNo }, new { version_no = versionNo, standard_version_id = versionId, version = r }, r.Remark);
        await tx.CommitAsync(ct);
        return Results.Ok(new { versionNo, standardVersionId = versionId });
    }

    private static async Task<(int VersionNo, long VersionId)> InsertVersionAsync(
        MySqlConnection conn, MySqlTransaction tx, long standardId, long unitProcessId, StandardVersionInput r, long? userId, DateTime now, bool isCurrent = true)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.ChargeQty < 0) errors["chargeQty"] = ["0 이상이어야 합니다."];
        if (r.RunningTimeMin is <= 0) errors["runningTimeMin"] = ["0보다 커야 합니다."];
        var chargeUnit = string.IsNullOrWhiteSpace(r.ChargeUnit) ? "charge" : r.ChargeUnit.Trim();
        if (chargeUnit.Length > 20) errors["chargeUnit"] = ["20자 이하여야 합니다."];

        HashSet<long> stepIds = [];
        if (r.StepTemplateId is { } templateId)
        {
            var template = await StepTemplateEndpoints.DetailAsync(conn, tx, templateId);
            if (template is null) errors["stepTemplateId"] = ["없는 단계 템플릿입니다."];
            else if (template.Header.UnitProcessId != unitProcessId) errors["stepTemplateId"] = ["작업표준과 단위공정이 다른 템플릿입니다."];
            else stepIds = template.Steps.Select(s => s.StepTemplateItemId).ToHashSet();
        }

        var conditions = (r.Conditions ?? []).Where(c => !string.IsNullOrWhiteSpace(c.ConditionValue)).ToList();
        var items = conditions.Count == 0 ? new Dictionary<long, string>() : (await conn.QueryAsync<(long Id, string ValueType)>(
                "SELECT CAST(condition_item_id AS SIGNED), value_type FROM condition_item WHERE condition_item_id IN @ids",
                new { ids = conditions.Select(c => c.ConditionItemId).Distinct().ToArray() }, tx))
            .ToDictionary(x => x.Id, x => x.ValueType);
        foreach (var c in conditions)
        {
            var value = c.ConditionValue!.Trim();
            if (!items.TryGetValue(c.ConditionItemId, out var valueType)) errors["conditions"] = ["없는 조건 항목이 있습니다."];
            else if (c.StepTemplateItemId is { } step && !stepIds.Contains(step)) errors["conditions"] = ["템플릿에 없는 단계가 있습니다."];
            else if (value.Length > 100) errors["conditions"] = ["조건값은 100자 이하여야 합니다."];
            else if (valueType == "NUMBER" && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                errors["conditions"] = [$"숫자 항목에 숫자가 아닌 값이 있습니다: '{value}'"];
        }
        if (conditions.GroupBy(c => (c.StepTemplateItemId, c.ConditionItemId)).Any(g => g.Count() > 1))
            errors["conditions"] = ["같은 단계·항목이 두 번 있습니다."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var versionNo = await conn.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(version_no), 0) + 1 FROM standard_version WHERE standard_id = @standardId", new { standardId }, tx);
        var versionId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO standard_version (standard_id, version_no, step_template_id, charge_qty, charge_unit, running_time_min, effective_from, is_current, remark, created_by)
            VALUES (@standardId, @versionNo, @StepTemplateId, @ChargeQty, @chargeUnit, @RunningTimeMin, @now, @isCurrent, @remark, @userId); SELECT LAST_INSERT_ID();
            """, new { standardId, versionNo, r.StepTemplateId, r.ChargeQty, chargeUnit, r.RunningTimeMin, now, isCurrent, remark = r.Remark?.Trim(), userId }, tx);
        foreach (var c in conditions)
            await conn.ExecuteAsync(
                "INSERT INTO standard_condition (standard_version_id, step_template_item_id, condition_item_id, condition_value) VALUES (@versionId, @StepTemplateItemId, @ConditionItemId, @value)",
                new { versionId, c.StepTemplateItemId, c.ConditionItemId, value = c.ConditionValue!.Trim() }, tx);
        return (versionNo, versionId);
    }

    /// <summary>코드 = 설정 standard.code_format ({PART} {UNIT} {SEQ}) — 같은 품목·단위공정 순번</summary>
    private static async Task<string> NextCodeAsync(MySqlConnection conn, MySqlTransaction tx, SettingsCache settings, long partId, long unitProcessId, string partCode, string unitCode)
    {
        var format = settings.GetString(SettingKeys.StandardCodeFormat);
        var tokens = new Dictionary<string, string> { ["PART"] = partCode, ["UNIT"] = unitCode };
        var seq = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM standard WHERE part_id = @partId AND unit_process_id = @unitProcessId", new { partId, unitProcessId }, tx);
        while (true)
        {
            var code = NumberFormat.Format(format, DateOnly.FromDateTime(DateTime.Today), ++seq, tokens);
            if (code.Length > 50) throw new BusinessRuleException("CODE_TOO_LONG", "작업표준 코드가 50자를 넘습니다. 설정 standard.code_format 을 확인하세요.");
            if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM standard WHERE standard_code = @code", new { code }, tx) == 0)
                return code;
        }
    }
}

public sealed class DuplicateStandardException(long existingId)
    : AppException(StatusCodes.Status422UnprocessableEntity, "DUPLICATE_STANDARD", "같은 품목·단위공정·설비·거래처·공정의 작업표준이 이미 있습니다. 그 표준의 새 버전으로 수정하세요.")
{
    public override IReadOnlyDictionary<string, object?> Extra => new Dictionary<string, object?> { ["standardId"] = existingId };
}

/// <summary>
/// Version 적용 시작 시각 — DB DATETIME 은 초 단위(소수초 버림)라 같은 초에 연달아 저장하면
/// 이전 버전의 종료(= 새 시작)가 이전 시작과 같아져 CHECK(effective_to > effective_from)에 걸린다. 최소 1초 뒤로.
/// </summary>
internal static class VersionClock
{
    public static DateTime NextFrom(TimeProvider time, DateTime? previousFrom)
    {
        var now = time.GetLocalNow().DateTime;
        now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);
        return previousFrom is { } p && now <= p ? p.AddSeconds(1) : now;
    }
}
