using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace JiMes.Api.Tests.Master;

[Collection(ApiCollection.Name)]
public sealed class ProcessStandardTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private long _wash, _carb, _temp, _part, _gasType, _tempCi, _timeCi;
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        await using var c = await fx.OpenAsync();
        async Task<long> Insert(string sql, object args) => await c.ExecuteScalarAsync<long>(sql + "; SELECT LAST_INSERT_ID();", args);
        _wash = await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@c, '세척')", new { c = $"W-{Tag()}" });
        _carb = await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@c, '침탄')", new { c = $"C-{Tag()}" });
        _temp = await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@c, '템퍼링')", new { c = $"T-{Tag()}" });
        _part = await Insert("INSERT INTO part (part_code, part_name) VALUES (@c, '기어')", new { c = $"G-{Tag()}" });
        _gasType = await Insert("INSERT INTO equipment_type (equipment_type_code, equipment_type_name) VALUES (@c, '가스로')", new { c = $"GT-{Tag()}" });
        _tempCi = await Insert("INSERT INTO condition_item (condition_item_code, condition_item_name, unit_code) VALUES (@c, '온도', '℃')", new { c = $"TP-{Tag()}" });
        _timeCi = await Insert("INSERT INTO condition_item (condition_item_code, condition_item_name, unit_code, value_type) VALUES (@c, '비고', NULL, 'TEXT')", new { c = $"RM-{Tag()}" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> Ok(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Route_edits_in_place_until_used_then_creates_new_version()
    {
        var created = await Ok(await _client.PostAsJsonAsync("/api/heat-processes", new
        {
            heatProcessCode = $"HP-{Tag()}", heatProcessName = "침탄 소입",
            operations = new[] { new { unitProcessId = _wash, isMainProcess = false }, new { unitProcessId = _carb, isMainProcess = true } },
        }));
        var id = created.GetProperty("heatProcessId").GetInt64();

        // 참조 없음 → 현재 버전 수정
        var edit = await Ok(await _client.PutAsJsonAsync($"/api/heat-processes/{id}/route", new
        {
            operations = new[] { new { unitProcessId = _wash, isMainProcess = false }, new { unitProcessId = _carb, isMainProcess = true }, new { unitProcessId = _temp, isMainProcess = false } },
        }));
        Assert.Equal((1, false), (edit.GetProperty("versionNo").GetInt32(), edit.GetProperty("newVersion").GetBoolean()));

        // 수주 품목이 v1 을 참조 → 다음 수정은 v2
        await using (var c = await fx.OpenAsync())
        {
            var v1 = await c.ExecuteScalarAsync<long>("SELECT heat_process_version_id FROM heat_process_version WHERE heat_process_id = @id", new { id });
            var customer = await c.ExecuteScalarAsync<long>("INSERT INTO customer (customer_code, customer_name) VALUES (@c, 'x'); SELECT LAST_INSERT_ID();", new { c = $"X-{Tag()}" });
            var order = await c.ExecuteScalarAsync<long>("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@n, CURDATE(), @customer); SELECT LAST_INSERT_ID();", new { n = $"SO-{Tag()}", customer });
            await c.ExecuteAsync("INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, heat_process_version_id, order_qty) VALUES (@n, @order, 1, @_part, @v1, 10)",
                new { n = $"I-{Tag()}", order, _part, v1 });
        }
        var next = await Ok(await _client.PutAsJsonAsync($"/api/heat-processes/{id}/route", new { operations = new[] { new { unitProcessId = _carb, isMainProcess = true } }, remark = "세척 생략" }));
        Assert.Equal((2, true), (next.GetProperty("versionNo").GetInt32(), next.GetProperty("newVersion").GetBoolean()));

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/heat-processes/{id}");
        var versions = detail.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(3, versions.Single(v => v.GetProperty("versionNo").GetInt32() == 1).GetProperty("operations").GetArrayLength());   // v1 보존
        Assert.EndsWith("침탄*", detail.GetProperty("header").GetProperty("routeSummary").GetString());

        var twoMain = await _client.PutAsJsonAsync($"/api/heat-processes/{id}/route", new
        {
            operations = new[] { new { unitProcessId = _wash, isMainProcess = true }, new { unitProcessId = _carb, isMainProcess = true } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, twoMain.StatusCode);
    }

    private async Task<(long Id, JsonElement Detail)> CreateTemplateAsync()
    {
        var res = await Ok(await _client.PostAsJsonAsync("/api/step-templates", new
        {
            stepTemplateCode = $"ST-{Tag()}", stepTemplateName = "가스 침탄", unitProcessId = _carb, equipmentTypeId = _gasType, isActive = true,
            steps = new[] { "승온", "침탄", "확산" },
            conditionItemIds = new[] { _tempCi, _timeCi },
        }));
        var id = res.GetProperty("stepTemplateId").GetInt64();
        return (id, await _client.GetFromJsonAsync<JsonElement>($"/api/step-templates/{id}"));
    }

    [Fact]
    public async Task Standard_owns_variable_steps_and_items_per_version()
    {
        var (templateId, template) = await CreateTemplateAsync();
        Assert.Equal(["승온", "침탄", "확산"], template.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("stepName").GetString()));

        // 템플릿에서 불러온 뒤 스텝 추가 (구 가변 그리드) — 조건은 (스텝 순서, 항목)
        var created = await Ok(await _client.PostAsJsonAsync("/api/standards", new
        {
            partId = _part, unitProcessId = _carb, equipmentTypeId = _gasType,
            version = new
            {
                stepTemplateId = templateId, chargeQty = 400, runningTimeMin = 420,
                steps = new[] { "승온", "침탄", "확산", "강온" }, items = new[] { _tempCi, _timeCi },
                conditions = new object[]
                {
                    new { stepNo = 2, conditionItemId = _tempCi, conditionValue = "920" },
                    new { stepNo = 4, conditionItemId = _tempCi, conditionValue = "850" },
                    new { stepNo = (int?)null, conditionItemId = _timeCi, conditionValue = "공통 메모" },   // 단계 무관 (LOT 공통)
                    new { stepNo = 3, conditionItemId = _tempCi, conditionValue = "" },                    // 빈 값은 저장 안 함
                },
            },
        }));
        var standardId = created.GetProperty("standardId").GetInt64();
        Assert.StartsWith("STD-G-", created.GetProperty("standardCode").GetString());

        var dup = await _client.PostAsJsonAsync("/api/standards", new { partId = _part, unitProcessId = _carb, equipmentTypeId = _gasType, version = new { chargeQty = 1 } });
        var dupBody = await dup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("DUPLICATE_STANDARD", standardId), (dupBody.GetProperty("code").GetString(), dupBody.GetProperty("standardId").GetInt64()));

        // 숫자 항목에 문자 / 없는 스텝 / 행에 없는 항목 → 거부
        foreach (var bad in new object[]
                 {
                     new { chargeQty = 1, steps = new[] { "승온" }, items = new[] { _tempCi }, conditions = new[] { new { stepNo = 1, conditionItemId = _tempCi, conditionValue = "구백" } } },
                     new { chargeQty = 1, steps = new[] { "승온" }, items = new[] { _tempCi }, conditions = new[] { new { stepNo = 2, conditionItemId = _tempCi, conditionValue = "900" } } },
                     new { chargeQty = 1, steps = new[] { "승온" }, items = new[] { _tempCi }, conditions = new[] { new { stepNo = 1, conditionItemId = _timeCi, conditionValue = "x" } } },
                     new { chargeQty = 1, steps = new[] { " " }, items = new[] { _tempCi } },
                 })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/standards/{standardId}/versions", bad)).StatusCode);

        // v2: 템플릿 없이 스텝 이름·순서·항목 순서를 바꾼다
        var v2 = await Ok(await _client.PostAsJsonAsync($"/api/standards/{standardId}/versions", new
        {
            chargeQty = 500, runningTimeMin = 400, remark = "스텝 변경",
            steps = new[] { "침탄(고온)", "소입" }, items = new[] { _timeCi, _tempCi },
            conditions = new[] { new { stepNo = (int?)1, conditionItemId = _tempCi, conditionValue = "930" } },
        }));
        Assert.Equal(2, v2.GetProperty("versionNo").GetInt32());

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/standards/{standardId}");
        Assert.Equal(2, detail.GetProperty("version").GetProperty("versionNo").GetInt32());
        Assert.Equal(["침탄(고온)", "소입"], detail.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("stepName").GetString()));
        Assert.Equal([_timeCi, _tempCi], detail.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("conditionItemId").GetInt64()));
        Assert.Equal(("930", 1), (detail.GetProperty("conditions")[0].GetProperty("conditionValue").GetString(), detail.GetProperty("conditions")[0].GetProperty("stepNo").GetInt32()));

        // 과거 버전 보존 — 스텝 4개, 값 3개
        var v1Id = detail.GetProperty("versions").EnumerateArray().Single(v => v.GetProperty("versionNo").GetInt32() == 1).GetProperty("standardVersionId").GetInt64();
        var v1 = await _client.GetFromJsonAsync<JsonElement>($"/api/standards/{standardId}?versionId={v1Id}");
        Assert.Equal((4, 3), (v1.GetProperty("steps").GetArrayLength(), v1.GetProperty("conditions").GetArrayLength()));

        // 템플릿은 초기값 — 스텝을 지우고 바꿔도 기존 표준은 그대로
        await Ok(await _client.PutAsJsonAsync($"/api/step-templates/{templateId}", new
        {
            stepTemplateCode = template.GetProperty("header").GetProperty("stepTemplateCode").GetString(), stepTemplateName = "가스 침탄",
            unitProcessId = _carb, equipmentTypeId = _gasType, isActive = true,
            steps = new[] { "침탄" }, conditionItemIds = new[] { _tempCi },
        }));
        var after = await _client.GetFromJsonAsync<JsonElement>($"/api/step-templates/{templateId}");
        Assert.Equal(["침탄"], after.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("stepName").GetString()));
        v1 = await _client.GetFromJsonAsync<JsonElement>($"/api/standards/{standardId}?versionId={v1Id}");
        Assert.Equal(4, v1.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public async Task Template_must_match_standard_unit_process()
    {
        var (templateId, _) = await CreateTemplateAsync();   // 침탄 템플릿
        var res = await _client.PostAsJsonAsync("/api/standards", new
        {
            partId = _part, unitProcessId = _temp, version = new { stepTemplateId = templateId, chargeQty = 1 },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
