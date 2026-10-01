using Dapper;
using JiMes.Api.Features.Scheduling;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Features.Equipment;

public sealed class DowntimeDto
{
    public long EquipmentDowntimeId { get; init; }
    public long EquipmentId { get; init; }
    public string EquipmentName { get; init; } = "";
    public string? EquipmentTypeName { get; init; }
    public DateTime DowntimeDate { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public decimal? DurationMin { get; init; }
    public bool IsPlanned { get; init; }
    public string? ReasonCode { get; init; }
    public long? ReporterEmployeeId { get; init; }
    public string? ReporterName { get; init; }
    public string? Remark { get; init; }
}

public sealed record DowntimeSaveRequest(
    long? EquipmentId, DateTime? StartedAt, DateTime? EndedAt, decimal? DurationMin, bool IsPlanned,
    string? ReasonCode, long? ReporterEmployeeId, string? Remark);

public sealed record DowntimeEndRequest(DateTime? EndedAt);

/// <summary>
/// 설비 비가동 — 구 F_DowntimeInput(입력) + F_DowntimeStatus(현황)을 한 화면으로 (설계 §26.2 A, §28.1).
/// 계획 비가동(is_planned) = 스케줄 계산의 제외 구간 → 저장·삭제하면 그 설비 계획을 다시 계산한다.
/// 고장 비가동은 종료 시각 없이 등록(진행 중) → [종료] 로 끝낸다. 시간(분)은 시작·종료가 있으면 계산값.
/// 구 기본값 08~09시 하드코딩 제거 — 화면이 지금 시각을 기본으로 넣는다.
/// </summary>
public sealed class DowntimeService(
    IDbConnectionFactory db, AuditWriter audit, ICurrentUser currentUser, CommonCodeCache codes, TimeProvider time, SchedulingService scheduling)
{
    private const string Table = "equipment_downtime";
    public const string ReasonGroup = "DOWNTIME_REASON";

    private const string SelectSql =
        """
        SELECT d.equipment_downtime_id, d.equipment_id, e.equipment_name, et.equipment_type_name, d.downtime_date, d.started_at, d.ended_at,
               d.duration_min, d.is_planned, d.reason_code, d.reporter_employee_id, r.employee_name AS reporter_name, d.remark
          FROM equipment_downtime d
          JOIN equipment e ON e.equipment_id = d.equipment_id
          LEFT JOIN equipment_type et ON et.equipment_type_id = e.equipment_type_id
          LEFT JOIN employee r ON r.employee_id = d.reporter_employee_id
        """;

    /// <summary>기간에 걸친 비가동 + 기간과 관계없이 진행 중(종료 없음)인 것</summary>
    public async Task<IEnumerable<DowntimeDto>> ListAsync(DateOnly from, DateOnly to, long? equipmentId, bool? planned, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.QueryAsync<DowntimeDto>(
            SelectSql + """
             WHERE (d.downtime_date BETWEEN @from AND @to OR (d.ended_at IS NULL AND d.started_at IS NOT NULL AND d.duration_min IS NULL))
               AND (@equipmentId IS NULL OR d.equipment_id = @equipmentId)
               AND (@planned IS NULL OR d.is_planned = @planned)
             ORDER BY d.downtime_date DESC, d.started_at DESC, d.equipment_downtime_id DESC
            """, new { from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue), equipmentId, planned });
    }

    public async Task<long> CreateAsync(DowntimeSaveRequest r, CancellationToken ct)
    {
        var v = Validate(r);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await EnsureReferencesAsync(conn, tx, r);
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO equipment_downtime (equipment_id, downtime_date, started_at, ended_at, duration_min, is_planned, reason_code,
                                            reporter_employee_id, remark, created_by, updated_by)
            VALUES (@EquipmentId, @date, @StartedAt, @EndedAt, @duration, @IsPlanned, @reason, @ReporterEmployeeId, @remark, @UserId, @UserId);
            SELECT LAST_INSERT_ID();
            """, Params(r, v), tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, Table, id, null, r);
        await tx.CommitAsync(ct);
        if (r.IsPlanned) await scheduling.RecalculateAsync(r.EquipmentId!.Value, ct);
        return id;
    }

    public async Task UpdateAsync(long id, DowntimeSaveRequest r, CancellationToken ct)
    {
        var v = Validate(r);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id);
        await EnsureReferencesAsync(conn, tx, r);
        await conn.ExecuteAsync(
            """
            UPDATE equipment_downtime SET equipment_id = @EquipmentId, downtime_date = @date, started_at = @StartedAt, ended_at = @EndedAt,
                   duration_min = @duration, is_planned = @IsPlanned, reason_code = @reason, reporter_employee_id = @ReporterEmployeeId,
                   remark = @remark, updated_by = @UserId
             WHERE equipment_downtime_id = @id
            """, Params(r, v, id), tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id, before, r);
        await tx.CommitAsync(ct);
        await RecalculateAsync(before, r.IsPlanned ? r.EquipmentId : null, ct);
    }

    /// <summary>진행 중 비가동 종료 (기본 = 지금)</summary>
    public async Task EndAsync(long id, DowntimeEndRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id);
        if (before.EndedAt is not null) throw new BusinessRuleException("DOWNTIME_ALREADY_ENDED", "이미 종료된 비가동입니다.");
        if (before.StartedAt is null) throw new BusinessRuleException("DOWNTIME_NO_START", "시작 시각이 없는 비가동은 수정 창에서 시간을 입력하세요.");
        var end = Minute(r.EndedAt ?? time.GetLocalNow().DateTime);
        if (end < before.StartedAt) throw new RequestValidationException("endedAt", "종료 시각이 시작보다 앞입니다.");
        await conn.ExecuteAsync(
            "UPDATE equipment_downtime SET ended_at = @end, duration_min = @duration, updated_by = @UserId WHERE equipment_downtime_id = @id",
            new { id, end, duration = (decimal)(end - before.StartedAt.Value).TotalMinutes, currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id, new { before.EndedAt }, new { endedAt = end });
        await tx.CommitAsync(ct);
        await RecalculateAsync(before, null, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id);
        await conn.ExecuteAsync("DELETE FROM equipment_downtime WHERE equipment_downtime_id = @id", new { id }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Delete, Table, id, before, null);
        await tx.CommitAsync(ct);
        await RecalculateAsync(before, null, ct);
    }

    private sealed record Derived(DateTime Date, decimal? Duration);

    private Derived Validate(DowntimeSaveRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.EquipmentId is null) errors["equipmentId"] = ["설비를 고르세요."];
        if (r.StartedAt is null) errors["startedAt"] = ["시작 시각을 입력하세요."];
        if (r.StartedAt is not null && r.EndedAt is not null && r.EndedAt < r.StartedAt) errors["endedAt"] = ["종료 시각이 시작보다 앞입니다."];
        if (r.IsPlanned && r.EndedAt is null) errors["endedAt"] = ["계획 비가동은 종료 시각이 필요합니다 (스케줄 제외 구간)."];
        if (r.DurationMin is < 0) errors["durationMin"] = ["시간은 0 이상입니다."];
        if (r.ReasonCode is { Length: > 0 } && codes.GetGroup(ReasonGroup).Codes.All(c => c.Code != r.ReasonCode || !c.IsActive))
            errors["reasonCode"] = ["비가동 사유를 목록에서 고르세요."];
        if (r.Remark is { Length: > 500 }) errors["remark"] = ["비고는 500자 이하입니다."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        var start = Minute(r.StartedAt!.Value);
        var duration = r.EndedAt is { } end ? (decimal)(Minute(end) - start).TotalMinutes : r.DurationMin;
        return new Derived(start.Date, duration);
    }

    private object Params(DowntimeSaveRequest r, Derived v, long id = 0) => new
    {
        id, r.EquipmentId, date = v.Date, StartedAt = Minute(r.StartedAt!.Value), EndedAt = r.EndedAt is { } e ? Minute(e) : (DateTime?)null,
        duration = v.Duration, r.IsPlanned, reason = string.IsNullOrWhiteSpace(r.ReasonCode) ? null : r.ReasonCode,
        r.ReporterEmployeeId, remark = string.IsNullOrWhiteSpace(r.Remark) ? null : r.Remark.Trim(), currentUser.UserId,
    };

    private static async Task EnsureReferencesAsync(MySqlConnection conn, MySqlTransaction tx, DowntimeSaveRequest r)
    {
        if (!await conn.ExecuteScalarAsync<bool>("SELECT COUNT(*) > 0 FROM equipment WHERE equipment_id = @EquipmentId", new { r.EquipmentId }, tx))
            throw new RequestValidationException("equipmentId", "설비가 없습니다.");
        if (r.ReporterEmployeeId is not null
            && !await conn.ExecuteScalarAsync<bool>("SELECT COUNT(*) > 0 FROM employee WHERE employee_id = @ReporterEmployeeId", new { r.ReporterEmployeeId }, tx))
            throw new RequestValidationException("reporterEmployeeId", "사원이 없습니다.");
    }

    private static async Task<DowntimeDto> LockAsync(MySqlConnection conn, MySqlTransaction tx, long id) =>
        await conn.QuerySingleOrDefaultAsync<DowntimeDto>(SelectSql + " WHERE d.equipment_downtime_id = @id FOR UPDATE", new { id }, tx)
        ?? throw new NotFoundException(Table, id);

    /// <summary>계획 비가동이 바뀐 설비만 스케줄 재계산 (전·후 설비가 다르면 둘 다)</summary>
    private async Task RecalculateAsync(DowntimeDto before, long? plannedAfterEquipmentId, CancellationToken ct)
    {
        var ids = new HashSet<long>();
        if (before.IsPlanned) ids.Add(before.EquipmentId);
        if (plannedAfterEquipmentId is { } a) ids.Add(a);
        foreach (var e in ids) await scheduling.RecalculateAsync(e, ct);
    }

    private static DateTime Minute(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);
}
