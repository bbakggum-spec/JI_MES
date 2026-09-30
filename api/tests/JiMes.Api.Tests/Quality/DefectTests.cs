using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace JiMes.Api.Tests.Quality;

/// <summary>부적합 — 작업 화면 불량 등록 → 판정 → 재작업 LOT → 재작업 완료 시 자동 완료 (설계 §23.6)</summary>
[Collection(ApiCollection.Name)]
public sealed class DefectTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private long _gas, _gas2, _carb, _item, _employee;
    private string _itemNo = "";

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var c = await fx.OpenAsync();
        async Task<long> Insert(string sql, object? args = null) => await c.ExecuteScalarAsync<long>(sql + "; SELECT LAST_INSERT_ID();", args);
        var type = await Insert("INSERT INTO equipment_type (equipment_type_code, equipment_type_name) VALUES (@code, '가스로')", new { code = $"G-{tag}" });
        _gas = await Insert("INSERT INTO equipment (equipment_type_id, equipment_code, equipment_initial, equipment_name) VALUES (@type, @code, @ini, '가스로')", new { type, code = $"G1-{tag}", ini = $"D{tag[..3]}" });
        _gas2 = await Insert("INSERT INTO equipment (equipment_type_id, equipment_code, equipment_initial, equipment_name) VALUES (@type, @code, @ini, '가스로2')", new { type, code = $"G2-{tag}", ini = $"E{tag[..3]}" });
        _carb = await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@code, '침탄')", new { code = $"C-{tag}" });
        var hp = await Insert("INSERT INTO heat_process (heat_process_code, heat_process_name) VALUES (@code, '침탄')", new { code = $"HP-{tag}" });
        var hpv = await Insert("INSERT INTO heat_process_version (heat_process_id, version_no, effective_from, is_current) VALUES (@hp, 1, '2026-01-01', 1)", new { hp });
        await c.ExecuteAsync("INSERT INTO heat_process_operation (heat_process_version_id, sequence_no, unit_process_id, is_main_process) VALUES (@hpv, 10, @_carb, 1)", new { hpv, _carb });
        var customer = await Insert("INSERT INTO customer (customer_code, customer_name) VALUES (@code, '고객')", new { code = $"CU-{tag}" });
        var part = await Insert("INSERT INTO part (part_code, part_name) VALUES (@code, '기어')", new { code = $"P-{tag}" });
        var order = await Insert("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@no, CURDATE(), @customer)", new { no = $"SO-{tag}", customer });
        _itemNo = $"I-{tag}";
        _item = await Insert(
            "INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, heat_process_version_id, order_qty, part_name_snapshot) VALUES (@no, @order, 1, @part, @hpv, 100, '기어')",
            new { no = _itemNo, order, part, hpv });
        _employee = await Insert("INSERT INTO employee (employee_code, employee_name) VALUES (@code, '박품질')", new { code = $"E-{tag}" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    private async Task<JsonElement> WorkAsync(long id) => await _client.GetFromJsonAsync<JsonElement>($"/api/works/{id}");
    private async Task<int> WorkVersion(long id) => (await WorkAsync(id)).GetProperty("work").GetProperty("rowVersion").GetInt32();
    private async Task<JsonElement> DefectAsync(long id) => await _client.GetFromJsonAsync<JsonElement>($"/api/defects/{id}");

    [Fact]
    public async Task Work_defect_decide_rework_and_auto_complete()
    {
        // 침탄 LOT: 100 투입 → 시작
        var lot = await Ok(await _client.PostAsJsonAsync("/api/works", new { equipmentId = _gas, unitProcessId = _carb }));
        var workId = lot.GetProperty("productionWorkId").GetInt64();
        await Ok(await _client.PostAsJsonAsync($"/api/works/{workId}/inputs", new { rowVersion = await WorkVersion(workId), salesOrderItemId = _item, inputQty = 100 }));
        var inputId = (await WorkAsync(workId)).GetProperty("inputs")[0].GetProperty("productionWorkInputId").GetInt64();
        Assert.Equal("WORK_NOT_STARTED", await Code(await _client.PostAsJsonAsync($"/api/works/{workId}/inputs/{inputId}/defects", new { rowVersion = await WorkVersion(workId), defectQty = 1 })));
        await Ok(await _client.PostAsJsonAsync($"/api/works/{workId}/start", new { rowVersion = await WorkVersion(workId) }));

        // 불량 30 → 양품 70, 초과 등록 불가
        var d1 = (await Ok(await _client.PostAsJsonAsync($"/api/works/{workId}/inputs/{inputId}/defects", new { rowVersion = await WorkVersion(workId), defectQty = 30, remark = "크랙" })))
            .GetProperty("defectOccurrenceId").GetInt64();
        var d2 = (await Ok(await _client.PostAsJsonAsync($"/api/works/{workId}/inputs/{inputId}/defects", new { rowVersion = await WorkVersion(workId), defectQty = 10 })))
            .GetProperty("defectOccurrenceId").GetInt64();
        Assert.Equal("DEFECT_EXCEEDS_GOOD", await Code(await _client.PostAsJsonAsync($"/api/works/{workId}/inputs/{inputId}/defects", new { rowVersion = await WorkVersion(workId), defectQty = 61 })));
        Assert.Equal(60m, (await WorkAsync(workId)).GetProperty("inputs")[0].GetProperty("goodQty").GetDecimal());
        await Ok(await _client.PostAsJsonAsync($"/api/works/{workId}/complete", new { rowVersion = await WorkVersion(workId) }));

        var open = await _client.GetFromJsonAsync<JsonElement>($"/api/defects?openOnly=true&search={_itemNo}");
        Assert.Equal(2, open.GetArrayLength());

        // 판정: 재처리 → 재작업 LOT (다른 설비), 재작업 LOT 완료 → 부적합 완료
        var defect = await DefectAsync(d1);
        Assert.Equal("OPEN", defect.GetProperty("status").GetString());
        Assert.Equal("DEFECT_NOT_REWORK", await Code(await _client.PostAsJsonAsync($"/api/defects/{d1}/rework", new { rowVersion = defect.GetProperty("rowVersion").GetInt32(), equipmentId = _gas2, unitProcessId = _carb })));
        await Ok(await _client.PutAsJsonAsync($"/api/defects/{d1}/decide", new { rowVersion = defect.GetProperty("rowVersion").GetInt32(), decision = "REWORK", employeeId = _employee, remark = "재침탄" }));
        defect = await DefectAsync(d1);
        var rework = await Ok(await _client.PostAsJsonAsync($"/api/defects/{d1}/rework", new { rowVersion = defect.GetProperty("rowVersion").GetInt32(), equipmentId = _gas2, unitProcessId = _carb }));
        var reworkId = rework.GetProperty("productionWorkId").GetInt64();
        defect = await DefectAsync(d1);
        Assert.Equal(("REWORKING", rework.GetProperty("lotNo").GetString()), (defect.GetProperty("status").GetString(), defect.GetProperty("reworkLotNo").GetString()));

        var reworkDetail = await WorkAsync(reworkId);
        Assert.True(reworkDetail.GetProperty("work").GetProperty("isRework").GetBoolean());
        var reworkInput = reworkDetail.GetProperty("inputs")[0];
        Assert.Equal((30m, reworkId), (reworkInput.GetProperty("inputQty").GetDecimal(), reworkInput.GetProperty("mainWorkId").GetInt64()));   // 주공정 재작업 → 주 LOT = 자신
        await using (var c = await fx.OpenAsync())
            Assert.Equal(workId, await c.ExecuteScalarAsync<long>("SELECT CAST(origin_work_id AS SIGNED) FROM production_work_input WHERE production_work_id = @reworkId", new { reworkId }));

        // 재작업은 정상 투입 잔량에 영향 없음 (수주 100 중 이미 100 투입)
        await Ok(await _client.PostAsJsonAsync($"/api/works/{reworkId}/start", new { rowVersion = await WorkVersion(reworkId) }));
        await Ok(await _client.PostAsJsonAsync($"/api/works/{reworkId}/complete", new { rowVersion = await WorkVersion(reworkId) }));
        Assert.Equal("COMPLETED", (await DefectAsync(d1)).GetProperty("status").GetString());

        // 다른 처리: 출하(특채) 판정 후 수동 완료 / 미결정 취소
        var other = await DefectAsync(d2);
        Assert.Equal("DEFECT_NOT_DECIDED", await Code(await _client.PostAsJsonAsync($"/api/defects/{d2}/complete", new { rowVersion = other.GetProperty("rowVersion").GetInt32(), employeeId = _employee })));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync($"/api/defects/{d2}/decide", new { rowVersion = other.GetProperty("rowVersion").GetInt32(), decision = "NOPE", employeeId = _employee })).StatusCode);
        await Ok(await _client.PostAsJsonAsync($"/api/defects/{d2}/cancel", new { rowVersion = other.GetProperty("rowVersion").GetInt32(), reason = "오등록" }));
        Assert.Equal("CANCELLED", (await DefectAsync(d2)).GetProperty("status").GetString());
        Assert.Equal(70m, (await WorkAsync(workId)).GetProperty("inputs")[0].GetProperty("goodQty").GetDecimal());   // 취소분은 양품 복귀
    }
}
