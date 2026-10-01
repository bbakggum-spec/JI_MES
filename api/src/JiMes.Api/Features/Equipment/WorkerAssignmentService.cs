using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Realtime;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Features.Equipment;

public sealed class BoardShiftDto
{
    public long WorkShiftId { get; init; }
    public string WorkShiftName { get; init; } = "";
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
    public bool IsNextDayEnd { get; init; }
}

public sealed class BoardEquipmentDto
{
    public long EquipmentId { get; init; }
    public string EquipmentName { get; init; } = "";
    public string? EquipmentTypeName { get; init; }
    /// <summary>이 작업일 진행 중(투입) LOT — 배치 판단 참고</summary>
    public string? RunningLotNo { get; init; }
}

public sealed class BoardEmployeeDto
{
    public long EmployeeId { get; init; }
    public string EmployeeName { get; init; } = "";
    public string? TeamName { get; init; }
    public string? DepartmentName { get; init; }
}

public sealed class AssignmentDto
{
    public long WorkerAssignmentId { get; init; }
    public long WorkShiftId { get; init; }
    public long EquipmentId { get; init; }
    public long EmployeeId { get; init; }
    public string EmployeeName { get; init; } = "";
    public string AssignmentType { get; init; } = "";
    public string? Remark { get; init; }
}

public sealed record AssignmentBoard(
    DateOnly WorkDate, IReadOnlyList<BoardShiftDto> Shifts, IReadOnlyList<BoardEquipmentDto> Equipment,
    IReadOnlyList<BoardEmployeeDto> Employees, IReadOnlyList<AssignmentDto> Assignments);

public sealed record AssignmentCreateRequest(DateOnly? WorkDate, long? WorkShiftId, long? EquipmentId, long? EmployeeId, string? AssignmentType);
public sealed record AssignmentUpdateRequest(long? WorkShiftId, long? EquipmentId, string? AssignmentType);
public sealed record AssignmentCopyRequest(DateOnly? FromDate, DateOnly? ToDate);

/// <summary>
/// 작업자 주·야 배치 (설계 §26.2 C, §28.5) — 사람이 직접 배치 (자동 배정 없음).
/// 보드 = 작업일 × 교대(열) × 설비(행), 칸에 사원을 끌어다 놓는다. 대상 사원 = 사원 기준정보의 "작업자 배정 대상"·재직.
/// 같은 사람을 같은 교대에 여러 설비로 배치할 수 있다 (여러 대 담당). 같은 칸에 같은 사람은 한 번 (DB UNIQUE).
/// 구분 PRIMARY(주) / SUPPORT(보조). 바뀌면 SignalR workerAssignmentChanged {workDate}.
/// </summary>
public sealed class WorkerAssignmentService(IDbConnectionFactory db, AuditWriter audit, ICurrentUser currentUser, EventPublisher events)
{
    private const string Table = "worker_assignment";
    private static readonly string[] Types = ["PRIMARY", "SUPPORT"];

    private const string AssignmentSql =
        """
        SELECT a.worker_assignment_id, a.work_shift_id, a.equipment_id, a.employee_id, e.employee_name, a.assignment_type, a.remark
          FROM worker_assignment a JOIN employee e ON e.employee_id = a.employee_id
        """;

    public async Task<AssignmentBoard> BoardAsync(DateOnly workDate, CancellationToken ct)
    {
        var date = workDate.ToDateTime(TimeOnly.MinValue);
        await using var conn = await db.OpenAsync(ct);
        var shifts = await conn.QueryAsync<BoardShiftDto>(
            "SELECT work_shift_id, work_shift_name, start_time, end_time, is_next_day_end FROM work_shift WHERE is_active = 1 ORDER BY sort_order, start_time");
        var equipment = await conn.QueryAsync<BoardEquipmentDto>(
            """
            SELECT e.equipment_id, e.equipment_name, t.equipment_type_name,
                   (SELECT w.lot_no FROM production_work w WHERE w.equipment_id = e.equipment_id AND w.status = 'INPUT' AND w.is_deleted = 0 LIMIT 1) AS running_lot_no
              FROM equipment e LEFT JOIN equipment_type t ON t.equipment_type_id = e.equipment_type_id
             WHERE e.is_active = 1
             ORDER BY e.sort_order, e.equipment_code
            """);
        // 배치 대상 + 이 날 이미 배치된 사람 (대상에서 빠졌어도 보이게)
        var employees = await conn.QueryAsync<BoardEmployeeDto>(
            """
            SELECT e.employee_id, e.employee_name, e.team_name, d.department_name
              FROM employee e LEFT JOIN department d ON d.department_id = e.department_id
             WHERE (e.is_assignment_target = 1 AND e.is_active = 1)
                OR e.employee_id IN (SELECT employee_id FROM worker_assignment WHERE work_date = @date)
             ORDER BY e.team_name IS NULL, e.team_name, e.employee_name
            """, new { date });
        var assignments = await conn.QueryAsync<AssignmentDto>(AssignmentSql + " WHERE a.work_date = @date ORDER BY a.assignment_type, e.employee_name", new { date });
        return new AssignmentBoard(workDate, shifts.ToList(), equipment.ToList(), employees.ToList(), assignments.ToList());
    }

    public async Task<long> CreateAsync(AssignmentCreateRequest r, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.WorkDate is null) errors["workDate"] = ["작업일을 고르세요."];
        if (r.WorkShiftId is null) errors["workShiftId"] = ["교대를 고르세요."];
        if (r.EquipmentId is null) errors["equipmentId"] = ["설비를 고르세요."];
        if (r.EmployeeId is null) errors["employeeId"] = ["작업자를 고르세요."];
        var type = string.IsNullOrEmpty(r.AssignmentType) ? "PRIMARY" : r.AssignmentType;
        if (!Types.Contains(type)) errors["assignmentType"] = ["구분이 올바르지 않습니다."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await EnsureAsync(conn, tx, r.WorkShiftId!.Value, r.EquipmentId!.Value);
        if (!await conn.ExecuteScalarAsync<bool>("SELECT COUNT(*) > 0 FROM employee WHERE employee_id = @EmployeeId", new { r.EmployeeId }, tx))
            throw new RequestValidationException("employeeId", "사원이 없습니다.");
        var date = r.WorkDate!.Value.ToDateTime(TimeOnly.MinValue);
        if (await conn.ExecuteScalarAsync<bool>(
                "SELECT COUNT(*) > 0 FROM worker_assignment WHERE work_date = @date AND work_shift_id = @WorkShiftId AND equipment_id = @EquipmentId AND employee_id = @EmployeeId",
                new { date, r.WorkShiftId, r.EquipmentId, r.EmployeeId }, tx))
            throw new BusinessRuleException("ASSIGNMENT_DUPLICATE", "이미 그 칸에 배치된 작업자입니다.");
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO worker_assignment (work_date, work_shift_id, equipment_id, employee_id, assignment_type, created_by, updated_by)
            VALUES (@date, @WorkShiftId, @EquipmentId, @EmployeeId, @type, @UserId, @UserId);
            SELECT LAST_INSERT_ID();
            """, new { date, r.WorkShiftId, r.EquipmentId, r.EmployeeId, type, currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, Table, id, null, r);
        await tx.CommitAsync(ct);
        await PublishAsync(r.WorkDate.Value, ct);
        return id;
    }

    /// <summary>다른 칸으로 옮기기 / 주·보조 바꾸기</summary>
    public async Task UpdateAsync(long id, AssignmentUpdateRequest r, CancellationToken ct)
    {
        if (r.AssignmentType is { } t && !Types.Contains(t)) throw new RequestValidationException("assignmentType", "구분이 올바르지 않습니다.");
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id);
        var shiftId = r.WorkShiftId ?? before.WorkShiftId;
        var equipmentId = r.EquipmentId ?? before.EquipmentId;
        await EnsureAsync(conn, tx, shiftId, equipmentId);
        if ((shiftId != before.WorkShiftId || equipmentId != before.EquipmentId) && await conn.ExecuteScalarAsync<bool>(
                """
                SELECT COUNT(*) > 0 FROM worker_assignment
                 WHERE work_date = @WorkDate AND work_shift_id = @shiftId AND equipment_id = @equipmentId AND employee_id = @EmployeeId
                """, new { before.WorkDate, shiftId, equipmentId, before.EmployeeId }, tx))
            throw new BusinessRuleException("ASSIGNMENT_DUPLICATE", "이미 그 칸에 배치된 작업자입니다.");
        await conn.ExecuteAsync(
            """
            UPDATE worker_assignment SET work_shift_id = @shiftId, equipment_id = @equipmentId, assignment_type = @type, updated_by = @UserId
             WHERE worker_assignment_id = @id
            """, new { id, shiftId, equipmentId, type = r.AssignmentType ?? before.AssignmentType, currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id, before, r);
        await tx.CommitAsync(ct);
        await PublishAsync(DateOnly.FromDateTime(before.WorkDate), ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id);
        await conn.ExecuteAsync("DELETE FROM worker_assignment WHERE worker_assignment_id = @id", new { id }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Delete, Table, id, before, null);
        await tx.CommitAsync(ct);
        await PublishAsync(DateOnly.FromDateTime(before.WorkDate), ct);
    }

    /// <summary>다른 날 배치를 복사 — 대상 날에 없는 칸만 추가 (이미 있는 배치는 그대로). 사용 중인 교대·설비만</summary>
    public async Task<int> CopyAsync(AssignmentCopyRequest r, CancellationToken ct)
    {
        if (r.FromDate is null || r.ToDate is null) throw new RequestValidationException("fromDate", "복사할 날짜를 고르세요.");
        if (r.FromDate == r.ToDate) throw new RequestValidationException("toDate", "같은 날짜로는 복사할 수 없습니다.");
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var copied = await conn.ExecuteAsync(
            """
            INSERT INTO worker_assignment (work_date, work_shift_id, equipment_id, employee_id, assignment_type, remark, created_by, updated_by)
            SELECT @to, a.work_shift_id, a.equipment_id, a.employee_id, a.assignment_type, a.remark, @UserId, @UserId
              FROM worker_assignment a
              JOIN work_shift s ON s.work_shift_id = a.work_shift_id AND s.is_active = 1
              JOIN equipment e ON e.equipment_id = a.equipment_id AND e.is_active = 1
              JOIN employee m ON m.employee_id = a.employee_id AND m.is_active = 1
             WHERE a.work_date = @from
               AND NOT EXISTS (SELECT 1 FROM worker_assignment x WHERE x.work_date = @to AND x.work_shift_id = a.work_shift_id
                                  AND x.equipment_id = a.equipment_id AND x.employee_id = a.employee_id)
            """, new { from = r.FromDate.Value.ToDateTime(TimeOnly.MinValue), to = r.ToDate.Value.ToDateTime(TimeOnly.MinValue), currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, Table, 0, null, new { copyFrom = r.FromDate, copyTo = r.ToDate, rows = copied });
        await tx.CommitAsync(ct);
        if (copied > 0) await PublishAsync(r.ToDate.Value, ct);
        return copied;
    }

    private sealed class LockedRow
    {
        public long WorkerAssignmentId { get; init; }
        public DateTime WorkDate { get; init; }
        public long WorkShiftId { get; init; }
        public long EquipmentId { get; init; }
        public long EmployeeId { get; init; }
        public string AssignmentType { get; init; } = "";
    }

    private static async Task<LockedRow> LockAsync(MySqlConnection conn, MySqlTransaction tx, long id) =>
        await conn.QuerySingleOrDefaultAsync<LockedRow>(
            "SELECT worker_assignment_id, work_date, work_shift_id, equipment_id, employee_id, assignment_type FROM worker_assignment WHERE worker_assignment_id = @id FOR UPDATE",
            new { id }, tx) ?? throw new NotFoundException(Table, id);

    private static async Task EnsureAsync(MySqlConnection conn, MySqlTransaction tx, long shiftId, long equipmentId)
    {
        if (!await conn.ExecuteScalarAsync<bool>("SELECT COUNT(*) > 0 FROM work_shift WHERE work_shift_id = @shiftId AND is_active = 1", new { shiftId }, tx))
            throw new RequestValidationException("workShiftId", "사용 중인 교대가 아닙니다.");
        if (!await conn.ExecuteScalarAsync<bool>("SELECT COUNT(*) > 0 FROM equipment WHERE equipment_id = @equipmentId AND is_active = 1", new { equipmentId }, tx))
            throw new RequestValidationException("equipmentId", "사용 중인 설비가 아닙니다.");
    }

    private Task PublishAsync(DateOnly workDate, CancellationToken ct) =>
        events.PublishAsync(RealtimeEvents.WorkerAssignmentChanged, new { workDate = workDate.ToString("yyyy-MM-dd") }, ct);
}
