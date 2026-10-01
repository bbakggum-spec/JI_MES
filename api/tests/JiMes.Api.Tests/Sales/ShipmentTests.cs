using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using JiMes.Api.Features.Sales;

namespace JiMes.Api.Tests.Sales;

public sealed class ShipmentMathTests
{
    [Fact]
    public void Amount_follows_price_basis()
    {
        Assert.Equal(6000m, ShipmentMath.Amount("EA", 60, 120, null, 100));
        Assert.Equal(12000m, ShipmentMath.Amount("KG", 60, 120, null, 100));    // 중량 × 단가 (구: 항상 수량 × 단가)
        Assert.Equal(300m, ShipmentMath.Amount("CHARGE", 60, 120, 3, 100));
        Assert.Null(ShipmentMath.Amount("KG", 60, null, null, 100));
        Assert.Null(ShipmentMath.Amount("EA", 60, null, null, null));
    }

    [Fact]
    public void Rounding_totals_and_closing_date()
    {
        Assert.Equal((1000m, 100m, 1100m), ShipmentMath.Totals([500.4m, 499.6m], 0.1m, "ROUND"));   // 행마다 반올림 후 합
        Assert.Equal(100m, ShipmentMath.Round(100.9m, "FLOOR"));
        Assert.Equal(101m, ShipmentMath.Round(100.1m, "CEIL"));
        Assert.Equal(new DateOnly(2026, 2, 28), ShipmentMath.ClosingDate(2026, 2, 31));   // 31 = 말일
        Assert.Equal(new DateOnly(2026, 10, 25), ShipmentMath.ClosingDate(2026, 10, 25));
        Assert.Equal(new DateOnly(2026, 10, 31), ShipmentMath.ClosingDate(2026, 10, null));
    }
}

/// <summary>출하 전표 (재고 = 수주 × 주 LOT) → 마감·이월·마감 취소 (설계 §23.7)</summary>
[Collection(ApiCollection.Name)]
public sealed class ShipmentTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private long _customer, _other, _itemA, _itemB, _itemC, _mainLot;

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var c = await fx.OpenAsync();
        async Task<long> Insert(string sql, object? args = null) => await c.ExecuteScalarAsync<long>(sql + "; SELECT LAST_INSERT_ID();", args);
        _customer = await Insert(
            "INSERT INTO customer (customer_code, customer_name, business_no, ceo_name, address, closing_day) VALUES (@code, '한독기어', '123-45-67890', '홍길동', '창원시', 25)",
            new { code = $"C-{tag}" });
        _other = await Insert("INSERT INTO customer (customer_code, customer_name) VALUES (@code, '다른업체')", new { code = $"O-{tag}" });
        var unit = await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@code, '침탄')", new { code = $"U-{tag}" });
        var part = await Insert("INSERT INTO part (part_code, part_name) VALUES (@code, '기어')", new { code = $"P-{tag}" });
        var order = await Insert("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@no, CURDATE(), @_customer)", new { no = $"SO-{tag}", _customer });
        async Task<long> Item(int line, decimal qty, string basis, decimal price, decimal? unitWeight) => await Insert(
            """
            INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, order_qty, price_basis, unit_price, unit_weight, part_name_snapshot, customer_lot)
            VALUES (@no, @order, @line, @part, @qty, @basis, @price, @unitWeight, '기어', 'CL-9')
            """, new { no = $"I-{tag}-{line}", order, line, part, qty, basis, price, unitWeight });
        _itemA = await Item(1, 100, "EA", 1000, 0.5m);
        _itemB = await Item(2, 50, "KG", 500, 2m);
        _itemC = await Item(3, 20, "EA", 300, null);   // 주 LOT 없음 → 수주 단위 재고
        _mainLot = await Insert(
            "INSERT INTO production_work (lot_no, unit_process_id, work_date, is_main_process, status, submit_lot_no) VALUES (@lot, @unit, CURDATE(), 1, 'COMPLETED', 'HD-77')",
            new { lot = $"L-{tag}", unit });
        async Task<long> Input(long item, decimal qty) => await Insert(
            "INSERT INTO production_work_input (production_work_id, sales_order_item_id, main_work_id, input_qty) VALUES (@_mainLot, @item, @_mainLot, @qty)",
            new { _mainLot, item, qty });
        var inputA = await Input(_itemA, 100);
        await Input(_itemB, 50);
        // 부적합 10 (미결정) → 출하 불가, 5 (처리구분 출하 = 특채) → 출하 가능
        await c.ExecuteAsync(
            """
            INSERT INTO defect_occurrence (sales_order_item_id, production_work_id, production_work_input_id, main_work_id, defect_date, defect_qty, status, decision)
            VALUES (@_itemA, @_mainLot, @inputA, @_mainLot, CURDATE(), 10, 'OPEN', NULL), (@_itemA, @_mainLot, @inputA, @_mainLot, CURDATE(), 5, 'DECIDED', 'SHIP')
            """, new { _itemA, _mainLot, inputA });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    private async Task<JsonElement> ShipmentAsync(long id) => await _client.GetFromJsonAsync<JsonElement>($"/api/shipments/{id}");
    private async Task<int> VersionAsync(long id) => (await ShipmentAsync(id)).GetProperty("shipment").GetProperty("rowVersion").GetInt32();

    private async Task<Dictionary<(long, long?), decimal>> StockAsync()
    {
        var rows = await _client.GetFromJsonAsync<JsonElement>($"/api/shipments/stock?customerId={_customer}");
        return rows.EnumerateArray().ToDictionary(
            r => (r.GetProperty("salesOrderItemId").GetInt64(), r.GetProperty("mainWorkId").ValueKind == JsonValueKind.Null ? (long?)null : r.GetProperty("mainWorkId").GetInt64()),
            r => r.GetProperty("availableQty").GetDecimal());
    }

    private object Body(DateOnly date, params object[] items) => new { shipmentDate = date, customerId = _customer, printSumByPart = false, items };

    [Fact]
    public async Task Shipment_stock_amounts_and_closing_flow()
    {
        var stock = await StockAsync();
        Assert.Equal(90m, stock[(_itemA, _mainLot)]);   // 100 − 부적합 10 (특채 5 는 출하 가능)
        Assert.Equal(50m, stock[(_itemB, _mainLot)]);
        Assert.Equal(20m, stock[(_itemC, null)]);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var s1 = await Ok(await _client.PostAsJsonAsync("/api/shipments", Body(today,
            new { salesOrderItemId = _itemA, mainWorkId = _mainLot, shipmentQty = 60, testSpecimenQty = 2 },
            new { salesOrderItemId = _itemB, mainWorkId = _mainLot, shipmentQty = 20, testSpecimenQty = 0 },
            new { salesOrderItemId = _itemC, shipmentQty = 5, testSpecimenQty = 0 })));
        var s1Id = s1.GetProperty("shipmentId").GetInt64();
        Assert.StartsWith($"O{today:yyMMdd}-", s1.GetProperty("shipmentNo").GetString());
        // 60 × 1000 + (20 × 2kg) × 500 + 5 × 300 = 81,500, 세액 10%
        Assert.Equal((81500m, 8150m, 89650m), (s1.GetProperty("supplyAmount").GetDecimal(), s1.GetProperty("vatAmount").GetDecimal(), s1.GetProperty("totalAmount").GetDecimal()));
        var detail = await ShipmentAsync(s1Id);
        Assert.Equal(("HD-77", "123-45-67890"), (detail.GetProperty("items")[0].GetProperty("submitLotNo").GetString(),
            await Scalar<string>("SELECT customer_business_no_snapshot FROM shipment WHERE shipment_id = @s1Id", new { s1Id })));
        Assert.Equal(28m, (await StockAsync())[(_itemA, _mainLot)]);   // 90 − 60 − 시험편 2

        Assert.Equal("SHIPMENT_EXCEEDS_STOCK", await Code(await _client.PostAsJsonAsync("/api/shipments", Body(today,
            new { salesOrderItemId = _itemA, mainWorkId = _mainLot, shipmentQty = 29, testSpecimenQty = 0 }))));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/shipments", new
        {
            shipmentDate = today, customerId = _other, items = new[] { new { salesOrderItemId = _itemA, mainWorkId = _mainLot, shipmentQty = 1, testSpecimenQty = 0 } },
        })).StatusCode);   // 다른 거래처의 수주

        // 수정: 자기 전표 수량은 빼고 다시 계산 (60 → 88 가능)
        var updated = await Ok(await _client.PutAsJsonAsync($"/api/shipments/{s1Id}", new
        {
            rowVersion = await VersionAsync(s1Id), shipmentDate = today, customerId = _customer, printSumByPart = true,
            items = new[] { new { salesOrderItemId = _itemA, mainWorkId = (long?)_mainLot, shipmentQty = 88m, testSpecimenQty = 2m } },
        }));
        Assert.Equal(88000m, updated.GetProperty("supplyAmount").GetDecimal());

        var s2Id = (await Ok(await _client.PostAsJsonAsync("/api/shipments", Body(today, new { salesOrderItemId = _itemC, shipmentQty = 3, testSpecimenQty = 0 }))))
            .GetProperty("shipmentId").GetInt64();

        // 마감: s1 마감, 나머지(s2) 다음 달로 이월
        var summary = await _client.GetFromJsonAsync<JsonElement>($"/api/closings/customers?year={today.Year}&month={today.Month}");
        var mine = summary.EnumerateArray().Single(x => x.GetProperty("customerId").GetInt64() == _customer);
        Assert.Equal((2L, 25), (mine.GetProperty("openCount").GetInt64(), mine.GetProperty("closingDate").GetDateTime().Day));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/closings", new
        {
            customerId = _other, year = today.Year, month = today.Month, shipmentIds = new[] { s1Id }, carryOverOthers = false,
        })).StatusCode);   // 전표·마감 업체 불일치
        var closing = await Ok(await _client.PostAsJsonAsync("/api/closings", new
        {
            customerId = _customer, year = today.Year, month = today.Month, shipmentIds = new[] { s1Id }, carryOverOthers = true,
        }));
        Assert.Equal((1, 96800m), (closing.GetProperty("carried").GetInt32(), closing.GetProperty("totalAmount").GetDecimal()));
        var next = today.AddMonths(1);
        Assert.Equal(("CLOSED", "CARRIED_OVER", next.Month),
            ((await ShipmentAsync(s1Id)).GetProperty("shipment").GetProperty("closingStatus").GetString(),
             (await ShipmentAsync(s2Id)).GetProperty("shipment").GetProperty("closingStatus").GetString(),
             (await ShipmentAsync(s2Id)).GetProperty("shipment").GetProperty("closingMonth").GetInt32()));

        // 마감된 전표는 수정·취소 불가 → 마감 취소 후 가능
        Assert.Equal("SHIPMENT_CLOSED", await Code(await _client.PostAsJsonAsync($"/api/shipments/{s1Id}/cancel", new { rowVersion = await VersionAsync(s1Id) })));
        var closingId = closing.GetProperty("shipmentClosingId").GetInt64();
        var closingVersion = (await _client.GetFromJsonAsync<JsonElement>($"/api/closings/{closingId}")).GetProperty("closing").GetProperty("rowVersion").GetInt32();
        await Ok(await _client.PostAsJsonAsync($"/api/closings/{closingId}/reopen", new { rowVersion = closingVersion, reason = "금액 정정" }));
        Assert.Equal("UNCLOSED", (await ShipmentAsync(s1Id)).GetProperty("shipment").GetProperty("closingStatus").GetString());
        await Ok(await _client.PostAsJsonAsync($"/api/shipments/{s1Id}/cancel", new { rowVersion = await VersionAsync(s1Id), reason = "반품" }));
        Assert.Equal(90m, (await StockAsync())[(_itemA, _mainLot)]);   // 취소 → 재고 복귀
    }

    private async Task<T> Scalar<T>(string sql, object args)
    {
        await using var c = await fx.OpenAsync();
        return (await c.ExecuteScalarAsync<T>(sql, args))!;
    }
}
