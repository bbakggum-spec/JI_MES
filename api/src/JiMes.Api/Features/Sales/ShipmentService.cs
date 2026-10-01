using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Numbering;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Sales;

public sealed class ShipmentDto
{
    public long ShipmentId { get; init; }
    public string ShipmentNo { get; init; } = "";
    public DateTime ShipmentDate { get; init; }
    public long CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string Status { get; init; } = "";
    public string ClosingStatus { get; init; } = "";
    public int? ClosingYear { get; init; }
    public int? ClosingMonth { get; init; }
    public string? ClosingNo { get; init; }
    public DateTime? ClosingDueDate { get; init; }
    public bool PrintSumByPart { get; init; }
    public decimal? SupplyAmount { get; init; }
    public decimal? VatAmount { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal TotalQty { get; init; }
    public long ItemCount { get; init; }
    public string? ItemSummary { get; init; }
    public string? Remark { get; init; }
    public int RowVersion { get; init; }
}

public sealed class ShipmentItemDto
{
    public long ShipmentItemId { get; init; }
    public int LineNo { get; init; }
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public long? MainWorkId { get; init; }
    public string? MainLotNo { get; init; }
    public decimal ShipmentQty { get; init; }
    public decimal TestSpecimenQty { get; init; }
    public decimal? ShipmentWeight { get; init; }
    public decimal? ChargeCount { get; init; }
    public string PriceBasis { get; init; } = "EA";
    public decimal? UnitPrice { get; init; }
    public decimal? Amount { get; init; }
    public string? SubmitLotNo { get; init; }
    public string? CustomerLot { get; init; }
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
}

/// <summary>출하 재고 = 수주 × 출하 LOT(주 LOT) 의 출하 가능 수량 (구 수주별 출고잔량, S1)</summary>
public sealed class StockRowDto
{
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public DateTime OrderDate { get; init; }
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
    public string? CustomerLot { get; init; }
    public decimal OrderQty { get; init; }
    public decimal? UnitPrice { get; init; }
    public string PriceBasis { get; init; } = "EA";
    public decimal? UnitWeight { get; init; }
    public long? MainWorkId { get; init; }
    public string? MainLotNo { get; init; }
    public string? MainStatus { get; init; }
    public string? SubmitLotNo { get; init; }
    public decimal LotQty { get; init; }
    public decimal ShippedQty { get; init; }
    public decimal AvailableQty { get; init; }
    /// <summary>수주 전체 출하 잔량 (수주수량 − 출하 − 시험편) — LOT 합계 상한</summary>
    public decimal OrderRemainingQty { get; init; }
}

public sealed record ShipmentLineInput(long SalesOrderItemId, long? MainWorkId, decimal ShipmentQty, decimal TestSpecimenQty, decimal? ChargeCount);
public sealed record ShipmentSaveRequest(int? RowVersion, DateOnly ShipmentDate, long CustomerId, DateOnly? ClosingDueDate, bool PrintSumByPart, string? Remark,
    ShipmentLineInput[]? Items);
public sealed record ShipmentVersionRequest(int RowVersion, string? Reason);

/// <summary>
/// 출하(=납품) 전표 — 구 F_OutAddForm / F_OutForm (설계 §6·§17, legacy_forms/F_OutForm_F_OutAddForm_F_MonthlyClosing.md).
/// 한 트랜잭션(S8), 잔량은 계산(S10), 행마다 출하 LOT(S1), 시험편(S2), 단가 구분별 금액(S3), 거래처 사업자정보 Snapshot(S4).
/// 마감(CLOSED)된 전표는 수정·취소 불가.
/// </summary>
public sealed class ShipmentService(IDbConnectionFactory db, AuditWriter audit, ICurrentUser currentUser, SettingsCache settings, TimeProvider time)
{
    private const string Table = "shipment";

    internal const string ShipmentSql =
        """
        SELECT s.shipment_id, s.shipment_no, s.shipment_date, s.customer_id, COALESCE(s.customer_name_snapshot, c.customer_name) AS customer_name,
               s.status, s.closing_status, s.closing_year, s.closing_month, sc.closing_no, s.closing_due_date, s.print_sum_by_part,
               s.supply_amount, s.vat_amount, s.total_amount,
               (SELECT COALESCE(SUM(si.shipment_qty), 0) FROM shipment_item si WHERE si.shipment_id = s.shipment_id) AS total_qty,
               (SELECT COUNT(*) FROM shipment_item si WHERE si.shipment_id = s.shipment_id) AS item_count,
               (SELECT GROUP_CONCAT(DISTINCT si.part_name_snapshot ORDER BY si.line_no SEPARATOR ', ') FROM shipment_item si WHERE si.shipment_id = s.shipment_id) AS item_summary,
               s.remark, s.row_version
          FROM shipment s
          JOIN customer c ON c.customer_id = s.customer_id
          LEFT JOIN shipment_closing sc ON sc.shipment_closing_id = s.shipment_closing_id
        """;

    public async Task<IEnumerable<ShipmentDto>> ListAsync(DateOnly? from, DateOnly? to, long? customerId, string? search, bool includeCancelled, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        await using var conn = await db.OpenAsync(ct);
        return await conn.QueryAsync<ShipmentDto>(
            ShipmentSql + """
             WHERE s.is_deleted = 0 AND s.shipment_date BETWEEN @from AND @to AND (@customerId IS NULL OR s.customer_id = @customerId)
               AND (@includeCancelled OR s.status <> 'CANCELLED')
               AND (@search IS NULL OR s.shipment_no LIKE @like OR EXISTS (
                    SELECT 1 FROM shipment_item si JOIN sales_order_item soi ON soi.sales_order_item_id = si.sales_order_item_id
                      LEFT JOIN production_work mw ON mw.production_work_id = si.main_work_id
                     WHERE si.shipment_id = s.shipment_id AND (soi.order_item_no LIKE @like OR si.part_name_snapshot LIKE @like OR mw.lot_no LIKE @like
                           OR si.customer_lot_snapshot LIKE @like)))
             ORDER BY s.shipment_date DESC, s.shipment_no DESC
             LIMIT 2000
            """,
            new
            {
                from = (from ?? today.AddDays(-settings.GetInt(SettingKeys.SalesOrderListDefaultDays))).ToDateTime(TimeOnly.MinValue),
                to = (to ?? today).ToDateTime(TimeOnly.MinValue), customerId, includeCancelled,
                search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), like = $"%{search?.Trim()}%",
            });
    }

    public async Task<object> DetailAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var shipment = await conn.QuerySingleOrDefaultAsync<ShipmentDto>(ShipmentSql + " WHERE s.shipment_id = @id AND s.is_deleted = 0", new { id })
            ?? throw new NotFoundException(Table, id);
        var items = await conn.QueryAsync<ShipmentItemDto>(
            """
            SELECT si.shipment_item_id, si.line_no, si.sales_order_item_id, soi.order_item_no, si.main_work_id, mw.lot_no AS main_lot_no, si.shipment_qty,
                   si.test_specimen_qty, si.shipment_weight, si.charge_count, si.price_basis_snapshot AS price_basis, si.unit_price_snapshot AS unit_price, si.amount,
                   si.submit_lot_no_snapshot AS submit_lot_no, si.customer_lot_snapshot AS customer_lot, si.part_name_snapshot AS part_name,
                   si.part_number_snapshot AS part_number
              FROM shipment_item si
              JOIN sales_order_item soi ON soi.sales_order_item_id = si.sales_order_item_id
              LEFT JOIN production_work mw ON mw.production_work_id = si.main_work_id
             WHERE si.shipment_id = @id ORDER BY si.line_no
            """, new { id });
        return new { shipment, items };
    }

    /// <summary>
    /// 출하 재고 (구 재고 목록 = 수주 출고잔량) — 수주 × 주 LOT 별 출하 가능 수량.
    /// LOT 가능 = 주 LOT 투입 − 부적합(주 LOT·후공정, 처리구분 '출하' 특채 제외) − 이미 출하·시험편. 주 LOT 이 없는 수주(경로에 주공정 없음·이관)는 수주 단위.
    /// </summary>
    public async Task<IEnumerable<StockRowDto>> StockAsync(long customerId, long? excludeShipmentId, bool includeZero, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await StockAsync(conn, null, customerId, excludeShipmentId, includeZero, null);
    }

    private static async Task<IEnumerable<StockRowDto>> StockAsync(MySqlConnection conn, MySqlTransaction? tx, long customerId, long? excludeShipmentId, bool includeZero,
        long[]? itemIds)
    {
        var rows = await conn.QueryAsync<StockRowDto>(
            """
            SELECT x.*, x.lot_qty - x.shipped_qty AS available_qty
              FROM (
                SELECT soi.sales_order_item_id, soi.order_item_no, so.order_date, soi.part_name_snapshot AS part_name, soi.part_number_snapshot AS part_number,
                       soi.customer_lot, soi.order_qty, soi.unit_price, soi.price_basis, soi.unit_weight,
                       mw.production_work_id AS main_work_id, mw.lot_no AS main_lot_no, mw.status AS main_status, mw.submit_lot_no,
                       m.input_qty
                       - COALESCE((SELECT SUM(d.defect_qty) FROM defect_occurrence d
                                    WHERE d.production_work_input_id = m.production_work_input_id AND d.is_deleted = 0 AND d.status <> 'CANCELLED' AND COALESCE(d.decision, '') <> 'SHIP'), 0)
                       - COALESCE((SELECT SUM(d.defect_qty) FROM defect_occurrence d JOIN production_work_input pi ON pi.production_work_input_id = d.production_work_input_id
                                    WHERE pi.main_input_id = m.production_work_input_id AND d.is_deleted = 0 AND d.status <> 'CANCELLED' AND COALESCE(d.decision, '') <> 'SHIP'), 0) AS lot_qty,
                       COALESCE((SELECT SUM(si.shipment_qty + si.test_specimen_qty) FROM shipment_item si JOIN shipment s ON s.shipment_id = si.shipment_id
                                  WHERE si.sales_order_item_id = soi.sales_order_item_id AND si.main_work_id = mw.production_work_id
                                    AND s.status <> 'CANCELLED' AND s.is_deleted = 0 AND s.shipment_id <> @exclude), 0) AS shipped_qty,
                       soi.order_qty - COALESCE((SELECT SUM(si.shipment_qty + si.test_specimen_qty) FROM shipment_item si JOIN shipment s ON s.shipment_id = si.shipment_id
                                  WHERE si.sales_order_item_id = soi.sales_order_item_id AND s.status <> 'CANCELLED' AND s.is_deleted = 0 AND s.shipment_id <> @exclude), 0) AS order_remaining_qty
                  FROM sales_order_item soi
                  JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
                  JOIN production_work_input m ON m.sales_order_item_id = soi.sales_order_item_id AND m.main_work_id = m.production_work_id AND m.status <> 'CANCELLED'
                  JOIN production_work mw ON mw.production_work_id = m.production_work_id AND mw.is_deleted = 0 AND mw.status <> 'CANCELLED'
                 WHERE so.customer_id = @customerId AND soi.status <> 'CANCELLED' AND (@noFilter OR soi.sales_order_item_id IN @itemIds)
                UNION ALL
                SELECT soi.sales_order_item_id, soi.order_item_no, so.order_date, soi.part_name_snapshot, soi.part_number_snapshot, soi.customer_lot, soi.order_qty,
                       soi.unit_price, soi.price_basis, soi.unit_weight, NULL, NULL, NULL, NULL, soi.order_qty,
                       COALESCE((SELECT SUM(si.shipment_qty + si.test_specimen_qty) FROM shipment_item si JOIN shipment s ON s.shipment_id = si.shipment_id
                                  WHERE si.sales_order_item_id = soi.sales_order_item_id AND s.status <> 'CANCELLED' AND s.is_deleted = 0 AND s.shipment_id <> @exclude), 0),
                       soi.order_qty - COALESCE((SELECT SUM(si.shipment_qty + si.test_specimen_qty) FROM shipment_item si JOIN shipment s ON s.shipment_id = si.shipment_id
                                  WHERE si.sales_order_item_id = soi.sales_order_item_id AND s.status <> 'CANCELLED' AND s.is_deleted = 0 AND s.shipment_id <> @exclude), 0)
                  FROM sales_order_item soi
                  JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
                 WHERE so.customer_id = @customerId AND soi.status <> 'CANCELLED' AND (@noFilter OR soi.sales_order_item_id IN @itemIds)
                   AND NOT EXISTS (SELECT 1 FROM production_work_input m JOIN production_work mw ON mw.production_work_id = m.production_work_id
                                    WHERE m.sales_order_item_id = soi.sales_order_item_id AND m.main_work_id = m.production_work_id AND m.status <> 'CANCELLED'
                                      AND mw.is_deleted = 0 AND mw.status <> 'CANCELLED')
              ) x
             ORDER BY x.order_date, x.order_item_no, x.main_lot_no
            """, new { customerId, exclude = excludeShipmentId ?? 0, noFilter = itemIds is null, itemIds = itemIds ?? [0L] }, tx);
        return includeZero ? rows : rows.Where(r => r.AvailableQty > 0 && r.OrderRemainingQty > 0);
    }

    public Task<object> CreateAsync(ShipmentSaveRequest r, CancellationToken ct) => SaveAsync(null, r, ct);
    public Task<object> UpdateAsync(long id, ShipmentSaveRequest r, CancellationToken ct) => SaveAsync(id, r, ct);

    private sealed class CustomerRow
    {
        public string CustomerName { get; init; } = "";
        public string? BusinessNo { get; init; }
        public string? CeoName { get; init; }
        public string? BusinessType { get; init; }
        public string? BusinessItem { get; init; }
        public string? Address { get; init; }
        public string? AddressDetail { get; init; }
        public bool IsActive { get; init; }
    }

    private async Task<object> SaveAsync(long? id, ShipmentSaveRequest r, CancellationToken ct)
    {
        var lines = r.Items ?? [];
        var errors = new Dictionary<string, string[]>();
        if (lines.Length == 0) errors["items"] = ["출하할 품목을 담으세요."];
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].ShipmentQty < 0 || lines[i].TestSpecimenQty < 0 || lines[i].ShipmentQty + lines[i].TestSpecimenQty <= 0)
                errors[$"items[{i}]"] = ["출하 수량(또는 시험편)이 0보다 커야 합니다."];
        if (lines.GroupBy(l => (l.SalesOrderItemId, l.MainWorkId)).Any(g => g.Count() > 1)) errors["items"] = ["같은 수주·LOT 이 두 번 있습니다."];
        if (r.Remark?.Trim().Length > 255) errors["remark"] = ["255자 이하여야 합니다."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        ShipmentDto? before = null;
        if (id is { } existingId)
        {
            before = await LockAsync(conn, tx, existingId, r.RowVersion ?? -1);
            if (before.Status == "CANCELLED") throw new BusinessRuleException("SHIPMENT_CANCELLED", "취소된 전표입니다.");
            if (before.ClosingStatus == "CLOSED") throw new BusinessRuleException("SHIPMENT_CLOSED", "마감된 전표는 고칠 수 없습니다. 마감을 취소한 뒤 고치세요.");
            if (before.CustomerId != r.CustomerId) throw new RequestValidationException("customerId", "전표의 거래처는 바꿀 수 없습니다.");
        }
        var customer = await conn.QuerySingleOrDefaultAsync<CustomerRow>("SELECT * FROM customer WHERE customer_id = @CustomerId", r, tx);
        if (customer is null || (!customer.IsActive && id is null)) throw new RequestValidationException("customerId", "거래처를 선택하세요.");

        // 수주 행 잠금 → 재고(가능 수량)를 이 전표 제외하고 다시 계산해 검증 (동시 출하 직렬화)
        var itemIds = lines.Select(l => l.SalesOrderItemId).Distinct().ToArray();
        await conn.ExecuteAsync("SELECT sales_order_item_id FROM sales_order_item WHERE sales_order_item_id IN @itemIds FOR UPDATE", new { itemIds }, tx);
        var stock = (await StockAsync(conn, tx, r.CustomerId, id, includeZero: true, itemIds)).ToList();
        var rounding = settings.GetString(SettingKeys.SalesAmountRounding);
        var computed = new List<(ShipmentLineInput Line, StockRowDto Stock, decimal? Weight, decimal? Amount)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            var s = stock.FirstOrDefault(x => x.SalesOrderItemId == l.SalesOrderItemId && x.MainWorkId == l.MainWorkId)
                ?? throw new RequestValidationException($"items[{i}]", "이 거래처의 출하 가능한 수주·LOT 이 아닙니다.");
            var qty = l.ShipmentQty + l.TestSpecimenQty;
            if (qty > s.AvailableQty)
                throw new BusinessRuleException("SHIPMENT_EXCEEDS_STOCK", $"{s.OrderItemNo}{(s.MainLotNo is null ? "" : $" ({s.MainLotNo})")}: 출하 가능 수량({s.AvailableQty:0.###})을 넘습니다.");
            var orderTotal = lines.Where(x => x.SalesOrderItemId == l.SalesOrderItemId).Sum(x => x.ShipmentQty + x.TestSpecimenQty);
            if (orderTotal > s.OrderRemainingQty)
                throw new BusinessRuleException("SHIPMENT_EXCEEDS_ORDER", $"{s.OrderItemNo}: 수주 출하 잔량({s.OrderRemainingQty:0.###})을 넘습니다.");
            var weight = s.UnitWeight is { } w ? l.ShipmentQty * w : (decimal?)null;
            if (s.PriceBasis == "KG" && weight is null) throw new BusinessRuleException("WEIGHT_REQUIRED", $"{s.OrderItemNo}: KG 단가 품목인데 단중이 없어 금액을 계산할 수 없습니다.");
            if (s.PriceBasis == "CHARGE" && l.ChargeCount is null) throw new RequestValidationException($"items[{i}]", "CHARGE 단가 품목은 charge 수를 넣으세요.");
            var amount = ShipmentMath.Amount(s.PriceBasis, l.ShipmentQty, weight, l.ChargeCount, s.UnitPrice) is { } a ? ShipmentMath.Round(a, rounding) : (decimal?)null;
            computed.Add((l, s, weight, amount));
        }
        var (supply, vat, total) = ShipmentMath.Totals(computed.Select(c => c.Amount), settings.GetDecimal(SettingKeys.SalesVatRate), rounding);

        long shipmentId;
        string shipmentNo;
        var header = new
        {
            date = r.ShipmentDate.ToDateTime(TimeOnly.MinValue), r.CustomerId, due = r.ClosingDueDate?.ToDateTime(TimeOnly.MinValue), r.PrintSumByPart,
            supply, vat, total, remark = r.Remark?.Trim() is { Length: > 0 } rm ? rm : null, customer.CustomerName, customer.BusinessNo, customer.CeoName,
            customer.BusinessType, customer.BusinessItem, customer.Address, customer.AddressDetail, currentUser.UserId,
        };
        if (before is null)
        {
            await using var numbers = await DocumentNumbers.LockAsync(conn, tx, "shipment");
            shipmentNo = await numbers.NextAsync("shipment", "shipment_no", settings.GetString(SettingKeys.ShipmentNumberFormat), r.ShipmentDate);
            shipmentId = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO shipment (shipment_no, shipment_date, customer_id, closing_due_date, print_sum_by_part, supply_amount, vat_amount, total_amount, status,
                       customer_name_snapshot, customer_business_no_snapshot, customer_ceo_name_snapshot, customer_business_type_snapshot,
                       customer_business_item_snapshot, customer_address_snapshot, customer_address_detail_snapshot, remark, created_by, updated_by)
                VALUES (@no, @date, @CustomerId, @due, @PrintSumByPart, @supply, @vat, @total, 'SHIPPED',
                        @CustomerName, @BusinessNo, @CeoName, @BusinessType, @BusinessItem, @Address, @AddressDetail, @remark, @UserId, @UserId);
                SELECT LAST_INSERT_ID();
                """, new DynamicParameters(header).With("no", shipmentNo), tx);
        }
        else
        {
            shipmentId = before.ShipmentId;
            shipmentNo = before.ShipmentNo;
            await conn.ExecuteAsync(
                """
                UPDATE shipment SET shipment_date = @date, closing_due_date = @due, print_sum_by_part = @PrintSumByPart, supply_amount = @supply, vat_amount = @vat,
                       total_amount = @total, remark = @remark, updated_by = @UserId, row_version = row_version + 1
                 WHERE shipment_id = @id
                """, new DynamicParameters(header).With("id", shipmentId), tx);
            await conn.ExecuteAsync("DELETE FROM shipment_item WHERE shipment_id = @shipmentId", new { shipmentId }, tx);
        }
        for (var i = 0; i < computed.Count; i++)
        {
            var (l, s, weight, amount) = computed[i];
            await conn.ExecuteAsync(
                """
                INSERT INTO shipment_item (shipment_id, line_no, sales_order_item_id, main_work_id, shipment_qty, test_specimen_qty, shipment_weight, charge_count,
                       price_basis_snapshot, unit_price_snapshot, amount, submit_lot_no_snapshot, customer_lot_snapshot, part_name_snapshot, part_number_snapshot,
                       specification_snapshot, material_snapshot)
                SELECT @shipmentId, @lineNo, soi.sales_order_item_id, @MainWorkId, @ShipmentQty, @TestSpecimenQty, @weight, @ChargeCount,
                       soi.price_basis, soi.unit_price, @amount, @submit, soi.customer_lot, soi.part_name_snapshot, soi.part_number_snapshot,
                       soi.specification_snapshot, soi.material_snapshot
                  FROM sales_order_item soi WHERE soi.sales_order_item_id = @SalesOrderItemId
                """,
                new { shipmentId, lineNo = i + 1, l.MainWorkId, l.ShipmentQty, l.TestSpecimenQty, weight, l.ChargeCount, amount, submit = s.SubmitLotNo, l.SalesOrderItemId }, tx);
        }
        await audit.WriteAsync(conn, tx, before is null ? AuditAction.Create : AuditAction.Update, Table, shipmentId,
            before is null ? null : new { before.SupplyAmount, before.TotalQty },
            new { shipment_no = shipmentNo, supply, vat, total, items = lines });
        await tx.CommitAsync(ct);
        return new { shipmentId, shipmentNo, supplyAmount = supply, vatAmount = vat, totalAmount = total };
    }

    /// <summary>전표 취소 (구 목록 삭제 → 수주 출고수량 복원) — 잔량은 VIEW 가 자동 반영 (S10). 마감된 전표는 불가</summary>
    public async Task CancelAsync(long id, ShipmentVersionRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id, r.RowVersion);
        if (before.Status == "CANCELLED") return;
        if (before.ClosingStatus == "CLOSED") throw new BusinessRuleException("SHIPMENT_CLOSED", "마감된 전표는 취소할 수 없습니다. 마감을 취소한 뒤 하세요.");
        await conn.ExecuteAsync(
            "UPDATE shipment SET status = 'CANCELLED', closing_status = 'UNCLOSED', closing_year = NULL, closing_month = NULL, updated_by = @UserId, row_version = row_version + 1 WHERE shipment_id = @id",
            new { id, currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id, new { before.Status }, new { status = "CANCELLED" }, r.Reason);
        await tx.CommitAsync(ct);
    }

    public async Task EnsureExistsAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM shipment WHERE shipment_id = @id AND is_deleted = 0 AND status <> 'CANCELLED'", new { id }) == 0)
            throw new NotFoundException(Table, id);
    }

    private static async Task<ShipmentDto> LockAsync(MySqlConnection conn, MySqlTransaction tx, long id, int rowVersion)
    {
        var version = await conn.ExecuteScalarAsync<int?>("SELECT row_version FROM shipment WHERE shipment_id = @id AND is_deleted = 0 FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException(Table, id);
        if (version != rowVersion) throw new ConcurrencyConflictException(Table, id);
        return await conn.QuerySingleAsync<ShipmentDto>(ShipmentSql + " WHERE s.shipment_id = @id", new { id }, tx);
    }
}

internal static class DynamicParametersExtensions
{
    public static DynamicParameters With(this DynamicParameters p, string name, object? value)
    {
        p.Add(name, value);
        return p;
    }
}
