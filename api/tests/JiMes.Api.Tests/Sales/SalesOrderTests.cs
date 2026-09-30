using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace JiMes.Api.Tests.Sales;

[Collection(ApiCollection.Name)]
public sealed class SalesOrderTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private long _customer, _gear, _shaft, _carb, _nitride;
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>입고번호는 입고일별 순번 — 다른 테스트와 겹치지 않는 날짜</summary>
    private static DateOnly UniqueDate() => new DateOnly(2031, 1, 1).AddDays(Random.Shared.Next(0, 3000));

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        await using var c = await fx.OpenAsync();
        async Task<long> Insert(string sql, object args) => await c.ExecuteScalarAsync<long>(sql + "; SELECT LAST_INSERT_ID();", args);
        _customer = await Insert("INSERT INTO customer (customer_code, customer_name) VALUES (@c, '한독기어')", new { c = $"C-{Tag()}" });
        _gear = await Insert(
            """
            INSERT INTO part (part_code, part_name, part_number, material, unit_weight, unit_price, price_basis, hardness, effective_hardening_depth)
            VALUES (@c, '헬리컬 기어', 'HG-100', 'SCM420H', 0.5, 1200, 'EA', 'HRC 58~62', '0.8~1.2')
            """, new { c = $"P-{Tag()}" });
        _shaft = await Insert("INSERT INTO part (part_code, part_name, unit_price) VALUES (@c, '샤프트', 300)", new { c = $"P-{Tag()}" });
        await c.ExecuteAsync("INSERT INTO part_customer (part_id, customer_id, customer_part_code) VALUES (@_gear, @_customer, 'HD-G1')", new { _gear, _customer });
        async Task<long> Route(string name)
        {
            var h = await Insert("INSERT INTO heat_process (heat_process_code, heat_process_name) VALUES (@c, @name)", new { c = $"H-{Tag()}", name });
            await c.ExecuteAsync("INSERT INTO heat_process_version (heat_process_id, version_no, effective_from, is_current) VALUES (@h, 1, '2026-01-01', 1)", new { h });
            return h;
        }
        _carb = await Route("침탄");
        _nitride = await Route("질화");
        await c.ExecuteAsync("INSERT INTO part_heat_process (part_id, heat_process_id, is_default) VALUES (@_gear, @_carb, 1), (@_gear, @_nitride, 0)", new { _gear, _carb, _nitride });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private Task<HttpResponseMessage> CreateAsync(DateOnly date, params object[] items) =>
        _client.PostAsJsonAsync("/api/sales-orders", new { orderDate = date, customerId = _customer, isReturn = false, items });

    [Fact]
    public async Task Create_assigns_item_numbers_and_snapshots_part_spec()
    {
        var date = UniqueDate();
        var res = await Ok(await CreateAsync(date,
            new { partId = _gear, orderQty = 400, customerLot = "CL-77", priority = 3, isSeparatelyManaged = true },   // 공정 = 품목 기본 (침탄)
            new { partId = _gear, heatProcessId = _nitride, orderQty = 10, unitPrice = 1500, isSeparatelyManaged = false },
            new { partId = _shaft, orderQty = 5, isSeparatelyManaged = false }));                                     // 공정 없음 (거래처 품목 아님도 허용)
        var items = res.GetProperty("items").EnumerateArray().ToList();
        var yymmdd = date.ToString("yyMMdd");
        Assert.Equal([$"I{yymmdd}-001", $"I{yymmdd}-002", $"I{yymmdd}-003"], items.Select(i => i.GetProperty("orderItemNo").GetString()));
        Assert.StartsWith($"SO{yymmdd}-", res.GetProperty("salesOrderNo").GetString());

        var first = await _client.GetFromJsonAsync<JsonElement>($"/api/sales-orders/items/{items[0].GetProperty("salesOrderItemId").GetInt64()}");
        Assert.Equal(("헬리컬 기어", "HG-100", "SCM420H", "침탄"), (first.GetProperty("partName").GetString(), first.GetProperty("partNumber").GetString(),
            first.GetProperty("material").GetString(), first.GetProperty("heatProcessName").GetString()));
        Assert.Equal(("HRC 58~62", "0.8~1.2", 200m, 1200m), (first.GetProperty("hardness").GetString(), first.GetProperty("caseDepth").GetString(),
            first.GetProperty("orderWeight").GetDecimal(), first.GetProperty("unitPrice").GetDecimal()));
        Assert.True(first.GetProperty("isSeparatelyManaged").GetBoolean());

        var second = await _client.GetFromJsonAsync<JsonElement>($"/api/sales-orders/items/{items[1].GetProperty("salesOrderItemId").GetInt64()}");
        Assert.Equal(("질화", 1500m), (second.GetProperty("heatProcessName").GetString(), second.GetProperty("unitPrice").GetDecimal()));

        // 목록 (기간 지정)
        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/sales-orders/items?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&customerId={_customer}");
        Assert.Equal(3, list.GetProperty("items").GetArrayLength());

        // 거래처 품목 후보: 기어(거래처 품목)만, 전체면 샤프트도
        var candidates = await _client.GetFromJsonAsync<JsonElement>($"/api/sales-orders/part-candidates?customerId={_customer}");
        Assert.Contains(candidates.EnumerateArray(), p => p.GetProperty("partId").GetInt64() == _gear && p.GetProperty("heatProcesses").GetArrayLength() == 2);
        Assert.DoesNotContain(candidates.EnumerateArray(), p => p.GetProperty("partId").GetInt64() == _shaft);
    }

    [Fact]
    public async Task Concurrent_registrations_get_distinct_numbers()
    {
        var date = UniqueDate();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CreateAsync(date, new { partId = _gear, orderQty = 1, isSeparatelyManaged = false })));
        var numbers = new List<string>();
        foreach (var res in results)
            numbers.Add((await Ok(res)).GetProperty("items")[0].GetProperty("orderItemNo").GetString()!);
        Assert.Equal(6, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Edit_and_cancel_respect_planning_and_input()
    {
        var date = UniqueDate();
        var res = await Ok(await CreateAsync(date,
            new { partId = _gear, orderQty = 20, isSeparatelyManaged = false },
            new { partId = _gear, orderQty = 30, isSeparatelyManaged = false }));
        var usedId = res.GetProperty("items")[0].GetProperty("salesOrderItemId").GetInt64();
        var freeId = res.GetProperty("items")[1].GetProperty("salesOrderItemId").GetInt64();

        async Task<JsonElement> Item(long id) => await _client.GetFromJsonAsync<JsonElement>($"/api/sales-orders/items/{id}");
        object Edit(JsonElement item, decimal qty, long? route = null) => new
        {
            rowVersion = item.GetProperty("rowVersion").GetUInt32(), heatProcessId = route ?? item.GetProperty("heatProcessId").GetInt64(), orderQty = qty,
            unitPrice = 1100, customerLot = "CL-1", priority = 2, isSeparatelyManaged = false, remark = "수정",
        };

        // 투입 7개가 있는 행
        await using (var c = await fx.OpenAsync())
        {
            var up = await c.ExecuteScalarAsync<long>("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@c, '침탄'); SELECT LAST_INSERT_ID();", new { c = $"U-{Tag()}" });
            var work = await c.ExecuteScalarAsync<long>("INSERT INTO production_work (lot_no, unit_process_id, work_date) VALUES (@l, @up, CURDATE()); SELECT LAST_INSERT_ID();", new { l = $"L-{Tag()}", up });
            await c.ExecuteAsync("INSERT INTO production_work_input (production_work_id, sales_order_item_id, input_qty) VALUES (@work, @usedId, 7)", new { work, usedId });
        }
        var used = await Item(usedId);
        Assert.Equal((7m, true), (used.GetProperty("usedQty").GetDecimal(), used.GetProperty("isScheduledOrInput").GetBoolean()));
        Assert.Equal("QTY_BELOW_USED", await Code(await _client.PutAsJsonAsync($"/api/sales-orders/items/{usedId}", Edit(used, 5))));
        Assert.Equal("ROUTE_IN_USE", await Code(await _client.PutAsJsonAsync($"/api/sales-orders/items/{usedId}", Edit(used, 20, _nitride))));
        Assert.Equal("ORDER_ITEM_IN_USE", await Code(await _client.PostAsJsonAsync($"/api/sales-orders/items/{usedId}/cancel", new { rowVersion = used.GetProperty("rowVersion").GetUInt32() })));

        var edited = await Ok(await _client.PutAsJsonAsync($"/api/sales-orders/items/{usedId}", Edit(used, 7)));
        Assert.Equal((7m, 2, 3.5m), (edited.GetProperty("orderQty").GetDecimal(), edited.GetProperty("priority").GetInt32(), edited.GetProperty("orderWeight").GetDecimal()));

        // 같은 묶음의 row_version 이 올라갔으므로 옛 값으로는 409
        var stale = await _client.PutAsJsonAsync($"/api/sales-orders/items/{freeId}", Edit(used, 10));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var free = await Item(freeId);
        await Ok(await _client.PutAsJsonAsync($"/api/sales-orders/items/{freeId}", Edit(free, 30, _nitride)));   // 계획·투입 없음 → 공정 변경 가능
        free = await Item(freeId);
        Assert.Equal("질화", free.GetProperty("heatProcessName").GetString());
        await Ok(await _client.PostAsJsonAsync($"/api/sales-orders/items/{freeId}/cancel", new { rowVersion = free.GetProperty("rowVersion").GetUInt32(), reason = "고객 취소" }));
        Assert.Equal("CANCELLED", (await Item(freeId)).GetProperty("status").GetString());

        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/sales-orders/items?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&customerId={_customer}");
        Assert.Equal(1, list.GetProperty("items").GetArrayLength());   // 취소 행은 기본 제외
    }

    [Fact]
    public async Task Invalid_lines_are_rejected()
    {
        var res = await CreateAsync(UniqueDate(), new { partId = _gear, orderQty = 0, isSeparatelyManaged = false }, new { partId = _gear, orderQty = 1, priority = 9, isSeparatelyManaged = false });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("items[0]", out _) && errors.TryGetProperty("items[1]", out _), errors.ToString());

        var empty = await CreateAsync(UniqueDate());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }
}
