using Dapper;
using JiMes.Api.Features.Master.Parts;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Files;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Equipment;

public sealed class MaintenanceDto
{
    public long MaintenanceId { get; init; }
    public long EquipmentId { get; init; }
    public string EquipmentName { get; init; } = "";
    public string MaintenanceType { get; init; } = "";
    public DateTime MaintenanceDate { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public long? WorkerEmployeeId { get; init; }
    public string? WorkerName { get; init; }
    public string? Description { get; init; }
    public string? Result { get; init; }
    public string? RepairPart { get; init; }
    public string? VendorName { get; init; }
    public decimal? Cost { get; init; }
    public DateTime? NextDueDate { get; init; }
    public string Status { get; init; } = "";
    public long AttachmentCount { get; init; }
    public int RowVersion { get; init; }
}

public sealed record MaintenanceDetail(MaintenanceDto Maintenance, IReadOnlyList<AttachmentDto> Attachments);

/// <summary>설비별 다음 점검 — 그 설비의 가장 최근 보전 기록에 적은 예정일</summary>
public sealed class MaintenanceDueDto
{
    public long EquipmentId { get; init; }
    public string EquipmentName { get; init; } = "";
    public DateTime? LastMaintenanceDate { get; init; }
    public DateTime? NextDueDate { get; init; }
    /// <summary>OVERDUE 지남 / DUE_SOON 임박(설정 maintenance.due_soon_days) / null</summary>
    public string? DueState { get; set; }
}

public sealed record MaintenanceSaveRequest(
    int? RowVersion, long? EquipmentId, string? MaintenanceType, DateOnly? MaintenanceDate, DateTime? StartedAt, DateTime? CompletedAt,
    long? WorkerEmployeeId, string? Description, string? Result, string? RepairPart, string? VendorName, decimal? Cost,
    DateOnly? NextDueDate, string? Status);

/// <summary>
/// 설비 보전·수리 — 구 F_MaintenanceForm (설계 §28.3). 측정기구 교정은 별도(CalibrationService, 결정 B).
/// 구분 = 공통코드 MAINTENANCE_TYPE(사용자 관리), 상태 = MAINTENANCE_STATUS(접수→진행→완료/취소).
/// 다음 점검 예정일이 설정 maintenance.due_soon_days 안이면 "임박", 지났으면 "지남". 사진·자료 = 공통 첨부(MAINTENANCE_PHOTO 등).
/// </summary>
public sealed class MaintenanceService(
    IDbConnectionFactory db, AuditWriter audit, ICurrentUser currentUser, CommonCodeCache codes, SettingsCache settings, TimeProvider time,
    AttachmentStore attachments)
{
    public const string Table = "maintenance";
    public const string TypeGroup = "MAINTENANCE_TYPE";
    public const string StatusGroup = "MAINTENANCE_STATUS";

    private const string SelectSql =
        """
        SELECT m.maintenance_id, m.equipment_id, e.equipment_name, m.maintenance_type, m.maintenance_date, m.started_at, m.completed_at,
               m.worker_employee_id, w.employee_name AS worker_name, m.description, m.result, m.repair_part, m.vendor_name, m.cost,
               m.next_due_date, m.status, m.row_version,
               (SELECT COUNT(*) FROM attachment a WHERE a.owner_table = 'maintenance' AND a.owner_id = m.maintenance_id) AS attachment_count
          FROM maintenance m
          JOIN equipment e ON e.equipment_id = m.equipment_id
          LEFT JOIN employee w ON w.employee_id = m.worker_employee_id
        """;

    public async Task<IEnumerable<MaintenanceDto>> ListAsync(DateOnly from, DateOnly to, long? equipmentId, string? status, string? search, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.QueryAsync<MaintenanceDto>(
            SelectSql + """
             WHERE m.maintenance_date BETWEEN @from AND @to
               AND (@equipmentId IS NULL OR m.equipment_id = @equipmentId) AND (@status IS NULL OR m.status = @status)
               AND (@search IS NULL OR m.description LIKE @like OR m.repair_part LIKE @like OR m.vendor_name LIKE @like OR m.result LIKE @like)
             ORDER BY m.maintenance_date DESC, m.maintenance_id DESC
             LIMIT 2000
            """,
            new
            {
                from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue), equipmentId, status = Empty(status),
                search = Empty(search?.Trim()), like = $"%{search?.Trim()}%",
            });
    }

    /// <summary>설비별 다음 점검 예정 (예정일이 있는 설비만, 가까운 순)</summary>
    public async Task<IEnumerable<MaintenanceDueDto>> DueAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<MaintenanceDueDto>(
            """
            SELECT e.equipment_id, e.equipment_name, x.last_date AS last_maintenance_date,
                   (SELECT m.next_due_date FROM maintenance m WHERE m.equipment_id = e.equipment_id AND m.status <> 'CANCELLED'
                     ORDER BY m.maintenance_date DESC, m.maintenance_id DESC LIMIT 1) AS next_due_date
              FROM equipment e
              JOIN (SELECT equipment_id, MAX(maintenance_date) AS last_date FROM maintenance WHERE status <> 'CANCELLED' GROUP BY equipment_id) x
                ON x.equipment_id = e.equipment_id
             WHERE e.is_active = 1
            """)).Where(r => r.NextDueDate is not null).OrderBy(r => r.NextDueDate).ToList();
        var today = Today();
        foreach (var r in rows) r.DueState = DueState(r.NextDueDate, today, settings.GetInt(SettingKeys.MaintenanceDueSoonDays));
        return rows;
    }

    public async Task<MaintenanceDetail> GetAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var m = await conn.QuerySingleOrDefaultAsync<MaintenanceDto>(SelectSql + " WHERE m.maintenance_id = @id", new { id })
            ?? throw new NotFoundException(Table, id);
        return new MaintenanceDetail(m, await AttachmentStore.ListAsync(conn, null, Table, id));
    }

    public async Task<long> CreateAsync(MaintenanceSaveRequest r, CancellationToken ct)
    {
        var v = Validate(r, null);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await EnsureReferencesAsync(conn, tx, r);
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO maintenance (equipment_id, maintenance_type, maintenance_date, started_at, completed_at, worker_employee_id, description, result,
                                     repair_part, vendor_name, cost, next_due_date, status, created_by, updated_by)
            VALUES (@EquipmentId, @MaintenanceType, @date, @StartedAt, @completedAt, @WorkerEmployeeId, @description, @result,
                    @repairPart, @vendorName, @Cost, @nextDue, @status, @UserId, @UserId);
            SELECT LAST_INSERT_ID();
            """, Params(r, v, 0), tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, Table, id, null, r);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task UpdateAsync(long id, MaintenanceSaveRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id, r.RowVersion);
        var v = Validate(r, before);
        await EnsureReferencesAsync(conn, tx, r);
        await conn.ExecuteAsync(
            """
            UPDATE maintenance SET equipment_id = @EquipmentId, maintenance_type = @MaintenanceType, maintenance_date = @date, started_at = @StartedAt,
                   completed_at = @completedAt, worker_employee_id = @WorkerEmployeeId, description = @description, result = @result,
                   repair_part = @repairPart, vendor_name = @vendorName, cost = @Cost, next_due_date = @nextDue, status = @status,
                   updated_by = @UserId, row_version = row_version + 1
             WHERE maintenance_id = @id
            """, Params(r, v, id), tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id, before, r);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, int rowVersion, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id, rowVersion);
        await conn.ExecuteAsync("DELETE FROM attachment WHERE owner_table = @Table AND owner_id = @id", new { Table, id }, tx);
        await conn.ExecuteAsync("DELETE FROM maintenance WHERE maintenance_id = @id", new { id }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Delete, Table, id, before, null);
        await tx.CommitAsync(ct);
    }

    public Task<long> AddAttachmentAsync(long id, string kind, string fileName, string? contentType, byte[] content, string? caption, CancellationToken ct) =>
        attachments.AddAsync(Table, id, kind, fileName, contentType, content, caption, ct);

    public Task<(byte[] Content, string FileName, string ContentType)> GetAttachmentAsync(long id, long attachmentId, CancellationToken ct) =>
        attachments.GetAsync(Table, id, attachmentId, ct);

    public Task DeleteAttachmentAsync(long id, long attachmentId, CancellationToken ct) => attachments.DeleteAsync(Table, id, attachmentId, ct);

    /// <summary>예정일 상태 — 지남 / 임박(오늘 + 기준 일수 이내)</summary>
    internal static string? DueState(DateTime? due, DateOnly today, int soonDays) =>
        due is not { } d ? null
        : DateOnly.FromDateTime(d) < today ? "OVERDUE"
        : DateOnly.FromDateTime(d) <= today.AddDays(soonDays) ? "DUE_SOON"
        : null;

    private sealed record Derived(string Status, DateTime? CompletedAt);

    private Derived Validate(MaintenanceSaveRequest r, MaintenanceDto? before)
    {
        var errors = new Dictionary<string, string[]>();
        var status = string.IsNullOrEmpty(r.Status) ? "OPEN" : r.Status;
        if (r.EquipmentId is null) errors["equipmentId"] = ["설비를 고르세요."];
        if (r.MaintenanceDate is null) errors["maintenanceDate"] = ["보전일을 입력하세요."];
        // 이관한 구 문자열 구분은 바꾸지 않으면 그대로 둘 수 있다
        if (string.IsNullOrWhiteSpace(r.MaintenanceType)
            || (r.MaintenanceType != before?.MaintenanceType && codes.GetGroup(TypeGroup).Codes.All(c => c.Code != r.MaintenanceType || !c.IsActive)))
            errors["maintenanceType"] = ["보전 구분을 고르세요."];
        if (codes.GetGroup(StatusGroup).Codes.All(c => c.Code != status)) errors["status"] = ["상태 값이 올바르지 않습니다."];
        if (r.StartedAt is { } s && r.CompletedAt is { } c && c < s) errors["completedAt"] = ["완료 시각이 시작보다 앞입니다."];
        if (r.MaintenanceDate is { } md && r.NextDueDate is { } nd && nd < md) errors["nextDueDate"] = ["다음 점검일이 보전일보다 앞입니다."];
        if (r.Cost is < 0) errors["cost"] = ["비용은 0 이상입니다."];
        if (r.RepairPart is { Length: > 100 }) errors["repairPart"] = ["100자 이하입니다."];
        if (r.VendorName is { Length: > 100 }) errors["vendorName"] = ["100자 이하입니다."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        // 완료로 저장하는데 완료 시각이 없으면: 오늘 보전 = 지금, 지난 날짜 보전 = 그날 (시작 시각이 있으면 시작 시각)
        var now = time.GetLocalNow().DateTime;
        var defaultCompleted = r.MaintenanceDate!.Value >= DateOnly.FromDateTime(now)
            ? now
            : r.StartedAt ?? r.MaintenanceDate.Value.ToDateTime(TimeOnly.MinValue);
        var completedAt = status == "COMPLETED" ? r.CompletedAt ?? before?.CompletedAt ?? defaultCompleted : r.CompletedAt;
        return new Derived(status, completedAt);
    }

    private object Params(MaintenanceSaveRequest r, Derived v, long id) => new
    {
        id, r.EquipmentId, r.MaintenanceType, date = r.MaintenanceDate!.Value.ToDateTime(TimeOnly.MinValue), r.StartedAt, completedAt = v.CompletedAt,
        r.WorkerEmployeeId, description = Trim(r.Description), result = Trim(r.Result), repairPart = Trim(r.RepairPart), vendorName = Trim(r.VendorName),
        r.Cost, nextDue = r.NextDueDate?.ToDateTime(TimeOnly.MinValue), status = v.Status, currentUser.UserId,
    };

    private static async Task EnsureReferencesAsync(MySqlConnection conn, MySqlTransaction tx, MaintenanceSaveRequest r)
    {
        if (!await conn.ExecuteScalarAsync<bool>("SELECT COUNT(*) > 0 FROM equipment WHERE equipment_id = @EquipmentId", new { r.EquipmentId }, tx))
            throw new RequestValidationException("equipmentId", "설비가 없습니다.");
        if (r.WorkerEmployeeId is not null
            && !await conn.ExecuteScalarAsync<bool>("SELECT COUNT(*) > 0 FROM employee WHERE employee_id = @WorkerEmployeeId", new { r.WorkerEmployeeId }, tx))
            throw new RequestValidationException("workerEmployeeId", "사원이 없습니다.");
    }

    private static async Task<MaintenanceDto> LockAsync(MySqlConnection conn, MySqlTransaction tx, long id, int? rowVersion)
    {
        var version = await conn.ExecuteScalarAsync<int?>("SELECT row_version FROM maintenance WHERE maintenance_id = @id FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException(Table, id);
        if (version != rowVersion) throw new ConcurrencyConflictException(Table, id);
        return await conn.QuerySingleAsync<MaintenanceDto>(SelectSql + " WHERE m.maintenance_id = @id", new { id }, tx);
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
    private static string? Empty(string? s) => string.IsNullOrEmpty(s) ? null : s;
    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
