using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using JiMes.Api.Tests.Printing;

namespace JiMes.Api.Tests.Master;

[Collection(ApiCollection.Name)]
public sealed class PartApiTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private long _customerA;
    private long _customerB;
    private long _heatProcess;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        await using var c = await fx.OpenAsync();
        _customerA = await c.ExecuteScalarAsync<long>("INSERT INTO customer (customer_code, customer_name) VALUES (@c, '한독'); SELECT LAST_INSERT_ID();", new { c = $"PA-{Tag()}" });
        _customerB = await c.ExecuteScalarAsync<long>("INSERT INTO customer (customer_code, customer_name) VALUES (@c, '삼미'); SELECT LAST_INSERT_ID();", new { c = $"PB-{Tag()}" });
        _heatProcess = await c.ExecuteScalarAsync<long>("INSERT INTO heat_process (heat_process_code, heat_process_name) VALUES (@c, '침탄'); SELECT LAST_INSERT_ID();", new { c = $"HP-{Tag()}" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private object Body(string code, string? number = null, bool allowDup = false, object[]? customers = null) => new
    {
        partCode = code, partName = "헬리컬 기어", partNumber = number, priceBasis = "KG", unitPrice = 1200, isActive = true,
        customers = customers ?? [new { customerId = _customerA, customerPartCode = "HD-G1", isCustomerLotRequired = true, isPrimary = true }],
        heatProcesses = new[] { new { heatProcessId = _heatProcess, isDefault = true } },
        allowDuplicatePartNumber = allowDup,
    };

    private async Task<long> CreateAsync(object body)
    {
        var res = await _client.PostAsJsonAsync("/api/parts", body);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("partId").GetInt64();
    }

    [Fact]
    public async Task Create_with_customers_and_process_then_update_keeps_history()
    {
        var id = await CreateAsync(Body($"P-{Tag()}"));
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/parts/{id}");
        Assert.Equal("KG", detail.GetProperty("part").GetProperty("priceBasis").GetString());
        Assert.Equal("HD-G1", detail.GetProperty("customers")[0].GetProperty("customerPartCode").GetString());
        Assert.Equal("침탄", detail.GetProperty("part").GetProperty("defaultHeatProcessName").GetString());

        // 거래처를 B 로 바꾸면 A 는 사용 중지 (삭제 아님)
        var update = await _client.PutAsJsonAsync($"/api/parts/{id}",
            Body(detail.GetProperty("part").GetProperty("partCode").GetString()!, customers: [new { customerId = _customerB, customerPartCode = (string?)null, isCustomerLotRequired = false, isPrimary = true }]));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        detail = await _client.GetFromJsonAsync<JsonElement>($"/api/parts/{id}");
        Assert.Equal("삼미", detail.GetProperty("customers").EnumerateArray().Single().GetProperty("customerName").GetString());
        await using (var c = await fx.OpenAsync())
            Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT is_active FROM part_customer WHERE part_id = @id AND customer_id = @_customerA", new { id, _customerA }));

        var history = await _client.GetFromJsonAsync<JsonElement>($"/api/parts/{id}/history");
        Assert.Equal(["UPDATE", "CREATE"], history.EnumerateArray().Select(h => h.GetProperty("changeType").GetString()));
        Assert.Contains("\"customer_part_code\":\"HD-G1\"", history[0].GetProperty("oldDataJson").GetString());

        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/parts?customerId={_customerB}");
        Assert.Contains(list.GetProperty("items").EnumerateArray(), p => p.GetProperty("partId").GetInt64() == id);
    }

    [Fact]
    public async Task Duplicate_part_number_needs_confirmation_and_code_must_be_unique()
    {
        var number = $"N-{Tag()}";
        var code = $"P-{Tag()}";
        await CreateAsync(Body(code, number));

        var dupNumber = await _client.PostAsJsonAsync("/api/parts", Body($"P-{Tag()}", number));
        Assert.Equal("DUPLICATE_PART_NUMBER", (await dupNumber.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await CreateAsync(Body($"P-{Tag()}", number, allowDup: true));   // 확인 후 저장

        var dupCode = await _client.PostAsJsonAsync("/api/parts", Body(code));
        Assert.Equal(HttpStatusCode.BadRequest, dupCode.StatusCode);

        var twoPrimary = await _client.PostAsJsonAsync("/api/parts", Body($"P-{Tag()}", customers:
        [
            new { customerId = _customerA, customerPartCode = (string?)null, isCustomerLotRequired = false, isPrimary = true },
            new { customerId = _customerB, customerPartCode = (string?)null, isCustomerLotRequired = false, isPrimary = true },
        ]));
        Assert.Equal(HttpStatusCode.BadRequest, twoPrimary.StatusCode);
    }

    [Fact]
    public async Task Drawing_attachment_round_trip_and_kind_check()
    {
        var id = await CreateAsync(Body($"P-{Tag()}"));
        async Task<HttpResponseMessage> Upload(string kind, byte[] content, string name)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", name);
            form.Add(new StringContent(kind), "kind");
            return await _client.PostAsync($"/api/parts/{id}/attachments", form);
        }

        var pdf = "%PDF-1.4 도면"u8.ToArray();
        var ok = await Upload("PART_DRAWING", pdf, "도면-A.pdf");
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        var attachmentId = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attachmentId").GetInt64();

        var file = await _client.GetAsync($"/api/parts/{id}/attachments/{attachmentId}");
        Assert.Equal(pdf, await file.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", file.Content.Headers.ContentType?.MediaType);

        var wrongKind = await Upload("HARDNESS_CHART", ExcelTemplateRendererTests.Png1x1, "chart.png");   // 검사 첨부 종류
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/parts/{id}/attachments/{attachmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/parts/{id}/attachments/{attachmentId}")).StatusCode);
    }

    [Fact]
    public async Task Print_template_links_drive_template_resolution()
    {
        var partId = await CreateAsync(Body($"P-{Tag()}"));
        await using var c = await fx.OpenAsync();
        var purpose = await c.ExecuteScalarAsync<long>("SELECT print_purpose_id FROM print_purpose WHERE purpose_code = 'INSPECTION_REPORT'");
        var template = await c.ExecuteScalarAsync<long>(
            "INSERT INTO print_template (print_template_name, print_purpose_id, template_kind) VALUES (@n, @purpose, 'EXCEL'); SELECT LAST_INSERT_ID();",
            new { n = $"한독용-{Tag()}", purpose });

        var bad = await _client.PutAsJsonAsync($"/api/parts/{partId}/print-templates", new[]
        {
            new { printTemplateId = template, customerId = (long?)_customerA, isDefault = true },
            new { printTemplateId = template, customerId = (long?)_customerA, isDefault = true },   // 중복은 합쳐짐
        });
        Assert.Equal(HttpStatusCode.NoContent, bad.StatusCode);
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/parts/{partId}");
        var link = detail.GetProperty("printTemplates").EnumerateArray().Single();
        Assert.Equal(("INSPECTION_REPORT", "한독"), (link.GetProperty("purposeCode").GetString(), link.GetProperty("customerName").GetString()));

        var lookups = await _client.GetFromJsonAsync<JsonElement>("/api/parts/lookups");
        Assert.Contains(lookups.GetProperty("templates").EnumerateArray(), t => t.GetProperty("value").GetInt64() == template);
    }
}
