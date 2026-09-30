using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace JiMes.Api.Tests.Production;

/// <summary>작업(투입) — 세척(주공정 전) → 침탄(주공정) → 템퍼링(주공정 후) 흐름 (설계 §3.2·§3.3·§23.4)</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private long _washer, _gas, _gas2, _tempering, _wash, _carb, _temper, _item, _item2, _tempCi, _cpCi;
    private string _itemNo = "", _item2No = "";
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        var tag = Tag();
        await using var c = await fx.OpenAsync();
        async Task<long> Insert(string sql, object? args = null) => await c.ExecuteScalarAsync<long>(sql + "; SELECT LAST_INSERT_ID();", args);
        async Task<long> Type(string name) => await Insert("INSERT INTO equipment_type (equipment_type_code, equipment_type_name) VALUES (@code, @name)", new { code = $"{name}-{tag}", name });
        async Task<long> Equip(long type, string initial) =>
            await Insert("INSERT INTO equipment (equipment_type_id, equipment_code, equipment_initial, equipment_name) VALUES (@type, @code, @initial, @initial)",
                new { type, code = $"{initial}-{tag}", initial = $"{initial}{tag[..3]}" });
        async Task<long> Unit(string name) => await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@code, @name)", new { code = $"{name}-{tag}", name });

        var gasType = await Type("GAS");
        _washer = await Equip(await Type("WSH"), "W");
        _gas = await Equip(gasType, "G");
        _gas2 = await Equip(gasType, "H");
        _tempering = await Equip(await Type("TMP"), "T");
        _wash = await Unit("세척");
        _carb = await Unit("침탄");
        _temper = await Unit("템퍼링");

        var hp = await Insert("INSERT INTO heat_process (heat_process_code, heat_process_name) VALUES (@code, '침탄')", new { code = $"HP-{tag}" });
        var hpv = await Insert("INSERT INTO heat_process_version (heat_process_id, version_no, effective_from, is_current) VALUES (@hp, 1, '2026-01-01', 1)", new { hp });
        await c.ExecuteAsync(
            """
            INSERT INTO heat_process_operation (heat_process_version_id, sequence_no, unit_process_id, is_main_process)
            VALUES (@hpv, 10, @_wash, 0), (@hpv, 20, @_carb, 1), (@hpv, 30, @_temper, 0)
            """, new { hpv, _wash, _carb, _temper });

        var part = await Insert("INSERT INTO part (part_code, part_name, unit_weight) VALUES (@code, '기어', 0.5)", new { code = $"P-{tag}" });
        _tempCi = await Insert("INSERT INTO condition_item (condition_item_code, condition_item_name, unit_code) VALUES (@code, '온도', '℃')", new { code = $"T-{tag}" });
        _cpCi = await Insert("INSERT INTO condition_item (condition_item_code, condition_item_name, unit_code) VALUES (@code, 'CP', '%')", new { code = $"CP-{tag}" });
        var std = await Insert(
            "INSERT INTO standard (standard_code, standard_name, part_id, unit_process_id, equipment_type_id) VALUES (@code, '기어 침탄', @part, @_carb, @gasType)",
            new { code = $"STD-{tag}", part, _carb, gasType });
        var sv = await Insert("INSERT INTO standard_version (standard_id, version_no, charge_qty, running_time_min, effective_from, is_current) VALUES (@std, 1, 400, 480, '2026-01-01', 1)", new { std });
        await c.ExecuteAsync("INSERT INTO standard_version_step (standard_version_id, sequence_no, step_name) VALUES (@sv, 1, '침탄'), (@sv, 2, '확산')", new { sv });
        await c.ExecuteAsync("INSERT INTO standard_version_item (standard_version_id, sequence_no, condition_item_id) VALUES (@sv, 1, @_tempCi), (@sv, 2, @_cpCi)", new { sv, _tempCi, _cpCi });
        await c.ExecuteAsync(
            "INSERT INTO standard_condition (standard_version_id, step_no, condition_item_id, condition_value) VALUES (@sv, 1, @_tempCi, '920'), (@sv, 2, @_cpCi, '0.85')",
            new { sv, _tempCi, _cpCi });

        var customer = await Insert("INSERT INTO customer (customer_code, customer_name) VALUES (@code, '고객')", new { code = $"C-{tag}" });
        var order = await Insert("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@no, CURDATE(), @customer)", new { no = $"SO-{tag}", customer });
        _itemNo = $"I-{tag}-1";
        _item2No = $"I-{tag}-2";
        _item = await Insert(
            """
            INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, heat_process_version_id, order_qty, unit_weight, customer_lot, part_name_snapshot)
            VALUES (@no, @order, 1, @part, @hpv, 100, 0.5, 'CL-1', '기어')
            """, new { no = _itemNo, order, part, hpv });
        _item2 = await Insert(
            "INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, heat_process_version_id, order_qty) VALUES (@no, @order, 2, @part, @hpv, 30)",
            new { no = _item2No, order, part, hpv });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<(long Id, string LotNo)> NewWorkAsync(long equipmentId, long unitProcessId)
    {
        var r = await Ok(await _client.PostAsJsonAsync("/api/works", new { equipmentId, unitProcessId }));
        return (r.GetProperty("productionWorkId").GetInt64(), r.GetProperty("lotNo").GetString()!);
    }

    private async Task<JsonElement> DetailAsync(long id) => await _client.GetFromJsonAsync<JsonElement>($"/api/works/{id}");
    private async Task<int> VersionAsync(long id) => (await DetailAsync(id)).GetProperty("work").GetProperty("rowVersion").GetInt32();

    private async Task<JsonElement> ScanAsync(long id, string code) => await Ok(await _client.PostAsJsonAsync($"/api/works/{id}/scan", new { code }));

    private async Task<HttpResponseMessage> AddAsync(long id, long itemId, decimal qty, long? mainInputId = null) =>
        await _client.PostAsJsonAsync($"/api/works/{id}/inputs", new { rowVersion = await VersionAsync(id), salesOrderItemId = itemId, mainInputId, inputQty = qty });

    [Fact]
    public async Task Pre_main_post_process_inputs_follow_remaining_quantities()
    {
        // ① 세척 (주공정 전): 입고번호 스캔 → 잔량 100
        var (wash, _) = await NewWorkAsync(_washer, _wash);
        var scan = await ScanAsync(wash, _itemNo);
        var cand = scan.GetProperty("candidates")[0];
        Assert.Equal(("ORDER_ITEM", "PRE", 100m), (scan.GetProperty("kind").GetString(), cand.GetProperty("phase").GetString(), cand.GetProperty("remainingQty").GetDecimal()));
        await Ok(await AddAsync(wash, _item, 60));
        Assert.Equal("DUPLICATE_INPUT", await Code(await AddAsync(wash, _item, 10)));
        var (wash2, _) = await NewWorkAsync(_washer, _wash);
        Assert.Equal(40m, (await ScanAsync(wash2, _itemNo)).GetProperty("candidates")[0].GetProperty("remainingQty").GetDecimal());
        Assert.Equal("INPUT_EXCEEDS_REMAINING", await Code(await AddAsync(wash2, _item, 41)));

        // ② 침탄 (주공정): 주 LOT = 자신, 표준 확정 → 조건 복사
        var (carb, carbLot) = await NewWorkAsync(_gas, _carb);
        await Ok(await AddAsync(carb, _item, 100));
        var detail = await DetailAsync(carb);
        var mainInput = detail.GetProperty("inputs")[0];
        Assert.Equal(carb, mainInput.GetProperty("mainWorkId").GetInt64());
        Assert.True(detail.GetProperty("work").GetProperty("isMainProcess").GetBoolean());

        var standards = await _client.GetFromJsonAsync<JsonElement>($"/api/works/{carb}/standards");
        var std = standards[0];
        await Ok(await _client.PostAsJsonAsync($"/api/works/{carb}/fix-standard", new
        {
            rowVersion = await VersionAsync(carb), productionWorkInputId = mainInput.GetProperty("productionWorkInputId").GetInt64(),
            standardVersionId = std.GetProperty("standardVersionId").GetInt64(),
        }));
        detail = await DetailAsync(carb);
        Assert.Equal((true, 480m), (detail.GetProperty("work").GetProperty("isStandardFixed").GetBoolean(), detail.GetProperty("work").GetProperty("expectedDurationMin").GetDecimal()));
        var conditions = detail.GetProperty("conditions").EnumerateArray().ToList();
        Assert.Equal(6, conditions.Count);   // 항목 2 × (공통 + 스텝 2)
        Assert.Contains(conditions, x => x.GetProperty("stepNameSnapshot").GetString() == "침탄" && x.GetProperty("setValue").GetString() == "920");

        // 조건 수정 (스텝 추가, 값 변경)
        await Ok(await _client.PutAsJsonAsync($"/api/works/{carb}/conditions", new
        {
            rowVersion = await VersionAsync(carb), steps = new[] { "침탄", "확산", "강온" }, items = new[] { _tempCi, _cpCi },
            conditions = new object[] { new { stepNo = 1, conditionItemId = _tempCi, conditionValue = "925" }, new { stepNo = 3, conditionItemId = _tempCi, conditionValue = "850" } },
        }));
        Assert.Equal(8, (await DetailAsync(carb)).GetProperty("conditions").GetArrayLength());

        // 시작 → 같은 설비에 두 번째 LOT 시작 불가 → 완료
        await Ok(await _client.PostAsJsonAsync($"/api/works/{carb}/start", new { rowVersion = await VersionAsync(carb) }));
        var (carbB, _) = await NewWorkAsync(_gas, _carb);
        await Ok(await AddAsync(carbB, _item2, 30));
        Assert.Equal("EQUIPMENT_BUSY", await Code(await _client.PostAsJsonAsync($"/api/works/{carbB}/start", new { rowVersion = await VersionAsync(carbB) })));
        await Ok(await _client.PostAsJsonAsync($"/api/works/{carb}/complete", new { rowVersion = await VersionAsync(carb) }));
        detail = await DetailAsync(carb);
        Assert.Equal("COMPLETED", detail.GetProperty("work").GetProperty("status").GetString());
        // 완료시각 = 지금을 5분(설정) 내림, 단 시작보다 앞이면 시작시각
        var end = detail.GetProperty("work").GetProperty("actualEndAt").GetDateTime();
        Assert.True(end.Minute % 5 == 0 || end == detail.GetProperty("work").GetProperty("actualStartAt").GetDateTime(), end.ToString("O"));
        Assert.Equal("WORK_NOT_EDITABLE", await Code(await AddAsync(carb, _item2, 1)));

        // ③ 템퍼링 (주공정 후): 입고번호 → 주 LOT 후보, 주 LOT번호 → 그 LOT 투입 행
        var (temper, _) = await NewWorkAsync(_tempering, _temper);
        var byItem = await ScanAsync(temper, _itemNo);
        var post = byItem.GetProperty("candidates")[0];
        Assert.Equal(("POST", carbLot, 100m), (post.GetProperty("phase").GetString(), post.GetProperty("mainLotNo").GetString(), post.GetProperty("remainingQty").GetDecimal()));
        var byLot = await ScanAsync(temper, carbLot);
        Assert.Equal("MAIN_LOT", byLot.GetProperty("kind").GetString());
        var mainInputId = byLot.GetProperty("candidates")[0].GetProperty("mainInputId").GetInt64();
        await Ok(await AddAsync(temper, _item, 70, mainInputId));
        var (temper2, _) = await NewWorkAsync(_tempering, _temper);
        Assert.Equal("INPUT_EXCEEDS_REMAINING", await Code(await AddAsync(temper2, _item, 31, mainInputId)));
        await Ok(await AddAsync(temper2, _item, 30, mainInputId));

        // 세척 LOT 에 주공정 후 투입을 섞을 수 없음 / 주공정 LOT 이 아닌 번호 스캔
        Assert.Equal("NOT_POST_PROCESS", await Code(await AddAsync(wash2, _item, 1, mainInputId)));

        // 세척 LOT 투입 행 삭제 (참조 없음) → 잔량 복귀
        var washInput = (await DetailAsync(wash)).GetProperty("inputs")[0].GetProperty("productionWorkInputId").GetInt64();
        var del = await _client.DeleteAsync($"/api/works/{wash}/inputs/{washInput}?rowVersion={await VersionAsync(wash)}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(100m, (await ScanAsync(wash2, _itemNo)).GetProperty("candidates")[0].GetProperty("remainingQty").GetDecimal());
    }

    [Fact]
    public async Task Scan_rejects_unknown_codes_and_items_outside_route()
    {
        var (wash, _) = await NewWorkAsync(_washer, _wash);
        var unknown = await _client.PostAsJsonAsync($"/api/works/{wash}/scan", new { code = "NO-SUCH" });
        Assert.Equal("SCAN_NOT_FOUND", await Code(unknown));

        await using (var c = await fx.OpenAsync())
            await c.ExecuteAsync("UPDATE sales_order_item SET heat_process_version_id = NULL WHERE sales_order_item_id = @_item2", new { _item2 });
        Assert.Equal("ROUTE_MISSING", await Code(await _client.PostAsJsonAsync($"/api/works/{wash}/scan", new { code = _item2No })));

        var stale = await _client.PostAsJsonAsync($"/api/works/{wash}/inputs", new { rowVersion = 999, salesOrderItemId = _item, inputQty = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
