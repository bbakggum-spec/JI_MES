using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using JiMes.Api.Features.Quality;

namespace JiMes.Api.Tests.Quality;

public sealed class InspectionJudgeTests
{
    [Theory]
    [InlineData("BETWEEN", 58.0, 62.0, new[] { "59.5", "60.1", "61" }, "PASS", "59.5~61")]
    [InlineData("BETWEEN", 58.0, 62.0, new[] { "57.2", "60" }, "FAIL", "57.2~60")]
    [InlineData("MIN", 0.8, null, new[] { "0.9", "" }, "PASS", "0.9")]
    [InlineData("MAX", null, 400.0, new[] { "401" }, "FAIL", "401")]
    public void Range_types_judge_numeric_samples(string range, double? lower, double? upper, string[] values, string decision, string result)
    {
        var j = InspectionJudge.Judge(range, (decimal?)lower, (decimal?)upper, values, null);
        Assert.Equal((decision, result), (j.Decision, j.Result));
    }

    [Fact]
    public void Record_only_and_text_values_use_manual_decision()
    {
        Assert.Equal("PASS", InspectionJudge.Judge("NONE", null, null, ["양호"], "PASS").Decision);
        Assert.Null(InspectionJudge.Judge(null, null, null, ["양호"], null).Decision);
        Assert.Equal("FAIL", InspectionJudge.Judge("BETWEEN", 1, 2, ["깨짐"], "FAIL").Decision);   // 숫자 없으면 수동
        Assert.Equal("FAIL", InspectionJudge.Overall(["PASS", null, "FAIL"]));
        Assert.Equal("PASS", InspectionJudge.Overall(["PASS", "NA"]));
        Assert.Null(InspectionJudge.Overall(["NA", null]));
    }
}

[Collection(ApiCollection.Name)]
public sealed class InspectionTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private long _work, _inputA, _inputB, _hardness, _appearance, _inspector;
    private string _lotNo = "";

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var c = await fx.OpenAsync();
        async Task<long> Insert(string sql, object? args = null) => await c.ExecuteScalarAsync<long>(sql + "; SELECT LAST_INSERT_ID();", args);
        var unit = await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@code, '침탄')", new { code = $"U-{tag}" });
        var customer = await Insert("INSERT INTO customer (customer_code, customer_name) VALUES (@code, '한독')", new { code = $"C-{tag}" });
        var part = await Insert("INSERT INTO part (part_code, part_name) VALUES (@code, '기어')", new { code = $"P-{tag}" });
        var order = await Insert("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@no, CURDATE(), @customer)", new { no = $"SO-{tag}", customer });
        async Task<long> Item(int line, decimal qty) => await Insert(
            "INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, order_qty, part_name_snapshot) VALUES (@no, @order, @line, @part, @qty, '기어')",
            new { no = $"I-{tag}-{line}", order, line, part, qty });
        var itemA = await Item(1, 400);
        var itemB = await Item(2, 100);
        _lotNo = $"L-{tag}";
        _work = await Insert("INSERT INTO production_work (lot_no, unit_process_id, work_date, is_main_process, status, submit_lot_no) VALUES (@_lotNo, @unit, CURDATE(), 1, 'COMPLETED', 'HD-01')", new { _lotNo, unit });
        async Task<long> Input(long item, decimal qty) => await Insert(
            "INSERT INTO production_work_input (production_work_id, sales_order_item_id, main_work_id, input_qty, part_name_snapshot) VALUES (@_work, @item, @_work, @qty, '기어')",
            new { _work, item, qty });
        _inputA = await Input(itemA, 400);
        _inputB = await Input(itemB, 100);
        var std = await Insert("INSERT INTO inspection_standard (part_id) VALUES (@part)", new { part });
        var version = await Insert("INSERT INTO inspection_standard_version (inspection_standard_id, version_no, effective_from, is_current) VALUES (@std, 1, '2026-01-01', 1)", new { std });
        _hardness = await Insert(
            "INSERT INTO inspection_criteria (inspection_standard_version_id, sequence_no, item_type, item_name, range_type, lower_limit, upper_limit, sample_count) VALUES (@version, 1, 'HARDNESS', '표면경도', 'BETWEEN', 58, 62, 3)",
            new { version });
        _appearance = await Insert(
            "INSERT INTO inspection_criteria (inspection_standard_version_id, sequence_no, item_type, item_name, range_type, sample_count) VALUES (@version, 2, 'APPEARANCE', '외관', 'NONE', 1)",
            new { version });
        _inspector = await Insert("INSERT INTO employee (employee_code, employee_name) VALUES (@code, '김검사')", new { code = $"E-{tag}" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> Code(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    private async Task<JsonElement> DetailAsync(long id) => await _client.GetFromJsonAsync<JsonElement>($"/api/inspections/{id}");
    private async Task<int> VersionAsync(long id) => (await DetailAsync(id)).GetProperty("inspection").GetProperty("rowVersion").GetInt32();

    private object[] Items(params string[] hardness) =>
    [
        new { inspectionCriteriaId = _hardness, itemName = "표면경도", values = hardness },
        new { inspectionCriteriaId = _appearance, itemName = "외관", decision = "PASS", values = new[] { "양호" } },
    ];

    [Fact]
    public async Task Save_judge_complete_with_defects_then_reinspect()
    {
        var lot = await _client.GetFromJsonAsync<JsonElement>($"/api/inspections/lot?lotNo={_lotNo}");
        var candidates = lot.GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal(2, candidates.Count);
        var versionId = candidates[0].GetProperty("inspectionStandardVersionId").GetInt64();

        var date = DateOnly.FromDateTime(DateTime.Today);
        var created = await Ok(await _client.PostAsJsonAsync("/api/inspections", new
        {
            inspectionType = "OUTGOING", inspectionDate = date, productionWorkId = _work, inputIds = new[] { _inputA, _inputB },
            inspectionStandardVersionId = versionId, inspectorEmployeeId = _inspector, items = Items("59.5", "60.1", "61"),
        }));
        var id = created.GetProperty("inspectionId").GetInt64();
        Assert.StartsWith($"TO{date:yyMMdd}-", created.GetProperty("inspectionNo").GetString());
        Assert.Equal("PASS", created.GetProperty("decision").GetString());

        var detail = await DetailAsync(id);
        Assert.Equal(("HD-01", 2), (detail.GetProperty("targets")[0].GetProperty("submitLotNo").GetString(), detail.GetProperty("targets").GetArrayLength()));
        Assert.Equal(["59.5", "60.1", "61"], detail.GetProperty("items")[0].GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("59.5~61", detail.GetProperty("items")[0].GetProperty("result").GetString());

        // 다시 저장 — 대상 1개로 줄이고 불합격 값
        var updated = await Ok(await _client.PutAsJsonAsync($"/api/inspections/{id}", new
        {
            rowVersion = await VersionAsync(id), inspectionDate = date, inputIds = new[] { _inputA }, inspectionStandardVersionId = versionId,
            inspectorEmployeeId = _inspector, items = Items("57.2", "60"),
        }));
        Assert.Equal("FAIL", updated.GetProperty("decision").GetString());

        // 확정 → 대상마다 부적합 (수량 = 대상 수량 전체)
        var done = await Ok(await _client.PostAsJsonAsync($"/api/inspections/{id}/complete", new { rowVersion = await VersionAsync(id) }));
        Assert.Equal(1, done.GetProperty("defects").GetInt32());
        await using (var c = await fx.OpenAsync())
        {
            var defect = await c.QuerySingleAsync<(decimal Qty, string Status, long MainWork)>(
                "SELECT defect_qty, status, CAST(main_work_id AS SIGNED) FROM defect_occurrence WHERE production_work_input_id = @_inputA", new { _inputA });
            Assert.Equal((400m, "OPEN", _work), defect);
        }

        // 확정 후 수정 불가 → 재검사 (새 번호, 대상·측정값 복사)
        var edit = await _client.PutAsJsonAsync($"/api/inspections/{id}", new
        {
            rowVersion = await VersionAsync(id), inspectionDate = date, inputIds = new[] { _inputA }, inspectorEmployeeId = _inspector, items = Items("60"),
        });
        Assert.Equal("INSPECTION_NOT_EDITABLE", await Code(edit));
        var re = await Ok(await _client.PostAsJsonAsync($"/api/inspections/{id}/reinspect", new { rowVersion = await VersionAsync(id), reason = "재측정" }));
        var reId = re.GetProperty("inspectionId").GetInt64();
        var reDetail = await DetailAsync(reId);
        Assert.Equal(("IN_PROGRESS", id, 1), (reDetail.GetProperty("inspection").GetProperty("status").GetString(),
            reDetail.GetProperty("inspection").GetProperty("reinspectionOfId").GetInt64(), reDetail.GetProperty("targets").GetArrayLength()));
        Assert.Equal(["57.2", "60"], reDetail.GetProperty("items")[0].GetProperty("values").EnumerateArray().Select(v => v.GetString()));

        await Ok(await _client.PostAsJsonAsync($"/api/inspections/{reId}/cancel", new { rowVersion = await VersionAsync(reId) }));
        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/inspections?search={_lotNo}");
        Assert.Equal(2, list.GetArrayLength());
    }

    [Fact]
    public async Task Validation_and_targets_must_belong_to_lot()
    {
        var missing = await _client.PostAsJsonAsync("/api/inspections", new
        {
            inspectionType = "OUTGOING", inspectionDate = DateOnly.FromDateTime(DateTime.Today), productionWorkId = _work, inputIds = Array.Empty<long>(),
        });
        var errors = (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("inputIds", out _) && errors.TryGetProperty("inspectorEmployeeId", out _));

        var other = await _client.PostAsJsonAsync("/api/inspections", new
        {
            inspectionType = "PROCESS", inspectionDate = DateOnly.FromDateTime(DateTime.Today), productionWorkId = _work, inputIds = new[] { 999999999L },
            inspectorEmployeeId = _inspector,
        });
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);

        var noDecision = await Ok(await _client.PostAsJsonAsync("/api/inspections", new
        {
            inspectionType = "PROCESS", inspectionDate = DateOnly.FromDateTime(DateTime.Today), productionWorkId = _work, inputIds = new[] { _inputB },
            inspectorEmployeeId = _inspector,
        }));
        var id = noDecision.GetProperty("inspectionId").GetInt64();
        Assert.StartsWith("TP", noDecision.GetProperty("inspectionNo").GetString());
        Assert.Equal("DECISION_REQUIRED", await Code(await _client.PostAsJsonAsync($"/api/inspections/{id}/complete", new { rowVersion = await VersionAsync(id) })));
    }
}
