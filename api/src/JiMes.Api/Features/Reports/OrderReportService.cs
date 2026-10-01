using Dapper;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Settings;

namespace JiMes.Api.Features.Reports;

/// <summary>수주(입고) 행 진행 — 공정 경로 단위공정별 투입·양품 + 부적합·출하·재고</summary>
public sealed class OrderProgressDto
{
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public DateTime OrderDate { get; init; }
    public DateTime? DueDate { get; init; }
    public long CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
    public string? CustomerLot { get; init; }
    public string? HeatProcessName { get; init; }
    public long? HeatProcessVersionId { get; init; }
    public int Priority { get; init; }
    public string Status { get; init; } = "";
    public decimal OrderQty { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal OrderAmount { get; init; }
    public decimal MainInputQty { get; init; }
    public decimal DefectQty { get; init; }
    public decimal OpenDefectQty { get; init; }
    public decimal ShipmentQty { get; init; }
    public decimal TestSpecimenQty { get; init; }
    public decimal ShipmentAmount { get; init; }
    /// <summary>재고 = 주 LOT 양품(특채 포함) − 출하 − 시험편 (출하 가능 수량 합)</summary>
    public decimal StockQty { get; init; }
    /// <summary>미투입 = 수주 − 주공정 투입</summary>
    public decimal NotInputQty { get; init; }
    public decimal RemainingShipmentQty { get; init; }
    public List<ProcessProgressDto> Processes { get; set; } = [];
}

public sealed class ProcessProgressDto
{
    public long SalesOrderItemId { get; init; }
    public int SequenceNo { get; init; }
    public string UnitProcessName { get; init; } = "";
    public bool IsMainProcess { get; init; }
    public decimal InputQty { get; init; }
    public decimal GoodQty { get; init; }
    public long LotCount { get; init; }
}

/// <summary>
/// 수주 진행·재고 현황 (구 F_OrderStatus · F_InventoryForm · F_OutcomeStatus) — 저장형 잔량 대신 VIEW·실적으로 계산 (§1.2).
/// 단위공정별 투입·양품 = vw_sales_order_item_process_progress (재작업 제외), 재고 = 출하 화면 재고와 같은 기준.
/// </summary>
public sealed class OrderReportService(IDbConnectionFactory db, SettingsCache settings, TimeProvider time)
{
    public async Task<IEnumerable<OrderProgressDto>> OrdersAsync(DateOnly? from, DateOnly? to, long? customerId, string? search, string? view, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        await using var conn = await db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<OrderProgressDto>(
            """
            SELECT x.*,
                   GREATEST(x.order_qty - x.main_input_qty, 0) AS not_input_qty,
                   x.order_qty - x.shipment_qty - x.test_specimen_qty AS remaining_shipment_qty
              FROM (
                SELECT soi.sales_order_item_id, soi.order_item_no, so.order_date, so.due_date, so.customer_id, c.customer_name,
                       soi.part_name_snapshot AS part_name, soi.part_number_snapshot AS part_number, soi.customer_lot, soi.heat_process_name_snapshot AS heat_process_name,
                       soi.heat_process_version_id, soi.priority, soi.status, soi.order_qty, soi.unit_price,
                       COALESCE(soi.order_qty * soi.unit_price, 0) AS order_amount,
                       COALESCE(pr.main_input_qty, 0) AS main_input_qty, COALESCE(pr.defect_qty, 0) AS defect_qty,
                       COALESCE((SELECT SUM(d.defect_qty) FROM defect_occurrence d WHERE d.sales_order_item_id = soi.sales_order_item_id AND d.is_deleted = 0
                                   AND d.status IN ('OPEN','DECIDED','REWORKING')), 0) AS open_defect_qty,
                       COALESCE(pr.shipment_qty, 0) AS shipment_qty, COALESCE(pr.test_specimen_qty, 0) AS test_specimen_qty,
                       COALESCE((SELECT SUM(si.amount) FROM shipment_item si JOIN shipment s ON s.shipment_id = si.shipment_id
                                  WHERE si.sales_order_item_id = soi.sales_order_item_id AND s.status <> 'CANCELLED' AND s.is_deleted = 0), 0) AS shipment_amount,
                       GREATEST(COALESCE((SELECT SUM(m.input_qty
                                   - COALESCE((SELECT SUM(d.defect_qty) FROM defect_occurrence d WHERE d.production_work_input_id = m.production_work_input_id
                                                 AND d.is_deleted = 0 AND d.status <> 'CANCELLED' AND COALESCE(d.decision, '') <> 'SHIP'), 0)
                                   - COALESCE((SELECT SUM(d.defect_qty) FROM defect_occurrence d JOIN production_work_input pi ON pi.production_work_input_id = d.production_work_input_id
                                                WHERE pi.main_input_id = m.production_work_input_id AND d.is_deleted = 0 AND d.status <> 'CANCELLED'
                                                  AND COALESCE(d.decision, '') <> 'SHIP'), 0))
                                  FROM production_work_input m JOIN production_work mw ON mw.production_work_id = m.production_work_id
                                 WHERE m.sales_order_item_id = soi.sales_order_item_id AND m.main_work_id = m.production_work_id AND m.status <> 'CANCELLED'
                                   AND mw.is_deleted = 0 AND mw.status <> 'CANCELLED'), 0)
                                - COALESCE(pr.shipment_qty, 0) - COALESCE(pr.test_specimen_qty, 0), 0) AS stock_qty
                  FROM sales_order_item soi
                  JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
                  JOIN customer c ON c.customer_id = so.customer_id
                  LEFT JOIN vw_sales_order_item_progress pr ON pr.sales_order_item_id = soi.sales_order_item_id
                 WHERE so.is_deleted = 0 AND soi.status <> 'CANCELLED' AND so.order_date BETWEEN @from AND @to
                   AND (@customerId IS NULL OR so.customer_id = @customerId)
                   AND (@search IS NULL OR soi.order_item_no LIKE @like OR soi.part_name_snapshot LIKE @like OR soi.part_number_snapshot LIKE @like
                        OR soi.customer_lot LIKE @like OR soi.model_snapshot LIKE @like)
              ) x
             WHERE (@view IS NULL
                    OR (@view = 'STOCK' AND x.stock_qty > 0)
                    OR (@view = 'OPEN' AND x.order_qty - x.shipment_qty - x.test_specimen_qty > 0)
                    OR (@view = 'NOT_INPUT' AND x.order_qty - x.main_input_qty > 0))
             ORDER BY x.order_date DESC, x.order_item_no DESC
             LIMIT 2000
            """,
            new
            {
                from = (from ?? today.AddDays(-settings.GetInt(SettingKeys.SalesOrderListDefaultDays))).ToDateTime(TimeOnly.MinValue),
                to = (to ?? today).ToDateTime(TimeOnly.MinValue), customerId, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                like = $"%{search?.Trim()}%", view = string.IsNullOrEmpty(view) ? null : view,
            })).ToList();
        if (rows.Count == 0) return rows;

        // 공정 경로 단위공정 순서대로 투입·양품 (경로에 있는 단위공정만, 실적 없으면 0)
        var ids = rows.Select(r => r.SalesOrderItemId).ToArray();
        var processes = await conn.QueryAsync<ProcessProgressDto>(
            """
            SELECT soi.sales_order_item_id, op.sequence_no, up.unit_process_name, op.is_main_process,
                   COALESCE(p.input_qty, 0) AS input_qty, COALESCE(p.good_qty, 0) AS good_qty, COALESCE(p.lot_count, 0) AS lot_count
              FROM sales_order_item soi
              JOIN heat_process_operation op ON op.heat_process_version_id = soi.heat_process_version_id
              JOIN unit_process up ON up.unit_process_id = op.unit_process_id
              LEFT JOIN vw_sales_order_item_process_progress p ON p.sales_order_item_id = soi.sales_order_item_id AND p.unit_process_id = op.unit_process_id
             WHERE soi.sales_order_item_id IN @ids
             ORDER BY soi.sales_order_item_id, op.sequence_no
            """, new { ids });
        var byItem = processes.ToLookup(p => p.SalesOrderItemId);
        foreach (var r in rows) r.Processes = byItem[r.SalesOrderItemId].ToList();
        return rows;
    }
}
