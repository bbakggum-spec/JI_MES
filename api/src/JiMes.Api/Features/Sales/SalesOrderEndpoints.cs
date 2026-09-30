using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Numbering;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Sales;

/// <summary>수주(입고) 품목 행 = 현장에서 스캔하는 입고번호 1개 (구 t_income 1행)</summary>
public sealed class SalesOrderItemDto
{
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public long SalesOrderId { get; init; }
    public string SalesOrderNo { get; init; } = "";
    public DateTime OrderDate { get; init; }
    public DateTime? DueDate { get; init; }
    public long CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public int LineNo { get; init; }
    public long PartId { get; init; }
    public string PartCode { get; init; } = "";
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
    public string? Specification { get; init; }
    public string? Model { get; init; }
    public string? Material { get; init; }
    public long? HeatProcessId { get; init; }
    public long? HeatProcessVersionId { get; init; }
    public string? HeatProcessName { get; init; }
    public string? CustomerLot { get; init; }
    public string? CoilNo { get; init; }
    public decimal OrderQty { get; init; }
    public decimal? OrderWeight { get; init; }
    public decimal? UnitWeight { get; init; }
    public decimal? UnitPrice { get; init; }
    public string? UnitCode { get; init; }
    public string PriceBasis { get; init; } = "EA";
    public int Priority { get; init; }
    public string? CustomerWorkOrderNo { get; init; }
    public bool IsSeparatelyManaged { get; init; }
    public bool IsReturn { get; init; }
    public string Status { get; init; } = "";
    public string? Hardness { get; init; }
    public string? CoreHardness { get; init; }
    public string? CaseDepth { get; init; }
    public string? Texture { get; init; }
    public string? Remark { get; init; }
    public decimal MainInputQty { get; init; }
    public decimal ShipmentQty { get; init; }
    public decimal RemainingShipmentQty { get; init; }
    /// <summary>단위공정별 계획·투입 중 가장 큰 수량 — 수량을 이보다 줄일 수 없다</summary>
    public decimal UsedQty { get; init; }
    public bool IsScheduledOrInput { get; init; }
    public uint RowVersion { get; init; }
}

/// <summary>수주 등록 화면의 품목 후보 (거래처 품목 + 적용 공정)</summary>
public sealed class PartCandidateDto
{
    public long PartId { get; init; }
    public string PartCode { get; init; } = "";
    public string PartName { get; init; } = "";
    public string? PartNumber { get; init; }
    public string? Specification { get; init; }
    public string? Model { get; init; }
    public string? Material { get; init; }
    public decimal? UnitWeight { get; init; }
    public decimal? UnitPrice { get; init; }
    public string PriceBasis { get; init; } = "EA";
    public string? CustomerPartCode { get; init; }
    public bool IsCustomerLotRequired { get; init; }
    public bool IsCustomerPart { get; init; }
    public List<HeatProcessOption> HeatProcesses { get; set; } = [];
}

public sealed class HeatProcessOption
{
    public long PartId { get; init; }
    public long HeatProcessId { get; init; }
    public string HeatProcessName { get; init; } = "";
    public bool IsDefault { get; init; }
}

public sealed record SalesOrderLineInput(
    long PartId, long? HeatProcessId, decimal OrderQty, decimal? UnitPrice, string? CustomerLot, string? CoilNo,
    string? CustomerWorkOrderNo, int? Priority, bool IsSeparatelyManaged, string? Remark);

public sealed record SalesOrderCreateRequest(
    DateOnly OrderDate, DateOnly? DueDate, long CustomerId, bool IsReturn, string? Remark, SalesOrderLineInput[]? Items);

public sealed record SalesOrderItemUpdateRequest(
    uint RowVersion, long? HeatProcessId, decimal OrderQty, decimal? UnitPrice, string? CustomerLot, string? CoilNo,
    string? CustomerWorkOrderNo, int Priority, bool IsSeparatelyManaged, string? Remark);

public sealed record RowVersionRequest(uint RowVersion, string? Reason);

/// <summary>
/// 수주(입고) — 구 F_IncomeAddForm / F_IncomeForm (설계 §17, legacy_forms/F_IncomeForm_F_IncomeAddForm.md).
/// 한 번 등록 = 묶음(sales_order) 1개 + 품목 행마다 입고번호(order_item_no) 1개. 품목 스펙은 행에 Snapshot.
/// 잔량(투입·출하·배정)은 저장하지 않고 VIEW·계획·투입에서 계산 (N7).
/// </summary>
public static class SalesOrderEndpoints
{
    public static void MapSalesOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sales-orders").WithTags("SalesOrder");
        const string key = MenuKeys.SalesOrder;
        g.MapGet("/items", ListAsync).RequirePermission(key, PermissionAction.Read);
        g.MapGet("/items/{itemId:long}", async (long itemId, IDbConnectionFactory db, CancellationToken ct) =>
            {
                await using var conn = await db.OpenAsync(ct);
                return Results.Ok(await GetItemAsync(conn, null, itemId) ?? throw new NotFoundException("sales_order_item", itemId));
            })
            .RequirePermission(key, PermissionAction.Read);
        g.MapGet("/part-candidates", PartCandidatesAsync).RequirePermission(key, PermissionAction.Read);
        g.MapPost("/", CreateAsync).RequirePermission(key, PermissionAction.Create);
        g.MapPut("/items/{itemId:long}", UpdateItemAsync).RequirePermission(key, PermissionAction.Update);
        g.MapPost("/items/{itemId:long}/cancel", CancelItemAsync).RequirePermission(key, PermissionAction.Delete);
    }

    private const string ItemSql =
        """
        SELECT soi.sales_order_item_id, soi.order_item_no, so.sales_order_id, so.sales_order_no, so.order_date, so.due_date,
               so.customer_id, c.customer_name, soi.line_no, soi.part_id, p.part_code,
               soi.part_name_snapshot AS part_name, soi.part_number_snapshot AS part_number, soi.specification_snapshot AS specification,
               soi.model_snapshot AS model, soi.material_snapshot AS material,
               hv.heat_process_id, soi.heat_process_version_id, soi.heat_process_name_snapshot AS heat_process_name,
               soi.customer_lot, soi.coil_no, soi.order_qty, soi.order_weight, soi.unit_weight, soi.unit_price, soi.unit_code, soi.price_basis,
               soi.priority, soi.customer_work_order_no, soi.is_separately_managed, soi.is_return, soi.status,
               soi.hardness_snapshot AS hardness, soi.core_hardness_snapshot AS core_hardness, soi.case_depth_snapshot AS case_depth,
               soi.texture_snapshot AS texture, soi.remark,
               COALESCE(pr.main_input_qty, 0) AS main_input_qty, COALESCE(pr.shipment_qty, 0) AS shipment_qty,
               COALESCE(pr.remaining_shipment_qty, soi.order_qty) AS remaining_shipment_qty,
               GREATEST(COALESCE(u.scheduled_qty, 0), COALESCE(u.input_qty, 0), COALESCE(pr.shipment_qty, 0) + COALESCE(pr.test_specimen_qty, 0)) AS used_qty,
               (u.sales_order_item_id IS NOT NULL) AS is_scheduled_or_input,
               so.row_version
          FROM sales_order_item soi
          JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
          JOIN customer c     ON c.customer_id = so.customer_id
          JOIN part p         ON p.part_id = soi.part_id
          LEFT JOIN heat_process_version hv ON hv.heat_process_version_id = soi.heat_process_version_id
          LEFT JOIN vw_sales_order_item_progress pr ON pr.sales_order_item_id = soi.sales_order_item_id
          LEFT JOIN (
                -- 단위공정별 계획·투입 수량 중 최대 (재작업 제외)
                SELECT x.sales_order_item_id, MAX(x.scheduled_qty) AS scheduled_qty, MAX(x.input_qty) AS input_qty
                  FROM (SELECT psi.sales_order_item_id, ps.unit_process_id, SUM(psi.planned_qty) AS scheduled_qty, 0 AS input_qty
                          FROM production_schedule_item psi
                          JOIN production_schedule ps ON ps.production_schedule_id = psi.production_schedule_id
                         WHERE ps.status <> 'CANCELLED' AND ps.is_deleted = 0 AND ps.is_rework = 0
                         GROUP BY psi.sales_order_item_id, ps.unit_process_id
                        UNION ALL
                        SELECT pwi.sales_order_item_id, w.unit_process_id, 0, SUM(pwi.input_qty)
                          FROM production_work_input pwi
                          JOIN production_work w ON w.production_work_id = pwi.production_work_id
                         WHERE pwi.status <> 'CANCELLED' AND w.is_deleted = 0 AND w.is_rework = 0
                         GROUP BY pwi.sales_order_item_id, w.unit_process_id) x
                 GROUP BY x.sales_order_item_id
          ) u ON u.sales_order_item_id = soi.sales_order_item_id
        """;

    /// <param name="from">입고일 시작 (없으면 오늘 − 설정 sales_order.list_default_days)</param>
    /// <param name="openOnly">출하 잔량이 있는 미취소 행만</param>
    private static async Task<IResult> ListAsync(IDbConnectionFactory db, SettingsCache settings, TimeProvider time, CancellationToken ct,
        DateOnly? from = null, DateOnly? to = null, long? customerId = null, string? search = null, bool openOnly = false, bool includeCancelled = false)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        from ??= today.AddDays(-settings.GetInt(SettingKeys.SalesOrderListDefaultDays));
        to ??= today;
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<SalesOrderItemDto>(
            ItemSql + """
             WHERE so.order_date BETWEEN @from AND @to AND so.is_deleted = 0
               AND (@customerId IS NULL OR so.customer_id = @customerId)
               AND (@includeCancelled OR soi.status <> 'CANCELLED')
               AND (NOT @openOnly OR (soi.status <> 'CANCELLED' AND COALESCE(pr.remaining_shipment_qty, soi.order_qty) > 0))
               AND (@search IS NULL OR soi.order_item_no LIKE @like OR so.sales_order_no LIKE @like OR p.part_code LIKE @like
                    OR soi.part_name_snapshot LIKE @like OR soi.part_number_snapshot LIKE @like OR soi.customer_lot LIKE @like
                    OR soi.customer_work_order_no LIKE @like OR soi.model_snapshot LIKE @like)
             ORDER BY so.order_date DESC, soi.order_item_no DESC
             LIMIT 2000
            """,
            new
            {
                from = from.Value.ToDateTime(TimeOnly.MinValue), to = to.Value.ToDateTime(TimeOnly.MinValue), customerId, includeCancelled, openOnly,
                search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), like = $"%{search?.Trim()}%",
            });
        return Results.Ok(new { from, to, items = rows });
    }

    internal static Task<SalesOrderItemDto?> GetItemAsync(MySqlConnection conn, MySqlTransaction? tx, long itemId) =>
        conn.QuerySingleOrDefaultAsync<SalesOrderItemDto>(ItemSql + " WHERE soi.sales_order_item_id = @itemId", new { itemId }, tx);

    /// <summary>거래처 품목(구 거래처 품목 목록) — all=true 면 전체 품목에서 검색 (거래처 품목이 먼저)</summary>
    private static async Task<IResult> PartCandidatesAsync(IDbConnectionFactory db, CancellationToken ct, long customerId, string? search = null, bool all = false)
    {
        await using var conn = await db.OpenAsync(ct);
        var parts = (await conn.QueryAsync<PartCandidateDto>(
            """
            SELECT p.part_id, p.part_code, p.part_name, p.part_number, p.specification, p.model, p.material, p.unit_weight, p.unit_price,
                   p.price_basis, pc.customer_part_code, COALESCE(pc.is_customer_lot_required, 0) AS is_customer_lot_required,
                   (pc.part_customer_id IS NOT NULL) AS is_customer_part
              FROM part p
              LEFT JOIN part_customer pc ON pc.part_id = p.part_id AND pc.customer_id = @customerId AND pc.is_active = 1
             WHERE p.is_active = 1 AND (@all OR pc.part_customer_id IS NOT NULL)
               AND (@search IS NULL OR p.part_code LIKE @like OR p.part_name LIKE @like OR p.part_number LIKE @like
                    OR p.model LIKE @like OR p.specification LIKE @like OR pc.customer_part_code LIKE @like)
             ORDER BY is_customer_part DESC, p.part_name
             LIMIT 300
            """, new { customerId, all, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), like = $"%{search?.Trim()}%" })).ToList();
        if (parts.Count > 0)
        {
            var processes = await conn.QueryAsync<HeatProcessOption>(
                """
                SELECT php.part_id, h.heat_process_id, h.heat_process_name, php.is_default
                  FROM part_heat_process php JOIN heat_process h ON h.heat_process_id = php.heat_process_id
                 WHERE php.part_id IN @ids AND php.is_active = 1 AND h.is_active = 1
                 ORDER BY php.is_default DESC, h.heat_process_name
                """, new { ids = parts.Select(p => p.PartId).ToArray() });
            var byPart = processes.ToLookup(x => x.PartId);
            foreach (var p in parts) p.HeatProcesses = byPart[p.PartId].ToList();
        }
        return Results.Ok(parts);
    }

    private sealed class PartRow
    {
        public long PartId { get; init; }
        public string PartName { get; init; } = "";
        public string? PartNumber { get; init; }
        public string? Specification { get; init; }
        public string? Model { get; init; }
        public string? Material { get; init; }
        public decimal? UnitWeight { get; init; }
        public string? UnitCode { get; init; }
        public string PriceBasis { get; init; } = "EA";
        public decimal? UnitPrice { get; init; }
        public string? Hardness { get; init; }
        public string? CoreHardness { get; init; }
        public string? EffectiveHardeningDepth { get; init; }
        public string? Texture { get; init; }
        public bool IsActive { get; init; }
    }

    private sealed class RouteRow
    {
        public long HeatProcessVersionId { get; init; }
        public string HeatProcessName { get; init; } = "";
    }

    /// <summary>공정 → 현재 Version (수주 행은 Version 을 참조 — 이후 경로가 바뀌어도 이 수주는 당시 경로)</summary>
    private static Task<RouteRow?> CurrentRouteAsync(MySqlConnection conn, MySqlTransaction tx, long heatProcessId) =>
        conn.QuerySingleOrDefaultAsync<RouteRow>(
            """
            SELECT v.heat_process_version_id, h.heat_process_name
              FROM heat_process h JOIN heat_process_version v ON v.heat_process_id = h.heat_process_id AND v.is_current = 1
             WHERE h.heat_process_id = @heatProcessId AND h.is_active = 1
            """, new { heatProcessId }, tx);

    private static async Task<IResult> CreateAsync(SalesOrderCreateRequest r, IDbConnectionFactory db, AuditWriter audit, SettingsCache settings,
        ICurrentUser user, CancellationToken ct)
    {
        var lines = r.Items ?? [];
        var errors = new Dictionary<string, string[]>();
        if (lines.Length == 0) errors["items"] = ["품목을 1개 이상 담으세요."];
        for (var i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            var e = new List<string>();
            if (l.OrderQty <= 0) e.Add("수량은 0보다 커야 합니다.");
            if (l.UnitPrice < 0) e.Add("단가는 0 이상이어야 합니다.");
            if (l.Priority is < 0 or > 3) e.Add("우선순위가 올바르지 않습니다.");
            if (Longer(l.CustomerLot, 100) || Longer(l.CoilNo, 100) || Longer(l.CustomerWorkOrderNo, 100) || Longer(l.Remark, 255)) e.Add("입력이 너무 깁니다.");
            if (e.Count > 0) errors[$"items[{i}]"] = [.. e];
        }
        if (Longer(r.Remark, 255)) errors["remark"] = ["255자 이하여야 합니다."];
        if (r.DueDate is { } due && due < r.OrderDate) errors["dueDate"] = ["납기는 입고일 이후여야 합니다."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM customer WHERE customer_id = @CustomerId AND is_active = 1", r, tx) == 0)
            throw new RequestValidationException("customerId", "거래처를 선택하세요.");
        var parts = (await conn.QueryAsync<PartRow>(
                "SELECT * FROM part WHERE part_id IN @ids", new { ids = lines.Select(l => l.PartId).Distinct().ToArray() }, tx))
            .ToDictionary(p => p.PartId);
        var defaults = (await conn.QueryAsync<(long PartId, long HeatProcessId)>(
                "SELECT CAST(part_id AS SIGNED), CAST(heat_process_id AS SIGNED) FROM part_heat_process WHERE part_id IN @ids AND is_default = 1 AND is_active = 1",
                new { ids = parts.Keys.ToArray() }, tx))
            .GroupBy(x => x.PartId).ToDictionary(g => g.Key, g => g.First().HeatProcessId);

        await using var numbers = await DocumentNumbers.LockAsync(conn, tx, "sales_order");
        var orderNo = await numbers.NextAsync("sales_order", "sales_order_no", settings.GetString(SettingKeys.SalesOrderNumberFormat), r.OrderDate);
        var orderId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO sales_order (sales_order_no, order_date, received_date, due_date, customer_id, remark, created_by, updated_by)
            VALUES (@orderNo, @orderDate, @orderDate, @dueDate, @CustomerId, @remark, @u, @u); SELECT LAST_INSERT_ID();
            """, new { orderNo, orderDate = r.OrderDate.ToDateTime(TimeOnly.MinValue), dueDate = r.DueDate?.ToDateTime(TimeOnly.MinValue), r.CustomerId, remark = Trim(r.Remark), u = user.UserId }, tx);

        var itemFormat = settings.GetString(SettingKeys.SalesOrderItemNumberFormat);
        var created = new List<object>();
        for (var i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (!parts.TryGetValue(l.PartId, out var part) || !part.IsActive)
                throw new RequestValidationException($"items[{i}]", "없거나 사용 중지된 품목입니다.");
            var heatProcessId = l.HeatProcessId ?? defaults.GetValueOrDefault(l.PartId);
            RouteRow? route = null;
            if (heatProcessId != 0)
                route = await CurrentRouteAsync(conn, tx, heatProcessId)
                    ?? throw new RequestValidationException($"items[{i}]", "공정 경로가 없거나 사용 중지되었습니다.");
            var itemNo = await numbers.NextAsync("sales_order_item", "order_item_no", itemFormat, r.OrderDate);
            var itemId = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, heat_process_version_id, customer_lot, coil_no,
                       order_qty, order_weight, unit_weight, unit_price, unit_code, price_basis, priority, customer_work_order_no,
                       is_separately_managed, is_return, part_name_snapshot, part_number_snapshot, specification_snapshot, model_snapshot,
                       material_snapshot, heat_process_name_snapshot, hardness_snapshot, core_hardness_snapshot, case_depth_snapshot,
                       texture_snapshot, remark)
                VALUES (@itemNo, @orderId, @lineNo, @PartId, @versionId, @customerLot, @coilNo,
                        @OrderQty, @weight, @UnitWeight, @price, @UnitCode, @PriceBasis, @priority, @workOrderNo,
                        @IsSeparatelyManaged, @IsReturn, @PartName, @PartNumber, @Specification, @Model,
                        @Material, @routeName, @Hardness, @CoreHardness, @EffectiveHardeningDepth,
                        @Texture, @remark);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    itemNo, orderId, lineNo = i + 1, l.PartId, versionId = route?.HeatProcessVersionId, customerLot = Trim(l.CustomerLot), coilNo = Trim(l.CoilNo),
                    l.OrderQty, weight = part.UnitWeight is { } w ? l.OrderQty * w : (decimal?)null, part.UnitWeight, price = l.UnitPrice ?? part.UnitPrice,
                    part.UnitCode, part.PriceBasis, priority = l.Priority ?? 1, workOrderNo = Trim(l.CustomerWorkOrderNo), l.IsSeparatelyManaged, r.IsReturn,
                    part.PartName, part.PartNumber, part.Specification, part.Model, part.Material, routeName = route?.HeatProcessName,
                    part.Hardness, part.CoreHardness, part.EffectiveHardeningDepth, part.Texture, remark = Trim(l.Remark),
                }, tx);
            created.Add(new { salesOrderItemId = itemId, orderItemNo = itemNo, l.PartId, part.PartName, l.OrderQty, heatProcess = route?.HeatProcessName });
        }
        await audit.WriteAsync(conn, tx, AuditAction.Create, "sales_order", orderId, null,
            new { sales_order_no = orderNo, r.OrderDate, r.DueDate, r.CustomerId, r.IsReturn, items = created });
        await tx.CommitAsync(ct);
        return Results.Ok(new { salesOrderId = orderId, salesOrderNo = orderNo, items = created });
    }

    private static async Task<IResult> UpdateItemAsync(long itemId, SalesOrderItemUpdateRequest r, IDbConnectionFactory db, AuditWriter audit,
        ICurrentUser user, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.OrderQty <= 0) errors["orderQty"] = ["수량은 0보다 커야 합니다."];
        if (r.UnitPrice < 0) errors["unitPrice"] = ["단가는 0 이상이어야 합니다."];
        if (r.Priority is < 0 or > 3) errors["priority"] = ["우선순위가 올바르지 않습니다."];
        if (Longer(r.CustomerLot, 100) || Longer(r.CoilNo, 100) || Longer(r.CustomerWorkOrderNo, 100) || Longer(r.Remark, 255))
            errors["remark"] = ["입력이 너무 깁니다."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockItemAsync(conn, tx, itemId);
        if (before.Status == "CANCELLED") throw new BusinessRuleException("ORDER_ITEM_CANCELLED", "취소된 입고 행은 수정할 수 없습니다.");
        if (r.OrderQty < before.UsedQty)
            throw new BusinessRuleException("QTY_BELOW_USED", $"수량을 이미 계획·투입·출하한 수량({before.UsedQty:0.###})보다 줄일 수 없습니다.");

        var versionId = before.HeatProcessVersionId;
        var routeName = before.HeatProcessName;
        if (r.HeatProcessId != before.HeatProcessId)
        {
            if (before.IsScheduledOrInput)
                throw new BusinessRuleException("ROUTE_IN_USE", "계획·투입이 있는 입고 행은 공정을 바꿀 수 없습니다. 계획을 취소한 뒤 바꾸세요.");
            var route = r.HeatProcessId is { } hp
                ? await CurrentRouteAsync(conn, tx, hp) ?? throw new RequestValidationException("heatProcessId", "공정 경로가 없거나 사용 중지되었습니다.")
                : null;
            versionId = route?.HeatProcessVersionId;
            routeName = route?.HeatProcessName;
        }

        await conn.ExecuteVersionedUpdateAsync(
            "UPDATE sales_order SET row_version = row_version + 1, updated_by = @u WHERE sales_order_id = @id AND row_version = @RowVersion",
            new { id = before.SalesOrderId, r.RowVersion, u = user.UserId }, "sales_order", before.SalesOrderId, tx);
        await conn.ExecuteAsync(
            """
            UPDATE sales_order_item
               SET heat_process_version_id = @versionId, heat_process_name_snapshot = @routeName, order_qty = @OrderQty,
                   order_weight = IF(unit_weight IS NULL, NULL, @OrderQty * unit_weight), unit_price = @UnitPrice,
                   customer_lot = @customerLot, coil_no = @coilNo, customer_work_order_no = @workOrderNo, priority = @Priority,
                   is_separately_managed = @IsSeparatelyManaged, remark = @remark
             WHERE sales_order_item_id = @itemId
            """,
            new
            {
                itemId, versionId, routeName, r.OrderQty, r.UnitPrice, customerLot = Trim(r.CustomerLot), coilNo = Trim(r.CoilNo),
                workOrderNo = Trim(r.CustomerWorkOrderNo), r.Priority, r.IsSeparatelyManaged, remark = Trim(r.Remark),
            }, tx);
        var after = await GetItemAsync(conn, tx, itemId);
        await audit.WriteAsync(conn, tx, AuditAction.Update, "sales_order_item", itemId, Snapshot(before), Snapshot(after!));
        await tx.CommitAsync(ct);
        return Results.Ok(after);
    }

    /// <summary>입고 행 취소 (구 목록 삭제) — 계획·투입이 있으면 먼저 취소해야 한다. 행이 모두 취소되면 묶음도 취소</summary>
    private static async Task<IResult> CancelItemAsync(long itemId, RowVersionRequest r, IDbConnectionFactory db, AuditWriter audit,
        ICurrentUser user, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockItemAsync(conn, tx, itemId);
        if (before.Status == "CANCELLED") return Results.NoContent();
        if (before.IsScheduledOrInput || before.ShipmentQty > 0)
            throw new BusinessRuleException("ORDER_ITEM_IN_USE", "계획·투입·출하가 있는 입고 행은 취소할 수 없습니다. 계획·투입을 먼저 취소하세요.");
        await conn.ExecuteVersionedUpdateAsync(
            "UPDATE sales_order SET row_version = row_version + 1, updated_by = @u WHERE sales_order_id = @id AND row_version = @RowVersion",
            new { id = before.SalesOrderId, r.RowVersion, u = user.UserId }, "sales_order", before.SalesOrderId, tx);
        await conn.ExecuteAsync("UPDATE sales_order_item SET status = 'CANCELLED' WHERE sales_order_item_id = @itemId", new { itemId }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE sales_order SET status = 'CANCELLED'
             WHERE sales_order_id = @id AND NOT EXISTS (SELECT 1 FROM sales_order_item WHERE sales_order_id = @id AND status <> 'CANCELLED')
            """, new { id = before.SalesOrderId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.StatusChange, "sales_order_item", itemId,
            new { before.OrderItemNo, before.Status }, new { status = "CANCELLED" }, r.Reason);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    /// <summary>행 잠금 후 조회 (수량·공정 검증과 수정 사이에 계획이 끼어들지 않게)</summary>
    private static async Task<SalesOrderItemDto> LockItemAsync(MySqlConnection conn, MySqlTransaction tx, long itemId)
    {
        if (await conn.ExecuteScalarAsync<long?>("SELECT sales_order_item_id FROM sales_order_item WHERE sales_order_item_id = @itemId FOR UPDATE", new { itemId }, tx) is null)
            throw new NotFoundException("sales_order_item", itemId);
        return (await GetItemAsync(conn, tx, itemId))!;
    }

    private static object Snapshot(SalesOrderItemDto d) => new
    {
        d.OrderItemNo, d.HeatProcessVersionId, d.OrderQty, d.UnitPrice, d.CustomerLot, d.CoilNo, d.CustomerWorkOrderNo, d.Priority,
        d.IsSeparatelyManaged, d.Remark, d.Status,
    };

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static bool Longer(string? s, int max) => s is not null && s.Trim().Length > max;
}
