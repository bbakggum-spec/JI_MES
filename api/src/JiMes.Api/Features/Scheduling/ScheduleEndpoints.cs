using Dapper;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;

namespace JiMes.Api.Features.Scheduling;

public sealed class BoardEquipmentDto
{
    public long EquipmentId { get; init; }
    public string EquipmentCode { get; init; } = "";
    public string EquipmentName { get; init; } = "";
    public long? EquipmentTypeId { get; init; }
    public string? EquipmentTypeName { get; init; }
}

public sealed class BoardBlockItemDto
{
    public long ProductionScheduleId { get; init; }
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public string? PartName { get; init; }
    public string? CustomerName { get; init; }
    public decimal PlannedQty { get; init; }
    public int Priority { get; init; }
}

public sealed class BoardBlockDto
{
    public long ProductionScheduleId { get; init; }
    public long EquipmentId { get; init; }
    public long UnitProcessId { get; init; }
    public string? UnitProcessName { get; init; }
    public DateTime WorkDate { get; init; }
    public int SequenceNo { get; init; }
    public string? PlannedLotNo { get; init; }
    public decimal PlannedQty { get; init; }
    public decimal PlannedDurationMin { get; init; }
    public string? DurationSource { get; init; }
    public DateTime PlannedStartAt { get; init; }
    public DateTime PlannedEndAt { get; init; }
    public string Status { get; init; } = "";
    public bool IsTimeLocked { get; init; }
    public bool IsRework { get; init; }
    public int RowVersion { get; init; }
    public List<BoardBlockItemDto> Items { get; } = [];
}

public sealed class BoardWorkDto
{
    public long ProductionWorkId { get; init; }
    public long EquipmentId { get; init; }
    public long? ProductionScheduleId { get; init; }
    public string LotNo { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime? ActualStartAt { get; init; }
    public DateTime? ActualEndAt { get; init; }
    public decimal? ExpectedDurationMin { get; init; }
}

public sealed class BoardDowntimeDto
{
    public long EquipmentId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime EndedAt { get; init; }
    public bool IsPlanned { get; init; }
}

public sealed class EquipmentTypeDto
{
    public long EquipmentTypeId { get; init; }
    public string EquipmentTypeName { get; init; } = "";
}

public sealed record BoardResponse(
    string DayStart, DateTime From, DateTime To,
    IReadOnlyList<EquipmentTypeDto> EquipmentTypes,
    IReadOnlyList<BoardEquipmentDto> Equipment, IReadOnlyList<BoardBlockDto> Blocks, IReadOnlyList<BoardWorkDto> Works,
    IReadOnlyList<DateTime> Holidays, IReadOnlyList<BoardDowntimeDto> Downtimes);

public sealed class BacklogRowDto
{
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public string? CustomerName { get; init; }
    public string? PartName { get; init; }
    public long UnitProcessId { get; init; }
    public string UnitProcessName { get; init; } = "";
    public int OperationSeq { get; init; }
    public decimal OrderQty { get; init; }
    public decimal RemainingQty { get; init; }
    public int Priority { get; init; }
    public DateTime? DueDate { get; init; }
}

/// <summary>생산계획 (설계 §4·§7·§11 Calendar/Gantt). 조회는 기간 + 설비(유형) 조건 필수 (§15.1 S9).</summary>
public static class ScheduleEndpoints
{
    // 서버 보호용 기술 상한 (업무 값 아님)
    private const int MaxBoardDays = 31;
    private const int MaxBacklogRows = 500;

    public static void MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/schedule").WithTags("Schedule");
        const string key = MenuKeys.ProductionSchedule;

        group.MapGet("/board", BoardAsync).RequirePermission(key, PermissionAction.Read);
        group.MapGet("/backlog", BacklogAsync).RequirePermission(key, PermissionAction.Read);

        group.MapPost("/blocks", async (CreateBlockRequest r, SchedulingService s, CancellationToken ct) =>
                Results.Ok(new { productionScheduleId = await s.CreateAsync(r, ct) }))
            .RequirePermission(key, PermissionAction.Create);
        group.MapPut("/blocks/{id:long}/move", async (long id, MoveBlockRequest r, SchedulingService s, CancellationToken ct) =>
            { await s.MoveAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        group.MapPost("/blocks/{id:long}/items", async (long id, MergeItemRequest r, SchedulingService s, CancellationToken ct) =>
            { await s.MergeAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        group.MapPut("/blocks/{id:long}/lock", async (long id, LockBlockRequest r, SchedulingService s, CancellationToken ct) =>
            { await s.SetLockAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        group.MapPost("/blocks/{id:long}/cancel", async (long id, VersionedRequest r, SchedulingService s, CancellationToken ct) =>
            { await s.CancelAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Delete);
        group.MapPost("/equipment/{id:long}/recalculate", async (long id, SchedulingService s, CancellationToken ct) =>
                Results.Ok(new { changed = await s.RecalculateAsync(id, ct) }))
            .RequirePermission(key, PermissionAction.Update);
    }

    /// <param name="from">시작 작업일 (yyyy-MM-dd). 없으면 오늘 작업일</param>
    /// <param name="days">표시 일수. 없으면 설정 schedule.board_days</param>
    private static async Task<IResult> BoardAsync(
        IDbConnectionFactory db, SettingsCache settings, SchedulingService scheduling, TimeProvider time, CancellationToken ct,
        DateOnly? from = null, int? days = null, long? equipmentTypeId = null)
    {
        var dayCount = days ?? settings.GetInt(SettingKeys.ScheduleBoardDays);
        if (dayCount < 1 || dayCount > MaxBoardDays)
            throw new RequestValidationException("days", $"표시 일수는 1~{MaxBoardDays}일입니다.");

        await using var conn = await db.OpenAsync(ct);
        var dayStart = await scheduling.DayStartAsync(conn, null);
        var fromDate = from ?? ScheduleCalculator.WorkDateOf(time.GetLocalNow().DateTime, dayStart);
        var rangeStart = ScheduleCalculator.WorkDayStart(fromDate, dayStart);
        var rangeEnd = rangeStart.AddDays(dayCount);
        var args = new { equipmentTypeId, rangeStart, rangeEnd };

        var types = (await conn.QueryAsync<EquipmentTypeDto>(
            "SELECT equipment_type_id, equipment_type_name FROM equipment_type WHERE is_active = 1 ORDER BY equipment_type_name")).ToList();
        var equipment = (await conn.QueryAsync<BoardEquipmentDto>(
            """
            SELECT e.equipment_id, e.equipment_code, e.equipment_name, e.equipment_type_id, t.equipment_type_name
              FROM equipment e LEFT JOIN equipment_type t ON t.equipment_type_id = e.equipment_type_id
             WHERE e.is_active = 1 AND (@equipmentTypeId IS NULL OR e.equipment_type_id = @equipmentTypeId)
             ORDER BY e.sort_order, e.equipment_code
            """, args)).ToList();
        var equipmentIds = equipment.Select(e => e.EquipmentId).ToList();
        if (equipmentIds.Count == 0)
            return Results.Ok(new BoardResponse(dayStart.ToString("HH:mm"), rangeStart, rangeEnd, types, [], [], [], [], []));

        var scoped = new { equipmentIds, rangeStart, rangeEnd };
        var blocks = (await conn.QueryAsync<BoardBlockDto>(
            """
            SELECT ps.production_schedule_id, ps.equipment_id, ps.unit_process_id, up.unit_process_name, ps.work_date, ps.sequence_no,
                   ps.planned_lot_no, ps.planned_qty, ps.planned_duration_min, ps.duration_source, ps.planned_start_at, ps.planned_end_at,
                   ps.status, ps.is_time_locked, ps.is_rework, ps.row_version
              FROM production_schedule ps JOIN unit_process up ON up.unit_process_id = ps.unit_process_id
             WHERE ps.equipment_id IN @equipmentIds AND ps.is_deleted = 0 AND ps.status <> 'CANCELLED'
               AND ps.planned_start_at < @rangeEnd AND ps.planned_end_at > @rangeStart
             ORDER BY ps.equipment_id, ps.planned_start_at
            """, scoped)).ToList();
        if (blocks.Count > 0)
        {
            var blockIds = blocks.Select(b => b.ProductionScheduleId).ToList();
            var items = await conn.QueryAsync<BoardBlockItemDto>(
                """
                SELECT psi.production_schedule_id, psi.sales_order_item_id, soi.order_item_no,
                       COALESCE(soi.part_name_snapshot, p.part_name) AS part_name, c.customer_name, psi.planned_qty, soi.priority
                  FROM production_schedule_item psi
                  JOIN sales_order_item soi ON soi.sales_order_item_id = psi.sales_order_item_id
                  JOIN sales_order so       ON so.sales_order_id = soi.sales_order_id
                  JOIN customer c           ON c.customer_id = so.customer_id
                  JOIN part p               ON p.part_id = soi.part_id
                 WHERE psi.production_schedule_id IN @blockIds
                 ORDER BY psi.production_schedule_item_id
                """, new { blockIds });
            var byBlock = blocks.ToDictionary(b => b.ProductionScheduleId);
            foreach (var item in items)
                byBlock[item.ProductionScheduleId].Items.Add(item);
        }

        var works = (await conn.QueryAsync<BoardWorkDto>(
            """
            SELECT production_work_id, equipment_id, production_schedule_id, lot_no, status, actual_start_at, actual_end_at, expected_duration_min
              FROM production_work
             WHERE equipment_id IN @equipmentIds AND is_deleted = 0 AND status <> 'CANCELLED'
               AND actual_start_at IS NOT NULL AND actual_start_at < @rangeEnd
               AND (actual_end_at IS NULL OR actual_end_at > @rangeStart)
            """, scoped)).ToList();
        var holidays = (await conn.QueryAsync<DateTime>(
            "SELECT calendar_date FROM work_calendar WHERE day_type = 'HOLIDAY' AND calendar_date BETWEEN @fromDate AND @toDate",
            new { fromDate = fromDate.ToDateTime(TimeOnly.MinValue), toDate = rangeEnd.Date })).ToList();
        var downtimes = (await conn.QueryAsync<BoardDowntimeDto>(
            """
            SELECT equipment_id, started_at, ended_at, is_planned FROM equipment_downtime
             WHERE equipment_id IN @equipmentIds AND started_at IS NOT NULL AND ended_at IS NOT NULL
               AND started_at < @rangeEnd AND ended_at > @rangeStart
            """, scoped)).ToList();

        return Results.Ok(new BoardResponse(dayStart.ToString("HH:mm"), rangeStart, rangeEnd, types, equipment, blocks, works, holidays, downtimes));
    }

    /// <summary>배정 대기 = 수주품목 × 공정 경로의 단위공정 중 계획 잔량이 남은 것. 설비유형이 오면 그 유형이 할 수 있는 단위공정만.</summary>
    private static async Task<IResult> BacklogAsync(
        IDbConnectionFactory db, CancellationToken ct, long? equipmentTypeId = null, string? search = null)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<BacklogRowDto>(
            $"""
            SELECT * FROM (
                SELECT soi.sales_order_item_id, soi.order_item_no, c.customer_name,
                       COALESCE(soi.part_name_snapshot, p.part_name) AS part_name,
                       op.unit_process_id, up.unit_process_name, op.sequence_no AS operation_seq,
                       soi.order_qty,
                       soi.order_qty - COALESCE((
                           SELECT SUM(psi.planned_qty)
                             FROM production_schedule_item psi
                             JOIN production_schedule ps ON ps.production_schedule_id = psi.production_schedule_id
                            WHERE psi.sales_order_item_id = soi.sales_order_item_id AND ps.unit_process_id = op.unit_process_id
                              AND ps.is_deleted = 0 AND ps.status <> 'CANCELLED'), 0) AS remaining_qty,
                       soi.priority, so.due_date
                  FROM sales_order_item soi
                  JOIN sales_order so            ON so.sales_order_id = soi.sales_order_id AND so.is_deleted = 0
                  JOIN customer c                ON c.customer_id = so.customer_id
                  JOIN part p                    ON p.part_id = soi.part_id
                  JOIN heat_process_operation op ON op.heat_process_version_id = soi.heat_process_version_id
                  JOIN unit_process up           ON up.unit_process_id = op.unit_process_id
                 WHERE soi.status IN ('OPEN','IN_PROGRESS')
                   AND (@equipmentTypeId IS NULL OR op.unit_process_id IN
                        (SELECT unit_process_id FROM step_template WHERE equipment_type_id = @equipmentTypeId))
                   AND (@search IS NULL OR soi.order_item_no LIKE @like OR c.customer_name LIKE @like
                        OR COALESCE(soi.part_name_snapshot, p.part_name) LIKE @like)
            ) b
             WHERE b.remaining_qty > 0
             ORDER BY b.priority DESC, b.due_date IS NULL, b.due_date, b.order_item_no, b.operation_seq
             LIMIT {MaxBacklogRows}
            """,
            new { equipmentTypeId, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), like = $"%{search?.Trim()}%" });
        return Results.Ok(rows);
    }
}
