using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;

namespace JiMes.Api.Tests.Printing;

/// <summary>서버에 LibreOffice 가 없으면 PDF 변환 테스트는 건너뛴다 (변환 경로는 설정 print.pdf_converter_path).</summary>
public sealed class LibreOfficeFactAttribute : FactAttribute
{
    public const string DefaultPath = @"C:\Program Files\LibreOffice\program\soffice.exe";

    public LibreOfficeFactAttribute()
    {
        if (!File.Exists(DefaultPath))
            Skip = $"LibreOffice 없음 ({DefaultPath})";
    }
}

[Collection(ApiCollection.Name)]
public sealed class PrintApiTests(ApiFixture fx) : IAsyncLifetime
{
    private sealed record Seed(long TargetA, long TargetB, long InspectionId, long PartA, long CustomerId, long ShipmentId);

    private Seed _s = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        _s = await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>검사 1건(대상 2개, 측정 항목 2개 × 시료 3) + 출하 전표 1건(품목 7행 → 2쪽) + 자사 정보</summary>
    private async Task<Seed> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var c = await fx.OpenAsync();
        async Task<long> Insert(string sql, object? args = null) => await c.ExecuteScalarAsync<long>(sql + "; SELECT LAST_INSERT_ID();", args);

        var customer = await Insert("INSERT INTO customer (customer_code, customer_name, business_no) VALUES (@code, '한독기어', '123-45-67890')", new { code = $"C-{tag}" });
        var partA = await Insert("INSERT INTO part (part_code, part_name, material) VALUES (@code, '헬리컬 기어', 'SCM420H')", new { code = $"PA-{tag}" });
        var partB = await Insert("INSERT INTO part (part_code, part_name) VALUES (@code, '샤프트')", new { code = $"PB-{tag}" });
        var up = await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@code, '침탄')", new { code = $"U-{tag}" });
        var order = await Insert("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@no, CURDATE(), @customer)", new { no = $"SO-{tag}", customer });
        var itemA = await Insert("INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, order_qty, customer_lot, unit_price) VALUES (@no, @order, 1, @partA, 400, 'CL-1', 100)", new { no = $"I-{tag}-1", order, partA });
        var itemB = await Insert("INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, order_qty) VALUES (@no, @order, 2, @partB, 100)", new { no = $"I-{tag}-2", order, partB });
        var work = await Insert("INSERT INTO production_work (lot_no, unit_process_id, work_date, status) VALUES (@lot, @up, '2026-09-30', 'COMPLETED')", new { lot = $"L-{tag}", up });
        var inputA = await Insert("INSERT INTO production_work_input (production_work_id, sales_order_item_id, input_qty) VALUES (@work, @itemA, 400)", new { work, itemA });
        var inputB = await Insert("INSERT INTO production_work_input (production_work_id, sales_order_item_id, input_qty) VALUES (@work, @itemB, 100)", new { work, itemB });

        var std = await Insert("INSERT INTO inspection_standard (part_id, customer_id) VALUES (@partA, @customer)", new { partA, customer });
        var stdv = await Insert("INSERT INTO inspection_standard_version (inspection_standard_id, version_no, effective_from, is_current) VALUES (@std, 1, '2026-01-01', 1)", new { std });
        var crit = await Insert(
            """
            INSERT INTO inspection_criteria (inspection_standard_version_id, sequence_no, item_type, item_name, specification_value, scale, range_type, lower_limit, upper_limit, sample_count)
            VALUES (@stdv, 1, 'APPEARANCE', '표면경도', 'HRC 58~62', 'HRC', 'BETWEEN', 58, 62, 3)
            """, new { stdv });
        var inspection = await Insert(
            """
            INSERT INTO inspection (inspection_no, inspection_type, inspection_date, production_work_id, inspection_standard_version_id, status, decision)
            VALUES (@no, 'OUTGOING', '2026-09-30', @work, @stdv, 'COMPLETED', 'PASS')
            """, new { no = $"TO-{tag}", work, stdv });
        var targetA = await Insert(
            """
            INSERT INTO inspection_target (inspection_id, sub_no, production_work_input_id, sales_order_item_id, customer_id, inspection_qty, submit_lot_no_snapshot)
            VALUES (@inspection, 1, @inputA, @itemA, @customer, 400, 'HD-SUB-01')
            """, new { inspection, inputA, itemA, customer });
        var targetB = await Insert(
            """
            INSERT INTO inspection_target (inspection_id, sub_no, production_work_input_id, sales_order_item_id, customer_id, inspection_qty)
            VALUES (@inspection, 2, @inputB, @itemB, @customer, 100)
            """, new { inspection, inputB, itemB, customer });
        for (var seq = 1; seq <= 2; seq++)
        {
            var item = await Insert(
                "INSERT INTO inspection_item (inspection_id, inspection_criteria_id, sequence_no, item_type, item_name, decision) VALUES (@inspection, @crit, @seq, 'APPEARANCE', @name, 'PASS')",
                new { inspection, crit, seq, name = $"경도{seq}" });
            for (var s = 1; s <= 3; s++)
                await Insert("INSERT INTO inspection_measurement (inspection_item_id, sample_no, measured_value) VALUES (@item, @s, @v)", new { item, s, v = 59.5m + s });
        }

        if (await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM company") == 0)
            await Insert("INSERT INTO company (company_name, business_no, ceo_name, stamp_image) VALUES ('제이아이열처리', '111-22-33333', '대표', @stamp)",
                new { stamp = ExcelTemplateRendererTests.Png1x1 });
        var shipment = await Insert(
            """
            INSERT INTO shipment (shipment_no, shipment_date, customer_id, supply_amount, vat_amount, total_amount, customer_name_snapshot)
            VALUES (@no, '2026-09-30', @customer, 70000, 7000, 77000, '한독기어')
            """, new { no = $"O-{tag}", customer });
        for (var line = 1; line <= 7; line++)
            await Insert(
                "INSERT INTO shipment_item (shipment_id, line_no, sales_order_item_id, shipment_qty, price_basis_snapshot, unit_price_snapshot, amount) VALUES (@shipment, @line, @itemA, 100, 'EA', 100, 10000)",
                new { shipment, line, itemA });

        return new Seed(targetA, targetB, inspection, partA, customer, shipment);
    }

    private static byte[] ReportTemplate(Action<IXLWorksheet>? extra = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("성적서");
        ws.Cell("A1").Value = "{{검사번호}}";
        ws.Cell("A2").Value = "{{PartName}} / {{ConvertLot}} / {{Decision}}";
        ws.Cell("A3").Value = "{{T1_1_P1}}";   // 구 좌표형 키
        ws.Cell("A4").Value = "{{#Measurements}}{{Item}}";
        ws.Cell("B4").Value = "{{P1}}{{/Measurements}}";
        extra?.Invoke(ws);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private async Task<long> CreateExcelTemplateAsync(string purpose, string outputFormat, byte[] file, string? name = null)
    {
        var res = await _client.PostAsJsonAsync("/api/print/templates",
            new { purposeCode = purpose, printTemplateName = name ?? $"양식-{Guid.NewGuid():N}"[..20], outputFormat });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printTemplateId").GetInt64();
        var upload = await UploadAsync(id, file);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        return id;
    }

    private Task<HttpResponseMessage> UploadAsync(long templateId, byte[] file, string fileName = "report.xlsx")
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "file", fileName);
        form.Add(new StringContent("테스트 등록"), "changeNote");
        return _client.PostAsync($"/api/print/templates/{templateId}/versions", form);
    }

    private async Task LinkPartAsync(long templateId, long? customerId)
    {
        await using var c = await fx.OpenAsync();
        await c.ExecuteAsync(
            """
            INSERT INTO part_print_template (part_id, customer_id, print_purpose_id, print_template_id, is_default)
            SELECT @PartA, @customerId, print_purpose_id, @templateId, 1 FROM print_purpose WHERE purpose_code = 'INSPECTION_REPORT'
            """, new { _s.PartA, customerId, templateId });
    }

    [Fact]
    public async Task Upload_reports_unknown_placeholders_and_rejects_duplicates_and_bad_syntax()
    {
        var file = ReportTemplate(ws => ws.Cell("A9").Value = "{{오타키}}");
        var create = await _client.PostAsJsonAsync("/api/print/templates", new { purposeCode = "INSPECTION_REPORT", printTemplateName = $"검증-{Guid.NewGuid():N}"[..20] });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printTemplateId").GetInt64();

        var ok = await UploadAsync(id, file);
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["오타키"], body.GetProperty("unknown").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(1, body.GetProperty("versionNo").GetInt32());

        var dup = await UploadAsync(id, file);
        Assert.Equal("DUPLICATE_TEMPLATE_FILE", (await dup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var bad = await UploadAsync(id, ReportTemplate(ws => ws.Cell("A10").Value = "{{#Measurements}}"));
        Assert.Equal("TEMPLATE_SYNTAX", (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var notExcel = await UploadAsync(id, Encoding.UTF8.GetBytes("hello"), "report.txt");
        Assert.Equal(HttpStatusCode.BadRequest, notExcel.StatusCode);
    }

    [Fact]
    public async Task Issue_xlsx_fills_values_logs_snapshot_and_updates_target()
    {
        var template = await CreateExcelTemplateAsync("INSPECTION_REPORT", "XLSX", ReportTemplate());
        await LinkPartAsync(template, _s.CustomerId);

        var res = await _client.PostAsJsonAsync("/api/print/issue", new { purposeCode = "INSPECTION_REPORT", sourceId = _s.TargetA });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        using var wb = new XLWorkbook(new MemoryStream(await res.Content.ReadAsByteArrayAsync()));
        var ws = wb.Worksheet("성적서");
        Assert.StartsWith("TO-", ws.Cell("A1").GetString());
        Assert.Equal("헬리컬 기어 / HD-SUB-01 / 합격", ws.Cell("A2").GetString());   // 판정 = 공통코드 표시명
        Assert.Equal("60.5", ws.Cell("A3").GetString());
        Assert.Equal(["경도1", "경도2"], [ws.Cell("A4").GetString(), ws.Cell("A5").GetString()]);

        var logId = long.Parse(res.Headers.GetValues("X-Print-Log-Id").Single());
        await using var c = await fx.OpenAsync();
        var target = await c.QuerySingleAsync<(int Count, DateTime? IssuedAt)>(
            "SELECT report_issue_count, report_issued_at FROM inspection_target WHERE inspection_target_id = @TargetA", new { _s.TargetA });
        Assert.Equal(1, target.Count);
        Assert.NotNull(target.IssuedAt);
        var snapshot = await c.ExecuteScalarAsync<string>("SELECT data_snapshot_json FROM print_log WHERE print_log_id = @logId", new { logId });
        Assert.Contains("HD-SUB-01", snapshot);
    }

    [Fact]
    public async Task Reprint_uses_the_version_and_values_of_the_original_issue()
    {
        var template = await CreateExcelTemplateAsync("INSPECTION_REPORT", "XLSX", ReportTemplate());
        await LinkPartAsync(template, _s.CustomerId);
        var first = await _client.PostAsJsonAsync("/api/print/issue", new { purposeCode = "INSPECTION_REPORT", sourceId = _s.TargetA });
        var logId = long.Parse(first.Headers.GetValues("X-Print-Log-Id").Single());

        // 발행 후 원본·양식이 바뀌어도 재발행은 당시 내용
        await using (var c = await fx.OpenAsync())
            await c.ExecuteAsync("UPDATE inspection_target SET submit_lot_no_snapshot = 'CHANGED' WHERE inspection_target_id = @TargetA", new { _s.TargetA });
        await UploadAsync(template, ReportTemplate(ws => ws.Cell("A1").Value = "새 양식 {{InspectionNo}}"));

        var re = await _client.PostAsync($"/api/print/logs/{logId}/reprint", null);
        Assert.True(re.IsSuccessStatusCode, await re.Content.ReadAsStringAsync());
        using var wb = new XLWorkbook(new MemoryStream(await re.Content.ReadAsByteArrayAsync()));
        Assert.Contains("HD-SUB-01", wb.Worksheet("성적서").Cell("A2").GetString());
        Assert.StartsWith("TO-", wb.Worksheet("성적서").Cell("A1").GetString());   // 새 양식("새 양식 …") 아님

        var fresh = await _client.PostAsJsonAsync("/api/print/issue", new { purposeCode = "INSPECTION_REPORT", sourceId = _s.TargetA });
        using var wb2 = new XLWorkbook(new MemoryStream(await fresh.Content.ReadAsByteArrayAsync()));
        Assert.StartsWith("새 양식", wb2.Worksheet("성적서").Cell("A1").GetString());
        Assert.Contains("CHANGED", wb2.Worksheet("성적서").Cell("A2").GetString());
    }

    [Fact]
    public async Task Template_resolution_prefers_part_and_customer_then_part_then_purpose_default()
    {
        byte[] Marked(string mark) => ReportTemplate(ws => ws.Cell("Z1").Value = mark);
        var common = await CreateExcelTemplateAsync("INSPECTION_REPORT", "XLSX", Marked("PART"));
        var specific = await CreateExcelTemplateAsync("INSPECTION_REPORT", "XLSX", Marked("PART+CUSTOMER"));
        await LinkPartAsync(common, null);
        await LinkPartAsync(specific, _s.CustomerId);

        async Task<string> IssueMark(long target)
        {
            var res = await _client.PostAsJsonAsync("/api/print/issue", new { purposeCode = "INSPECTION_REPORT", sourceId = target });
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            using var wb = new XLWorkbook(new MemoryStream(await res.Content.ReadAsByteArrayAsync()));
            return wb.Worksheet("성적서").Cell("Z1").GetString();
        }

        Assert.Equal("PART+CUSTOMER", await IssueMark(_s.TargetA));
        await using (var c = await fx.OpenAsync())
            await c.ExecuteAsync("DELETE FROM part_print_template WHERE print_template_id = @specific", new { specific });
        Assert.Equal("PART", await IssueMark(_s.TargetA));

        // 품목 연결이 없는 대상(B) — 용도 기본 양식이 없으면 422, 지정하면 그 양식
        var none = await _client.PostAsJsonAsync("/api/print/issue", new { purposeCode = "INSPECTION_REPORT", sourceId = _s.TargetB });
        if (none.StatusCode == HttpStatusCode.UnprocessableEntity)
            Assert.Equal("TEMPLATE_NOT_FOUND", (await none.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var fallback = await CreateExcelTemplateAsync("INSPECTION_REPORT", "XLSX", Marked("PURPOSE-DEFAULT"));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsync($"/api/print/templates/{fallback}/default", null)).StatusCode);
        Assert.Equal("PURPOSE-DEFAULT", await IssueMark(_s.TargetB));
    }

    [Fact]
    public async Task Fixed_sales_slip_is_issued_from_default_template()
    {
        var res = await _client.PostAsJsonAsync("/api/print/issue", new { purposeCode = "SHIPMENT_SLIP", sourceId = _s.ShipmentId });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", res.Content.Headers.ContentType?.MediaType);
        var pdf = await res.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        // 품목 7행 / 페이지당 6행 (DDL 초기 옵션) → 2쪽
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?![s\w])").Count);
    }

    [Fact]
    public async Task Shipment_slip_can_be_issued_with_a_chosen_excel_template()
    {
        byte[] slipFile;
        using (var wb = new XLWorkbook())
        {
            wb.AddWorksheet("명세표").Cell("A1").Value = "{{ShipmentNo}}";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            slipFile = ms.ToArray();
        }
        var custom = await CreateExcelTemplateAsync("SHIPMENT_SLIP", "XLSX", slipFile, $"업체전용-{Guid.NewGuid():N}"[..16]);

        // 콤보 목록: 기본 양식(당사 FIXED) 먼저, 등록한 엑셀 양식 포함
        var choices = await _client.GetFromJsonAsync<JsonElement>("/api/shipments/slip-templates");
        Assert.True(choices[0].GetProperty("isDefault").GetBoolean());
        Assert.Contains(choices.EnumerateArray(), c => c.GetProperty("printTemplateId").GetInt64() == custom);

        var res = await _client.PostAsync($"/api/shipments/{_s.ShipmentId}/slip?printTemplateId={custom}", null);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        using (var wb = new XLWorkbook(new MemoryStream(await res.Content.ReadAsByteArrayAsync())))
            Assert.False(string.IsNullOrEmpty(wb.Worksheet("명세표").Cell("A1").GetString()));

        // 다른 용도 양식은 거부
        var other = await CreateExcelTemplateAsync("INSPECTION_REPORT", "XLSX", ReportTemplate());
        var wrong = await _client.PostAsync($"/api/shipments/{_s.ShipmentId}/slip?printTemplateId={other}", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
    }

    [Fact]
    public async Task Fixed_options_save_new_version_and_validate_json()
    {
        await using var c = await fx.OpenAsync();
        var id = await c.ExecuteScalarAsync<long>("SELECT print_template_id FROM print_template WHERE renderer_key = 'SALES_SLIP'");
        var bad = await _client.PostAsJsonAsync($"/api/print/templates/{id}/options", new { layoutOptionsJson = "{oops" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var current = await c.ExecuteScalarAsync<string>("SELECT layout_options_json FROM print_template_version WHERE print_template_id = @id AND is_current = 1", new { id });
        var ok = await _client.PostAsJsonAsync($"/api/print/templates/{id}/options", new { layoutOptionsJson = current, changeNote = "그대로 저장" });
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        Assert.True((await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("versionNo").GetInt32() >= 2);
    }

    [Fact]
    public async Task Sample_template_passes_its_own_validation()
    {
        var res = await _client.GetAsync("/api/print/purposes/INSPECTION_REPORT/sample-template");
        Assert.True(res.IsSuccessStatusCode);
        var sample = await res.Content.ReadAsByteArrayAsync();
        var create = await _client.PostAsJsonAsync("/api/print/templates", new { purposeCode = "INSPECTION_REPORT", printTemplateName = $"샘플-{Guid.NewGuid():N}"[..20] });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printTemplateId").GetInt64();
        var upload = await UploadAsync(id, sample);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());
        Assert.Empty((await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unknown").EnumerateArray());
    }

    [LibreOfficeFact]
    public async Task Excel_template_is_converted_to_pdf_on_server_and_report_is_kept()
    {
        var template = await CreateExcelTemplateAsync("INSPECTION_REPORT", "PDF", ReportTemplate());
        await LinkPartAsync(template, _s.CustomerId);
        var res = await _client.PostAsJsonAsync("/api/print/issue", new { purposeCode = "INSPECTION_REPORT", sourceId = _s.TargetA });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var pdf = await res.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));

        // 성적서는 발행본 보관 용도 (설정 print.keep_issued_output)
        var logId = long.Parse(res.Headers.GetValues("X-Print-Log-Id").Single());
        var kept = await _client.GetAsync($"/api/print/logs/{logId}/file");
        Assert.Equal(pdf, await kept.Content.ReadAsByteArrayAsync());
    }
}
