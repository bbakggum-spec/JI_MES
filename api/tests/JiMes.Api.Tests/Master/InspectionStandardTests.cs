using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace JiMes.Api.Tests.Master;

[Collection(ApiCollection.Name)]
public sealed class InspectionStandardTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private long _part, _customer;
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        await using var c = await fx.OpenAsync();
        _part = await c.ExecuteScalarAsync<long>("INSERT INTO part (part_code, part_name) VALUES (@c, '피니언'); SELECT LAST_INSERT_ID();", new { c = $"IP-{Tag()}" });
        _customer = await c.ExecuteScalarAsync<long>("INSERT INTO customer (customer_code, customer_name) VALUES (@c, '대영'); SELECT LAST_INSERT_ID();", new { c = $"IC-{Tag()}" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object Hardness(decimal lo = 58, decimal hi = 62) => new
    {
        itemType = "APPEARANCE", itemName = "표면경도", specificationValue = $"HRC {lo}~{hi}", scale = "HRC", rangeType = "BETWEEN",
        lowerLimit = lo, upperLimit = hi, sampleCount = 5, testCount = 1, points = new[] { "치면", "치저", "보스" },
    };

    [Fact]
    public async Task Create_edit_in_place_then_new_version_after_use()
    {
        var res = await _client.PostAsJsonAsync("/api/inspection-standards", new { partId = _part, customerId = _customer, criteria = new[] { Hardness() } });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inspectionStandardId").GetInt64();

        var dup = await _client.PostAsJsonAsync("/api/inspection-standards", new { partId = _part, customerId = _customer, criteria = new[] { Hardness() } });
        var dupBody = await dup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("DUPLICATE_INSPECTION_STANDARD", id), (dupBody.GetProperty("code").GetString(), dupBody.GetProperty("inspectionStandardId").GetInt64()));

        // 검사에서 쓰기 전 → v1 수정
        var edit = await (await _client.PutAsJsonAsync($"/api/inspection-standards/{id}/criteria", new { criteria = new[] { Hardness(57, 62) } })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((1, false), (edit.GetProperty("versionNo").GetInt32(), edit.GetProperty("newVersion").GetBoolean()));

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/inspection-standards/{id}");
        var criteria = detail.GetProperty("criteria")[0];
        Assert.Equal(57m, criteria.GetProperty("lowerLimit").GetDecimal());
        Assert.Equal(["치면", "치저", "보스"], criteria.GetProperty("points").EnumerateArray().Select(p => p.GetString()));

        // 검사가 v1 을 참조 → 다음 수정은 v2, v1 보존 (같은 초 연속 저장도 가능)
        await using (var c = await fx.OpenAsync())
        {
            var v1 = await c.ExecuteScalarAsync<long>("SELECT inspection_standard_version_id FROM inspection_standard_version WHERE inspection_standard_id = @id", new { id });
            var up = await c.ExecuteScalarAsync<long>("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@c, 'x'); SELECT LAST_INSERT_ID();", new { c = $"U-{Tag()}" });
            var work = await c.ExecuteScalarAsync<long>("INSERT INTO production_work (lot_no, unit_process_id, work_date) VALUES (@l, @up, CURDATE()); SELECT LAST_INSERT_ID();", new { l = $"L-{Tag()}", up });
            await c.ExecuteAsync("INSERT INTO inspection (inspection_no, inspection_date, production_work_id, inspection_standard_version_id) VALUES (@n, CURDATE(), @work, @v1)",
                new { n = $"TO-{Tag()}", work, v1 });
        }
        var v2 = await (await _client.PutAsJsonAsync($"/api/inspection-standards/{id}/criteria", new { criteria = new[] { Hardness(58, 63) }, remark = "상한 변경" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((2, true), (v2.GetProperty("versionNo").GetInt32(), v2.GetProperty("newVersion").GetBoolean()));
        var v3 = await (await _client.PutAsJsonAsync($"/api/inspection-standards/{id}/criteria", new { criteria = new[] { Hardness(58, 64) } })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((2, false), (v3.GetProperty("versionNo").GetInt32(), v3.GetProperty("newVersion").GetBoolean()));   // v2 는 아직 미사용 → 수정

        detail = await _client.GetFromJsonAsync<JsonElement>($"/api/inspection-standards/{id}");
        var v1Id = detail.GetProperty("versions").EnumerateArray().Single(v => v.GetProperty("versionNo").GetInt32() == 1).GetProperty("inspectionStandardVersionId").GetInt64();
        var old = await _client.GetFromJsonAsync<JsonElement>($"/api/inspection-standards/{id}?versionId={v1Id}");
        Assert.Equal(57m, old.GetProperty("criteria")[0].GetProperty("lowerLimit").GetDecimal());
    }

    [Fact]
    public async Task Range_rules_are_validated_per_row()
    {
        var res = await _client.PostAsJsonAsync("/api/inspection-standards", new
        {
            partId = _part, customerId = (long?)null,
            criteria = new object[]
            {
                new { itemName = "경화깊이", rangeType = "MIN", sampleCount = 1, testCount = 1 },                                    // 하한 없음
                new { itemName = "심부경도", rangeType = "BETWEEN", lowerLimit = 40, upperLimit = 30, sampleCount = 1, testCount = 1 },  // 하한 > 상한
                new { itemName = "", rangeType = "NONE", sampleCount = 0, testCount = 1 },                                          // 이름·시료수
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("criteria");
        Assert.True(errors.GetArrayLength() >= 4, errors.ToString());
    }
}
