using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace JiMes.Api.Tests.Reports;

/// <summary>LOT 현황·추적 — 세척(전공정) → 침탄(주) → 템퍼링(후공정) + 재작업 + 출하 를 주 LOT 하나로 모은다 (설계 §3.4·§24.1)</summary>
[Collection(ApiCollection.Name)]
public sealed class LotTraceTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private long _washer, _gas, _gas2, _tempering, _wash, _carb, _temper, _item, _employee;
    private string _itemNo = "", _tag = "";

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        _tag = Guid.NewGuid().ToString("N")[..8];
        var tag = _tag;
        await using var c = await fx.OpenAsync();
        async Task<long> Insert(string sql, object? args = null) => await c.ExecuteScalarAsync<long>(sql + "; SELECT LAST_INSERT_ID();", args);
        async Task<long> Type(string n) => await Insert("INSERT INTO equipment_type (equipment_type_code, equipment_type_name) VALUES (@code, @n)", new { code = $"{n}-{tag}", n });
        async Task<long> Equip(long type, string ini) => await Insert(
            "INSERT INTO equipment (equipment_type_id, equipment_code, equipment_initial, equipment_name) VALUES (@type, @code, @ini, @ini)", new { type, code = $"{ini}-{tag}", ini = $"{ini}{tag[..3]}" });
        async Task<long> Unit(string n) => await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@code, @n)", new { code = $"{n}-{tag}", n });
        var gasType = await Type("G");
        _washer = await Equip(await Type("W"), "W");
        _gas = await Equip(gasType, "G");
        _gas2 = await Equip(gasType, "H");
        _tempering = await Equip(await Type("T"), "T");
        _wash = await Unit("세척");
        _carb = await Unit("침탄");
        _temper = await Unit("템퍼링");
        var hp = await Insert("INSERT INTO heat_process (heat_process_code, heat_process_name) VALUES (@code, '침탄')", new { code = $"HP-{tag}" });
        var hpv = await Insert("INSERT INTO heat_process_version (heat_process_id, version_no, effective_from, is_current) VALUES (@hp, 1, '2026-01-01', 1)", new { hp });
        await c.ExecuteAsync("INSERT INTO heat_process_operation (heat_process_version_id, sequence_no, unit_process_id, is_main_process) VALUES (@hpv, 10, @_wash, 0), (@hpv, 20, @_carb, 1), (@hpv, 30, @_temper, 0)",
            new { hpv, _wash, _carb, _temper });
        var customer = await Insert("INSERT INTO customer (customer_code, customer_name) VALUES (@code, '고객')", new { code = $"C-{tag}" });
        var part = await Insert("INSERT INTO part (part_code, part_name) VALUES (@code, '기어')", new { code = $"P-{tag}" });
        var order = await Insert("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@no, CURDATE(), @customer)", new { no = $"SO-{tag}", customer });
        _itemNo = $"I-{tag}";
        _item = await Insert(
            "INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, heat_process_version_id, order_qty, unit_price, part_name_snapshot) VALUES (@no, @order, 1, @part, @hpv, 100, 10, '기어')",
            new { no = _itemNo, order, part, hpv });
        _employee = await Insert("INSERT INTO employee (employee_code, employee_name) VALUES (@code, '이추적')", new { code = $"E-{tag}" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<int> Version(long id) => (await _client.GetFromJsonAsync<JsonElement>($"/api/works/{id}")).GetProperty("work").GetProperty("rowVersion").GetInt32();

    private async Task<(long Id, string Lot)> Lot(long equipmentId, long unitProcessId, decimal qty, long? mainInputId = null, bool start = true, bool complete = true)
    {
        var lot = await Ok(await _client.PostAsJsonAsync("/api/works", new { equipmentId, unitProcessId }));
        var id = lot.GetProperty("productionWorkId").GetInt64();
        await Ok(await _client.PostAsJsonAsync($"/api/works/{id}/inputs", new { rowVersion = await Version(id), salesOrderItemId = _item, mainInputId, inputQty = qty }));
        if (start) await Ok(await _client.PostAsJsonAsync($"/api/works/{id}/start", new { rowVersion = await Version(id) }));
        if (start && complete) await Ok(await _client.PostAsJsonAsync($"/api/works/{id}/complete", new { rowVersion = await Version(id) }));
        return (id, lot.GetProperty("lotNo").GetString()!);
    }

    [Fact]
    public async Task Trace_collects_pre_post_rework_defects_and_shipments_by_main_lot()
    {
        var (_, washLot) = await Lot(_washer, _wash, 100);
        var (carb, carbLot) = await Lot(_gas, _carb, 100, complete: false);
        var carbInput = (await _client.GetFromJsonAsync<JsonElement>($"/api/works/{carb}")).GetProperty("inputs")[0].GetProperty("productionWorkInputId").GetInt64();
        var defectId = (await Ok(await _client.PostAsJsonAsync($"/api/works/{carb}/inputs/{carbInput}/defects", new { rowVersion = await Version(carb), defectQty = 10 })))
            .GetProperty("defectOccurrenceId").GetInt64();
        await Ok(await _client.PostAsJsonAsync($"/api/works/{carb}/complete", new { rowVersion = await Version(carb) }));
        await Ok(await _client.PutAsJsonAsync($"/api/works/{carb}", new { rowVersion = await Version(carb), submitLotNo = $"SUB-{_tag}" }));
        var (_, temperLot) = await Lot(_tempering, _temper, 80, carbInput);

        var defect = await _client.GetFromJsonAsync<JsonElement>($"/api/defects/{defectId}");
        await Ok(await _client.PutAsJsonAsync($"/api/defects/{defectId}/decide", new { rowVersion = defect.GetProperty("rowVersion").GetInt32(), decision = "REWORK", employeeId = _employee }));
        defect = await _client.GetFromJsonAsync<JsonElement>($"/api/defects/{defectId}");
        var rework = await Ok(await _client.PostAsJsonAsync($"/api/defects/{defectId}/rework", new { rowVersion = defect.GetProperty("rowVersion").GetInt32(), equipmentId = _gas2, unitProcessId = _carb }));
        var reworkLot = rework.GetProperty("lotNo").GetString()!;

        await Ok(await _client.PostAsJsonAsync("/api/shipments", new
        {
            shipmentDate = DateOnly.FromDateTime(DateTime.Today), customerId = await CustomerOfItem(),
            items = new[] { new { salesOrderItemId = _item, mainWorkId = (long?)carb, shipmentQty = 50m, testSpecimenQty = 0m } },
        }));

        // 어떤 번호로 찾아도 주 LOT(침탄)로 모인다
        foreach (var code in new[] { carbLot, temperLot, washLot, reworkLot, _itemNo, $"SUB-{_tag}" })
        {
            var resolved = await _client.GetFromJsonAsync<JsonElement>($"/api/reports/trace/resolve?code={Uri.EscapeDataString(code)}");
            Assert.Contains(resolved.GetProperty("mains").EnumerateArray(), m => m.GetProperty("productionWorkId").GetInt64() == carb);
        }

        var trace = await _client.GetFromJsonAsync<JsonElement>($"/api/reports/trace/{carb}");
        Assert.Equal(carbLot, trace.GetProperty("main").GetProperty("lotNo").GetString());
        Assert.Equal([washLot], trace.GetProperty("pre").EnumerateArray().Select(x => x.GetProperty("lotNo").GetString()));
        Assert.Equal([temperLot], trace.GetProperty("post").EnumerateArray().Select(x => x.GetProperty("lotNo").GetString()));
        Assert.Equal([reworkLot], trace.GetProperty("rework").EnumerateArray().Select(x => x.GetProperty("lotNo").GetString()));
        Assert.Equal((1, 1), (trace.GetProperty("defects").GetArrayLength(), trace.GetProperty("shipments").GetArrayLength()));
        Assert.Equal(90m, trace.GetProperty("inputs")[0].GetProperty("goodQty").GetDecimal());

        // LOT 현황: 주 LOT 만 / 검색
        var lots = await _client.GetFromJsonAsync<JsonElement>($"/api/reports/lots?mainOnly=true&search={_itemNo}");
        var row = lots.EnumerateArray().Single(x => x.GetProperty("lotNo").GetString() == carbLot);
        Assert.Equal((1L, 50m), (row.GetProperty("postLotCount").GetInt64(), row.GetProperty("shipmentQty").GetDecimal()));
        Assert.DoesNotContain(lots.EnumerateArray(), x => x.GetProperty("lotNo").GetString() == temperLot);

        // 수주 진행: 경로 순 단위공정 투입·양품 (재작업 제외), 재고 = 주 LOT 양품(재작업 LOT 포함) − 출하
        var order = (await _client.GetFromJsonAsync<JsonElement>($"/api/reports/orders?search={_itemNo}"))[0];
        Assert.Equal(["세척:100:100", "침탄:100:90", "템퍼링:80:80"], order.GetProperty("processes").EnumerateArray()
            .Select(p => $"{p.GetProperty("unitProcessName").GetString()}:{p.GetProperty("inputQty").GetDecimal():0}:{p.GetProperty("goodQty").GetDecimal():0}"));
        Assert.Equal((100m, 50m, 50m, 0m), (order.GetProperty("mainInputQty").GetDecimal(), order.GetProperty("shipmentQty").GetDecimal(),
            order.GetProperty("stockQty").GetDecimal(), order.GetProperty("notInputQty").GetDecimal()));
        Assert.Equal(1, (await _client.GetFromJsonAsync<JsonElement>($"/api/reports/orders?search={_itemNo}&view=STOCK")).GetArrayLength());
        Assert.Equal(0, (await _client.GetFromJsonAsync<JsonElement>($"/api/reports/orders?search={_itemNo}&view=NOT_INPUT")).GetArrayLength());

        Assert.Equal("TRACE_NOT_FOUND", (await (await _client.GetAsync("/api/reports/trace/resolve?code=NOPE")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private async Task<long> CustomerOfItem()
    {
        await using var c = await fx.OpenAsync();
        return await c.ExecuteScalarAsync<long>(
            "SELECT CAST(so.customer_id AS SIGNED) FROM sales_order_item soi JOIN sales_order so ON so.sales_order_id = soi.sales_order_id WHERE soi.sales_order_item_id = @_item", new { _item });
    }
}
