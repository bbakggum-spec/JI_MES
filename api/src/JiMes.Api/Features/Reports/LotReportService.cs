using Dapper;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Reports;

/// <summary>LOT 현황 1행 — vw_work_lot_status (배정·투입·완료 + 참고 정보) + 표시용 요약</summary>
public sealed class LotStatusDto
{
    public long ProductionWorkId { get; init; }
    public string LotNo { get; init; } = "";
    public DateTime WorkDate { get; init; }
    public long? EquipmentId { get; init; }
    public string? EquipmentName { get; init; }
    public string? UnitProcessName { get; init; }
    public string? HeatProcessName { get; init; }
    public bool IsMainProcess { get; init; }
    public bool IsRework { get; init; }
    public string Status { get; init; } = "";
    public DateTime? ActualStartAt { get; init; }
    public DateTime? ActualEndAt { get; init; }
    public string? SubmitLotNo { get; init; }
    public decimal InputQty { get; init; }
    public decimal GoodQty { get; init; }
    public long PostLotCount { get; init; }
    public long PostDoneCount { get; init; }
    public long InspectionCount { get; init; }
    public long InspectionDoneCount { get; init; }
    public long OpenDefectCount { get; init; }
    public decimal ShipmentQty { get; init; }
    public string? OrderSummary { get; init; }
    public string? PartSummary { get; init; }
    public string? CustomerSummary { get; init; }
}

/// <summary>추적 화면의 LOT 1행 (전공정·후공정·재작업)</summary>
public sealed class TraceLotDto
{
    public long ProductionWorkId { get; init; }
    public string LotNo { get; init; } = "";
    public string? UnitProcessName { get; init; }
    public string? EquipmentName { get; init; }
    public string Status { get; init; } = "";
    public bool IsRework { get; init; }
    public DateTime WorkDate { get; init; }
    public DateTime? ActualStartAt { get; init; }
    public DateTime? ActualEndAt { get; init; }
    public decimal InputQty { get; init; }
    public decimal GoodQty { get; init; }
    public string? OrderSummary { get; init; }
}

public sealed class TraceInputDto
{
    public long ProductionWorkInputId { get; init; }
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public string? CustomerName { get; init; }
    public string? CustomerLot { get; init; }
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
    public decimal InputQty { get; init; }
    public decimal DefectQty { get; init; }
    public decimal GoodQty { get; init; }
}

public sealed class TraceInspectionDto
{
    public long InspectionId { get; init; }
    public string InspectionNo { get; init; } = "";
    public string InspectionType { get; init; } = "";
    public DateTime InspectionDate { get; init; }
    public string LotNo { get; init; } = "";
    public string Status { get; init; } = "";
    public string? Decision { get; init; }
    public string? Targets { get; init; }
}

public sealed class TraceDefectDto
{
    public long DefectOccurrenceId { get; init; }
    public DateTime DefectDate { get; init; }
    public string? LotNo { get; init; }
    public string? UnitProcessName { get; init; }
    public string OrderItemNo { get; init; } = "";
    public decimal DefectQty { get; init; }
    public string Status { get; init; } = "";
    public string? Decision { get; init; }
    public string? ReworkLotNo { get; init; }
}

public sealed class TraceShipmentDto
{
    public long ShipmentId { get; init; }
    public string ShipmentNo { get; init; } = "";
    public DateTime ShipmentDate { get; init; }
    public string? CustomerName { get; init; }
    public string OrderItemNo { get; init; } = "";
    public decimal ShipmentQty { get; init; }
    public decimal TestSpecimenQty { get; init; }
    public string Status { get; init; } = "";
    public string ClosingStatus { get; init; } = "";
}

/// <summary>추적 시작 후보 (입력한 번호가 가리키는 주 LOT)</summary>
public sealed class TraceMainDto
{
    public long ProductionWorkId { get; init; }
    public string LotNo { get; init; } = "";
    public string? UnitProcessName { get; init; }
    public string? EquipmentName { get; init; }
    public DateTime WorkDate { get; init; }
    public string Status { get; init; } = "";
    public string? SubmitLotNo { get; init; }
}

/// <summary>
/// LOT 현황·추적 (설계 §3.4·§3.5, 구 F_WorkHistoryForm·F_ProductionStatus).
/// 추적은 주 LOT 기준: 후공정(main_work_id)·재작업(origin_work_id)·검사·부적합·출하는 정확, 전공정은 주 LOT 의 수주로 연결(수주 단위 정밀도 — 업무 확인 결과 수용).
/// </summary>
public sealed class LotReportService(IDbConnectionFactory db, SettingsCache settings, TimeProvider time)
{
    private const string LotSql =
        """
        SELECT v.production_work_id, v.lot_no, v.work_date, v.equipment_id, COALESCE(e.equipment_name, w.equipment_name_snapshot) AS equipment_name,
               v.unit_process_name, w.heat_process_name_snapshot AS heat_process_name, v.is_main_process, v.is_rework, v.status,
               w.actual_start_at, w.actual_end_at, w.submit_lot_no,
               (SELECT COALESCE(SUM(i.input_qty), 0) FROM production_work_input i WHERE i.production_work_id = v.production_work_id AND i.status <> 'CANCELLED') AS input_qty,
               v.good_qty, v.post_lot_count, v.post_done_count, v.inspection_count, v.inspection_done_count, v.open_defect_count, v.shipment_qty,
               (SELECT GROUP_CONCAT(DISTINCT soi.order_item_no ORDER BY soi.order_item_no SEPARATOR ', ') FROM production_work_input i
                  JOIN sales_order_item soi ON soi.sales_order_item_id = i.sales_order_item_id
                 WHERE i.production_work_id = v.production_work_id AND i.status <> 'CANCELLED') AS order_summary,
               (SELECT GROUP_CONCAT(DISTINCT i.part_name_snapshot SEPARATOR ', ') FROM production_work_input i
                 WHERE i.production_work_id = v.production_work_id AND i.status <> 'CANCELLED') AS part_summary,
               (SELECT GROUP_CONCAT(DISTINCT i.customer_name_snapshot SEPARATOR ', ') FROM production_work_input i
                 WHERE i.production_work_id = v.production_work_id AND i.status <> 'CANCELLED') AS customer_summary
          FROM vw_work_lot_status v
          JOIN production_work w ON w.production_work_id = v.production_work_id
          LEFT JOIN equipment e ON e.equipment_id = v.equipment_id
        """;

    /// <summary>LOT 현황 — 작업일·설비·단위공정·상태·주 LOT 만·검색 (기본 기간 = 오늘 − 설정 sales_order.list_default_days)</summary>
    public async Task<IEnumerable<LotStatusDto>> LotsAsync(DateOnly? from, DateOnly? to, long? equipmentId, long? unitProcessId, string? status, bool mainOnly,
        string? search, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        await using var conn = await db.OpenAsync(ct);
        return await conn.QueryAsync<LotStatusDto>(
            LotSql + """
             WHERE v.work_date BETWEEN @from AND @to AND w.status <> 'CANCELLED'
               AND (@equipmentId IS NULL OR v.equipment_id = @equipmentId) AND (@unitProcessId IS NULL OR w.unit_process_id = @unitProcessId)
               AND (@status IS NULL OR v.status = @status) AND (NOT @mainOnly OR v.is_main_process = 1)
               AND (@search IS NULL OR v.lot_no LIKE @like OR w.submit_lot_no LIKE @like OR EXISTS (
                    SELECT 1 FROM production_work_input i JOIN sales_order_item soi ON soi.sales_order_item_id = i.sales_order_item_id
                     WHERE i.production_work_id = v.production_work_id AND (soi.order_item_no LIKE @like OR i.part_name_snapshot LIKE @like
                           OR i.customer_name_snapshot LIKE @like OR i.customer_lot_snapshot LIKE @like)))
             ORDER BY v.work_date DESC, v.lot_no DESC
             LIMIT 2000
            """,
            new
            {
                from = (from ?? today.AddDays(-settings.GetInt(SettingKeys.SalesOrderListDefaultDays))).ToDateTime(TimeOnly.MinValue),
                to = (to ?? today).ToDateTime(TimeOnly.MinValue), equipmentId, unitProcessId, status = string.IsNullOrEmpty(status) ? null : status, mainOnly,
                search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), like = $"%{search?.Trim()}%",
            });
    }

    /// <summary>
    /// 추적 시작 — 입력한 번호를 주 LOT 로 바꾼다.
    /// 작업 LOT번호: 주공정이면 그 LOT / 후공정이면 투입 행의 주 LOT / 재작업이면 원 LOT 의 주 LOT / 전공정이면 그 수주들의 주 LOT.
    /// 제출 LOT: 그 제출 LOT 을 가진 LOT. 입고번호(수주번호): 그 수주가 들어간 주 LOT.
    /// </summary>
    public async Task<object> ResolveAsync(string code, CancellationToken ct)
    {
        code = code.Trim();
        if (code.Length == 0) throw new RequestValidationException("code", "LOT번호·제출 LOT·입고번호를 입력하세요.");
        await using var conn = await db.OpenAsync(ct);
        string kind;
        var workIds = (await conn.QueryAsync<long>(
            "SELECT CAST(production_work_id AS SIGNED) FROM production_work WHERE lot_no = @code AND is_deleted = 0 AND status <> 'CANCELLED'", new { code })).ToList();
        if (workIds.Count > 0) kind = "LOT";
        else
        {
            workIds = (await conn.QueryAsync<long>(
                "SELECT CAST(production_work_id AS SIGNED) FROM production_work WHERE submit_lot_no = @code AND is_deleted = 0 AND status <> 'CANCELLED'", new { code })).ToList();
            kind = workIds.Count > 0 ? "SUBMIT_LOT" : "ORDER_ITEM";
        }

        List<long> mains;
        long[] itemIds;
        if (kind == "ORDER_ITEM")
        {
            itemIds = (await conn.QueryAsync<long>("SELECT CAST(sales_order_item_id AS SIGNED) FROM sales_order_item WHERE order_item_no = @code", new { code })).ToArray();
            if (itemIds.Length == 0) throw new BusinessRuleException("TRACE_NOT_FOUND", $"'{code}' 에 해당하는 LOT·제출 LOT·입고번호가 없습니다.");
            mains = await MainsOfItemsAsync(conn, itemIds);
        }
        else
        {
            mains = (await conn.QueryAsync<long?>(
                """
                SELECT DISTINCT CAST(COALESCE(
                         CASE WHEN w.is_main_process = 1 AND w.is_rework = 0 THEN w.production_work_id END,
                         CASE WHEN w.is_rework = 1 THEN (SELECT o.main_work_id FROM production_work_input o
                                                          WHERE o.production_work_id = i.origin_work_id AND o.main_work_id IS NOT NULL LIMIT 1) END,
                         i.main_work_id) AS SIGNED)
                  FROM production_work w
                  LEFT JOIN production_work_input i ON i.production_work_id = w.production_work_id AND i.status <> 'CANCELLED'
                 WHERE w.production_work_id IN @workIds
                """, new { workIds })).Where(x => x is > 0).Select(x => x!.Value).ToList();
            itemIds = (await conn.QueryAsync<long>(
                "SELECT DISTINCT CAST(sales_order_item_id AS SIGNED) FROM production_work_input WHERE production_work_id IN @workIds AND status <> 'CANCELLED'", new { workIds })).ToArray();
            if (mains.Count == 0 && itemIds.Length > 0) mains = await MainsOfItemsAsync(conn, itemIds);   // 전공정 LOT → 그 수주의 주 LOT
        }
        var candidates = mains.Count == 0 ? [] : await conn.QueryAsync<TraceMainDto>(
            """
            SELECT w.production_work_id, w.lot_no, w.unit_process_name_snapshot AS unit_process_name, w.equipment_name_snapshot AS equipment_name,
                   w.work_date, w.status, w.submit_lot_no
              FROM production_work w WHERE w.production_work_id IN @mains ORDER BY w.work_date, w.lot_no
            """, new { mains });
        return new { kind, code, mains = candidates, itemIds };
    }

    private static async Task<List<long>> MainsOfItemsAsync(MySqlConnection conn, long[] itemIds) =>
        (await conn.QueryAsync<long>(
            """
            SELECT DISTINCT CAST(i.production_work_id AS SIGNED)
              FROM production_work_input i JOIN production_work w ON w.production_work_id = i.production_work_id
             WHERE i.sales_order_item_id IN @itemIds AND i.main_work_id = i.production_work_id AND i.status <> 'CANCELLED'
               AND w.is_deleted = 0 AND w.status <> 'CANCELLED'
            """, new { itemIds })).ToList();

    /// <summary>주 LOT 추적 — 투입 수주·전공정·후공정·재작업·검사·부적합·출하</summary>
    public async Task<object> TraceAsync(long mainWorkId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var main = await conn.QuerySingleOrDefaultAsync<LotStatusDto>(LotSql + " WHERE v.production_work_id = @mainWorkId", new { mainWorkId })
            ?? throw new NotFoundException("production_work", mainWorkId);
        var args = new { mainWorkId };
        var inputs = (await conn.QueryAsync<TraceInputDto>(
            """
            SELECT i.production_work_input_id, i.sales_order_item_id, soi.order_item_no, i.customer_name_snapshot AS customer_name, i.customer_lot_snapshot AS customer_lot,
                   i.part_name_snapshot AS part_name, i.part_number_snapshot AS part_number, i.input_qty, COALESCE(q.defect_qty, 0) AS defect_qty,
                   COALESCE(q.good_qty, i.input_qty) AS good_qty
              FROM production_work_input i
              JOIN sales_order_item soi ON soi.sales_order_item_id = i.sales_order_item_id
              LEFT JOIN vw_production_work_input_qty q ON q.production_work_input_id = i.production_work_input_id
             WHERE i.production_work_id = @mainWorkId AND i.status <> 'CANCELLED'
             ORDER BY i.production_work_input_id
            """, args)).ToList();
        var itemIds = inputs.Select(i => i.SalesOrderItemId).Distinct().ToArray();

        const string traceLot =
            """
            SELECT DISTINCT w.production_work_id, w.lot_no, w.unit_process_name_snapshot AS unit_process_name, w.equipment_name_snapshot AS equipment_name,
                   w.status, w.is_rework, w.work_date, w.actual_start_at, w.actual_end_at,
                   (SELECT COALESCE(SUM(x.input_qty), 0) FROM production_work_input x WHERE x.production_work_id = w.production_work_id AND x.status <> 'CANCELLED' {0}) AS input_qty,
                   (SELECT COALESCE(SUM(q.good_qty), 0) FROM vw_production_work_input_qty q JOIN production_work_input x ON x.production_work_input_id = q.production_work_input_id
                     WHERE x.production_work_id = w.production_work_id {0}) AS good_qty,
                   (SELECT GROUP_CONCAT(DISTINCT soi.order_item_no SEPARATOR ', ') FROM production_work_input x JOIN sales_order_item soi ON soi.sales_order_item_id = x.sales_order_item_id
                     WHERE x.production_work_id = w.production_work_id AND x.status <> 'CANCELLED' {0}) AS order_summary
              FROM production_work w
              JOIN production_work_input i ON i.production_work_id = w.production_work_id AND i.status <> 'CANCELLED'
             WHERE w.is_deleted = 0 AND w.status <> 'CANCELLED' AND {1}
             ORDER BY w.work_date, w.lot_no
            """;
        // 전공정: 이 주 LOT 수주들이 주 LOT 없이 들어간 LOT (수주 단위 연결)
        var pre = itemIds.Length == 0 ? [] : await conn.QueryAsync<TraceLotDto>(
            string.Format(traceLot, "AND x.sales_order_item_id IN @itemIds AND x.main_work_id IS NULL", "i.main_work_id IS NULL AND w.is_rework = 0 AND i.sales_order_item_id IN @itemIds"),
            new { itemIds });
        var post = await conn.QueryAsync<TraceLotDto>(
            string.Format(traceLot, "AND x.main_work_id = @mainWorkId", "i.main_work_id = @mainWorkId AND w.production_work_id <> @mainWorkId AND w.is_rework = 0"), args);
        var rework = await conn.QueryAsync<TraceLotDto>(
            string.Format(traceLot, "AND x.origin_work_id = @mainWorkId", "i.origin_work_id = @mainWorkId"), args);

        var inspections = await conn.QueryAsync<TraceInspectionDto>(
            """
            SELECT DISTINCT ins.inspection_id, ins.inspection_no, ins.inspection_type, ins.inspection_date, w.lot_no, ins.status, ins.decision,
                   (SELECT GROUP_CONCAT(t2.part_name_snapshot ORDER BY t2.sub_no SEPARATOR ', ') FROM inspection_target t2 WHERE t2.inspection_id = ins.inspection_id) AS targets
              FROM inspection ins
              JOIN production_work w ON w.production_work_id = ins.production_work_id
              JOIN inspection_target t ON t.inspection_id = ins.inspection_id
              JOIN production_work_input i ON i.production_work_input_id = t.production_work_input_id
             WHERE ins.is_deleted = 0 AND ins.status <> 'CANCELLED' AND (i.main_work_id = @mainWorkId OR i.origin_work_id = @mainWorkId OR i.production_work_id = @mainWorkId)
             ORDER BY ins.inspection_date, ins.inspection_no
            """, args);
        var defects = await conn.QueryAsync<TraceDefectDto>(
            """
            SELECT d.defect_occurrence_id, d.defect_date, w.lot_no, w.unit_process_name_snapshot AS unit_process_name, soi.order_item_no, d.defect_qty, d.status, d.decision,
                   rw.lot_no AS rework_lot_no
              FROM defect_occurrence d
              JOIN sales_order_item soi ON soi.sales_order_item_id = d.sales_order_item_id
              LEFT JOIN production_work w ON w.production_work_id = d.production_work_id
              LEFT JOIN production_work_input ri ON ri.production_work_input_id = d.rework_input_id
              LEFT JOIN production_work rw ON rw.production_work_id = ri.production_work_id
             WHERE d.is_deleted = 0 AND d.status <> 'CANCELLED' AND (d.main_work_id = @mainWorkId OR d.production_work_id = @mainWorkId)
             ORDER BY d.defect_date, d.defect_occurrence_id
            """, args);
        var shipments = await conn.QueryAsync<TraceShipmentDto>(
            """
            SELECT s.shipment_id, s.shipment_no, s.shipment_date, s.customer_name_snapshot AS customer_name, soi.order_item_no, si.shipment_qty, si.test_specimen_qty,
                   s.status, s.closing_status
              FROM shipment_item si
              JOIN shipment s ON s.shipment_id = si.shipment_id
              JOIN sales_order_item soi ON soi.sales_order_item_id = si.sales_order_item_id
             WHERE si.main_work_id = @mainWorkId AND s.is_deleted = 0 AND s.status <> 'CANCELLED'
             ORDER BY s.shipment_date, s.shipment_no
            """, args);
        return new { main, inputs, pre, post, rework, inspections, defects, shipments };
    }
}
