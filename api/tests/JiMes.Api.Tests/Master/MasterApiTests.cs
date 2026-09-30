using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Tests.Printing;

namespace JiMes.Api.Tests.Master;

[Collection(ApiCollection.Name)]
public sealed class MasterApiTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    public async Task InitializeAsync() => _client = await fx.LoginAdminAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<long> CreateAsync(string entity, object body)
    {
        var res = await _client.PostAsJsonAsync($"/api/master/{entity}", body);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    private async Task<JsonElement> ProblemAsync(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Customer_crud_with_defaults_audit_and_deactivation()
    {
        var code = $"C-{Tag()}";
        var id = await CreateAsync("customer", new { customer_code = code, customer_name = "  대영오토  ", closing_day = 25 });

        var row = await _client.GetFromJsonAsync<JsonElement>($"/api/master/customer/{id}");
        Assert.Equal("대영오토", row.GetProperty("customer_name").GetString());   // 앞뒤 공백 제거
        Assert.Equal("SALES", row.GetProperty("customer_type").GetString());       // 정의 기본값
        Assert.True(row.GetProperty("is_active").GetBoolean());

        var update = await _client.PutAsJsonAsync($"/api/master/customer/{id}", new { is_active = false, __reason = "거래 종료" });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var active = await _client.GetFromJsonAsync<JsonElement>($"/api/master/customer?search={code}");
        Assert.Equal(0, active.GetProperty("total").GetInt64());   // 기본 목록은 사용 중만
        var all = await _client.GetFromJsonAsync<JsonElement>($"/api/master/customer?search={code}&includeInactive=true");
        Assert.Equal(1, all.GetProperty("total").GetInt64());

        await using var c = await fx.OpenAsync();
        Assert.Equal(["CREATE", "STATUS_CHANGE"], await c.QueryAsync<string>(
            "SELECT action_type FROM audit_log WHERE table_name = 'customer' AND record_id = @id ORDER BY audit_log_id", new { id }));

        // 사용 중지 대상은 삭제 경로가 없다
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.DeleteAsync($"/api/master/customer/{id}")).StatusCode);
    }

    [Fact]
    public async Task Validation_reports_every_field_error()
    {
        var existing = $"C-{Tag()}";
        await CreateAsync("customer", new { customer_code = existing, customer_name = "기존" });
        var res = await _client.PostAsJsonAsync("/api/master/customer",
            new { customer_code = existing, customer_type = "NOPE", closing_day = 40, email = new string('x', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await ProblemAsync(res)).GetProperty("errors");
        foreach (var field in new[] { "customer_code", "customer_name", "customer_type", "closing_day", "email" })
            Assert.True(errors.TryGetProperty(field, out _), $"{field} 오류 없음: {errors}");
    }

    [Fact]
    public async Task Lookup_labels_times_and_one_of_rule()
    {
        var typeId = await CreateAsync("equipment_type", new { equipment_type_code = $"T-{Tag()}", equipment_type_name = "진공로" });
        var eqId = await CreateAsync("equipment", new { equipment_type_id = typeId, equipment_code = $"V-{Tag()}", equipment_name = "진공로1" });
        var eq = await _client.GetFromJsonAsync<JsonElement>($"/api/master/equipment/{eqId}");
        Assert.Equal("진공로", eq.GetProperty("equipment_type_id__label").GetString());

        var badLookup = await _client.PostAsJsonAsync("/api/master/equipment", new { equipment_type_id = 999999, equipment_code = $"X-{Tag()}", equipment_name = "x" });
        Assert.True((await ProblemAsync(badLookup)).GetProperty("errors").TryGetProperty("equipment_type_id", out _));

        // 사용 중지로 만든다 — 활성 교대는 작업일 시작 시각을 바꿔 같은 DB 의 스케줄 테스트에 영향 (설계 §7)
        var shift = await CreateAsync("work_shift", new { work_shift_code = $"S-{Tag()}", work_shift_name = "야간", start_time = "20:00", end_time = "08:00", is_next_day_end = true, is_active = false });
        var s = await _client.GetFromJsonAsync<JsonElement>($"/api/master/work_shift/{shift}");
        Assert.Equal("20:00", s.GetProperty("start_time").GetString());

        // 기준시간: 설비유형 또는 설비 중 하나 필수 (DDL CHECK)
        var none = await _client.PostAsJsonAsync("/api/master/process_default_time", new { running_time_min = 60 });
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        var ok = await CreateAsync("process_default_time", new { equipment_type_id = typeId, running_time_min = 90.5 });
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/master/process_default_time/{ok}")).StatusCode);
    }

    [Fact]
    public async Task Calendar_date_is_unique_and_deletable()
    {
        var date = new DateOnly(2030, 1, 1).AddDays(Random.Shared.Next(3000)).ToString("yyyy-MM-dd");
        var id = await CreateAsync("work_calendar", new { calendar_date = date, remark = "테스트 휴일" });   // day_type 기본 HOLIDAY
        var dup = await _client.PostAsJsonAsync("/api/master/work_calendar", new { calendar_date = date });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        var row = await _client.GetFromJsonAsync<JsonElement>($"/api/master/work_calendar/{id}");
        Assert.Equal((date, "HOLIDAY"), (row.GetProperty("calendar_date").GetString(), row.GetProperty("day_type").GetString()));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/master/work_calendar/{id}")).StatusCode);
    }

    [Fact]
    public async Task Company_stamp_image_upload_and_download()
    {
        var id = await CreateAsync("company", new { company_name = $"자사-{Tag()}" });
        var form = new MultipartFormDataContent();
        var png = new ByteArrayContent(ExcelTemplateRendererTests.Png1x1);
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(png, "file", "stamp.png");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsync($"/api/master/company/{id}/image/stamp_image", form)).StatusCode);

        var row = await _client.GetFromJsonAsync<JsonElement>($"/api/master/company/{id}");
        Assert.True(row.GetProperty("has_stamp_image").GetBoolean());
        Assert.Equal(ExcelTemplateRendererTests.Png1x1, await _client.GetByteArrayAsync($"/api/master/company/{id}/image/stamp_image"));

        var notImage = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "x.png" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsync($"/api/master/company/{id}/image/stamp_image", notImage)).StatusCode);
    }

    [Fact]
    public async Task Options_need_login_only_and_writes_need_permission()
    {
        var login = $"m_ro_{Tag()}";
        await fx.CreateUserAsync(login, "master-ro-pw", ("master.customer", PermissionAction.Read));
        var client = await fx.LoginAsync(login, "master-ro-pw");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/master/customer")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/master/equipment/options")).StatusCode);   // 다른 화면 드롭다운용
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/master/equipment")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/master/customer", new { customer_code = "x", customer_name = "x" })).StatusCode);
    }
}
