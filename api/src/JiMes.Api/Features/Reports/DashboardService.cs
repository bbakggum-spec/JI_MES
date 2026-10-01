using Dapper;
using JiMes.Api.Features.Scheduling;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;

namespace JiMes.Api.Features.Reports;

public sealed class EquipmentPanelDto
{
    public long EquipmentId { get; init; }
    public string EquipmentName { get; init; } = "";
    public string? EquipmentTypeName { get; init; }
    public string? RunningLotNo { get; init; }
    public long? RunningWorkId { get; init; }
    public string? RunningUnitProcessName { get; init; }
    public DateTime? RunningStartAt { get; init; }
    public decimal? RunningExpectedMin { get; init; }
    public string? NextLotNo { get; init; }
    public DateTime? NextPlannedStartAt { get; init; }
    public long AllocatedCount { get; init; }
    public long CompletedCount { get; init; }
    public bool IsDown { get; init; }
}

public sealed class TrendPointDto
{
    public DateTime Day { get; init; }
    public decimal? IntakeAmount { get; set; }
    public long? IntakeCount { get; set; }
    public decimal? ShipmentAmount { get; set; }
    public long? ShipmentCount { get; set; }
    public long? DefectCount { get; set; }
    public decimal? DefectQty { get; set; }
}

/// <summary>
/// 대시보드 KPI (구 F_DashForm 일별·월별 입고금액·출고금액·불량건수 + 설비 가동·품질·마감 패널, 설계 §19.4·§24.3).
/// 패널마다 해당 업무 메뉴 읽기 권한이 있어야 보인다 (금액 등 노출 범위). 추이는 권한 있는 계열만 채운다.
/// </summary>
public sealed class DashboardService(IDbConnectionFactory db, SettingsCache settings, TimeProvider time, SchedulingService scheduling,
    PermissionService permissions, ICurrentUser currentUser)
{
    private DateTime Now() => time.GetLocalNow().DateTime;

    /// <summary>설비 가동 — 설비마다 진행 중 LOT(시작·예상 작업시간)·다음 배정·이 작업일 완료 수·계획 비가동 중</summary>
    public async Task<object> EquipmentAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var now = Now();
        var workDate = ScheduleCalculator.WorkDateOf(now, await scheduling.DayStartAsync(conn, null)).ToDateTime(TimeOnly.MinValue);
        var rows = await conn.QueryAsync<EquipmentPanelDto>(
            """
            SELECT e.equipment_id, e.equipment_name, t.equipment_type_name,
                   r.lot_no AS running_lot_no, r.production_work_id AS running_work_id, r.unit_process_name_snapshot AS running_unit_process_name,
                   r.actual_start_at AS running_start_at, r.expected_duration_min AS running_expected_min,
                   (SELECT w.lot_no FROM production_work w LEFT JOIN production_schedule ps ON ps.production_schedule_id = w.production_schedule_id
                     WHERE w.equipment_id = e.equipment_id AND w.status = 'ALLOCATED' AND w.is_deleted = 0
                     ORDER BY COALESCE(ps.planned_start_at, w.created_at) LIMIT 1) AS next_lot_no,
                   (SELECT ps.planned_start_at FROM production_work w JOIN production_schedule ps ON ps.production_schedule_id = w.production_schedule_id
                     WHERE w.equipment_id = e.equipment_id AND w.status = 'ALLOCATED' AND w.is_deleted = 0
                     ORDER BY ps.planned_start_at LIMIT 1) AS next_planned_start_at,
                   (SELECT COUNT(*) FROM production_work w WHERE w.equipment_id = e.equipment_id AND w.status = 'ALLOCATED' AND w.is_deleted = 0) AS allocated_count,
                   (SELECT COUNT(*) FROM production_work w WHERE w.equipment_id = e.equipment_id AND w.status = 'COMPLETED' AND w.is_deleted = 0
                       AND w.work_date = @workDate) AS completed_count,
                   EXISTS (SELECT 1 FROM equipment_downtime d WHERE d.equipment_id = e.equipment_id AND d.started_at <= @now
                             AND (d.ended_at IS NULL OR d.ended_at > @now)) AS is_down
              FROM equipment e
              LEFT JOIN equipment_type t ON t.equipment_type_id = e.equipment_type_id
              LEFT JOIN production_work r ON r.equipment_id = e.equipment_id AND r.status = 'INPUT' AND r.is_deleted = 0
             WHERE e.is_active = 1
             ORDER BY e.sort_order, e.equipment_code
            """, new { workDate, now });
        return new { now, workDate, equipment = rows };
    }

    /// <summary>품질 — 미처리 부적합(상태별 건수·수량), 이 작업일 검사(확정·불합격), 최근 미처리 부적합</summary>
    public async Task<object> QualityAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var today = Now().Date;
        var byStatus = await conn.QueryAsync<(string Status, long Count, decimal Qty)>(
            """
            SELECT status, COUNT(*), COALESCE(SUM(defect_qty), 0) FROM defect_occurrence
             WHERE is_deleted = 0 AND status IN ('OPEN','DECIDED','REWORKING') GROUP BY status
            """);
        var inspections = await conn.QuerySingleAsync<(long Total, long Completed, long Failed, long Pending)>(
            """
            SELECT COUNT(*), CAST(COALESCE(SUM(status = 'COMPLETED'), 0) AS SIGNED), CAST(COALESCE(SUM(status = 'COMPLETED' AND decision = 'FAIL'), 0) AS SIGNED),
                   (SELECT COUNT(*) FROM inspection WHERE is_deleted = 0 AND status IN ('WAITING','IN_PROGRESS'))
              FROM inspection WHERE is_deleted = 0 AND status <> 'CANCELLED' AND inspection_date = @today
            """, new { today });
        var recent = await conn.QueryAsync(
            """
            SELECT d.defect_occurrence_id AS id, d.defect_date AS defectDate, soi.order_item_no AS orderItemNo, soi.part_name_snapshot AS partName,
                   w.lot_no AS lotNo, d.defect_qty AS defectQty, d.status
              FROM defect_occurrence d JOIN sales_order_item soi ON soi.sales_order_item_id = d.sales_order_item_id
              LEFT JOIN production_work w ON w.production_work_id = d.production_work_id
             WHERE d.is_deleted = 0 AND d.status IN ('OPEN','DECIDED','REWORKING')
             ORDER BY d.defect_date DESC, d.defect_occurrence_id DESC LIMIT 5
            """);
        return new
        {
            openDefects = byStatus.Select(x => new { x.Status, x.Count, x.Qty }),
            inspections = new { inspections.Total, inspections.Completed, inspections.Failed, inspections.Pending }, recent,
        };
    }

    /// <summary>영업 — 오늘·이번 달 입고(건수·금액), 미투입 입고 행, 오늘·이번 달 출하, 미마감 금액</summary>
    public async Task<object> SalesAsync(CancellationToken ct)
    {
        var access = await permissions.GetAsync(currentUser.UserId ?? 0, ct);
        await using var conn = await db.OpenAsync(ct);
        var today = Now().Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        object? intake = null, shipment = null;
        if (access.Has(MenuKeys.SalesOrder, PermissionAction.Read))
        {
            var i = await conn.QuerySingleAsync<(long TodayCount, decimal TodayAmount, long MonthCount, decimal MonthAmount, long NotInput)>(
                """
                SELECT CAST(COALESCE(SUM(so.order_date = @today), 0) AS SIGNED), COALESCE(SUM(IF(so.order_date = @today, soi.order_qty * COALESCE(soi.unit_price, 0), 0)), 0),
                       COUNT(*), COALESCE(SUM(soi.order_qty * COALESCE(soi.unit_price, 0)), 0),
                       (SELECT COUNT(*) FROM vw_sales_order_item_progress p JOIN sales_order_item x ON x.sales_order_item_id = p.sales_order_item_id
                         WHERE x.status <> 'CANCELLED' AND p.remaining_input_qty > 0)
                  FROM sales_order_item soi JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
                 WHERE so.is_deleted = 0 AND soi.status <> 'CANCELLED' AND so.order_date BETWEEN @monthStart AND @today
                """, new { today, monthStart });
            intake = new { i.TodayCount, i.TodayAmount, i.MonthCount, i.MonthAmount, i.NotInput };
        }
        if (access.Has(MenuKeys.SalesShipment, PermissionAction.Read))
        {
            var s = await conn.QuerySingleAsync<(long TodayCount, decimal TodayAmount, long MonthCount, decimal MonthAmount)>(
                """
                SELECT CAST(COALESCE(SUM(shipment_date = @today), 0) AS SIGNED), COALESCE(SUM(IF(shipment_date = @today, total_amount, 0)), 0), COUNT(*), COALESCE(SUM(total_amount), 0)
                  FROM shipment WHERE is_deleted = 0 AND status <> 'CANCELLED' AND shipment_date BETWEEN @monthStart AND @today
                """, new { today, monthStart });
            var unclosed = await conn.QuerySingleAsync<(long Count, decimal Amount)>(
                "SELECT COUNT(*), COALESCE(SUM(total_amount), 0) FROM shipment WHERE is_deleted = 0 AND status <> 'CANCELLED' AND closing_status <> 'CLOSED'");
            shipment = new { s.TodayCount, s.TodayAmount, s.MonthCount, s.MonthAmount, UnclosedCount = unclosed.Count, UnclosedAmount = unclosed.Amount };
        }
        if (intake is null && shipment is null) throw new ForbiddenAreaException();
        return new { intake, shipment };
    }

    /// <summary>일별 추이 (구 F_DashForm) — 입고 금액·출하 금액·부적합 건수. 기간 = 설정 dashboard.trend_days, 권한 있는 계열만</summary>
    public async Task<IEnumerable<TrendPointDto>> TrendAsync(int? days, CancellationToken ct)
    {
        var access = await permissions.GetAsync(currentUser.UserId ?? 0, ct);
        var count = Math.Clamp(days ?? settings.GetInt(SettingKeys.DashboardTrendDays), 1, 92);
        var to = Now().Date;
        var from = to.AddDays(-(count - 1));
        var points = Enumerable.Range(0, count).Select(i => new TrendPointDto { Day = from.AddDays(i) }).ToDictionary(p => p.Day);
        await using var conn = await db.OpenAsync(ct);
        if (access.Has(MenuKeys.SalesOrder, PermissionAction.Read))
            foreach (var r in await conn.QueryAsync<(DateTime Day, long Count, decimal Amount)>(
                         """
                         SELECT so.order_date, COUNT(*), COALESCE(SUM(soi.order_qty * COALESCE(soi.unit_price, 0)), 0)
                           FROM sales_order_item soi JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
                          WHERE so.is_deleted = 0 AND soi.status <> 'CANCELLED' AND so.order_date BETWEEN @from AND @to GROUP BY so.order_date
                         """, new { from, to }))
            {
                points[r.Day].IntakeCount = r.Count;
                points[r.Day].IntakeAmount = r.Amount;
            }
        if (access.Has(MenuKeys.SalesShipment, PermissionAction.Read))
            foreach (var r in await conn.QueryAsync<(DateTime Day, long Count, decimal Amount)>(
                         """
                         SELECT shipment_date, COUNT(*), COALESCE(SUM(total_amount), 0) FROM shipment
                          WHERE is_deleted = 0 AND status <> 'CANCELLED' AND shipment_date BETWEEN @from AND @to GROUP BY shipment_date
                         """, new { from, to }))
            {
                points[r.Day].ShipmentCount = r.Count;
                points[r.Day].ShipmentAmount = r.Amount;
            }
        if (access.Has(MenuKeys.QualityDefect, PermissionAction.Read))
            foreach (var r in await conn.QueryAsync<(DateTime Day, long Count, decimal Qty)>(
                         """
                         SELECT defect_date, COUNT(*), COALESCE(SUM(defect_qty), 0) FROM defect_occurrence
                          WHERE is_deleted = 0 AND status <> 'CANCELLED' AND defect_date BETWEEN @from AND @to GROUP BY defect_date
                         """, new { from, to }))
            {
                points[r.Day].DefectCount = r.Count;
                points[r.Day].DefectQty = r.Qty;
            }
        // 권한 있는 계열은 값이 없는 날도 0 (그래프 연속), 없는 계열은 null
        foreach (var p in points.Values)
        {
            if (access.Has(MenuKeys.SalesOrder, PermissionAction.Read)) { p.IntakeAmount ??= 0; p.IntakeCount ??= 0; }
            if (access.Has(MenuKeys.SalesShipment, PermissionAction.Read)) { p.ShipmentAmount ??= 0; p.ShipmentCount ??= 0; }
            if (access.Has(MenuKeys.QualityDefect, PermissionAction.Read)) { p.DefectCount ??= 0; p.DefectQty ??= 0; }
        }
        return points.Values.OrderBy(p => p.Day);
    }
}

public sealed class ForbiddenAreaException()
    : AppException(StatusCodes.Status403Forbidden, "FORBIDDEN", "이 영역을 볼 권한이 없습니다.");
