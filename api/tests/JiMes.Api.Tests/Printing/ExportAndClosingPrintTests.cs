using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;

namespace JiMes.Api.Tests.Printing;

/// <summary>목록 내보내기 (엑셀·CSV·PDF) · 마감내역서 엑셀 양식 · 출력 양식 용도 정리 (설계 §29.3)</summary>
[Collection(ApiCollection.Name)]
public sealed class ExportAndClosingPrintTests(ApiFixture fx)
{
    private static object Body(string format) => new
    {
        title = "거래처 목록",
        format,
        columns = new object[] { new { title = "코드" }, new { title = "이름" }, new { title = "금액", number = true } },
        rows = new object?[][] { ["C001", "한독, \"기어\"", 1234567], ["C002", "샘플", 0.5m], ["C003", null, null] },
    };

    [Fact]
    public async Task Export_xlsx_csv_pdf()
    {
        var client = await fx.LoginAdminAsync();

        var xlsx = await client.PostAsJsonAsync("/api/export", Body("XLSX"));
        Assert.True(xlsx.IsSuccessStatusCode, await xlsx.Content.ReadAsStringAsync());
        using (var wb = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync())))
        {
            var ws = wb.Worksheet(1);
            Assert.Equal("이름", ws.Cell(1, 2).GetString());
            Assert.Equal(1234567m, ws.Cell(2, 3).GetValue<decimal>());   // 숫자 셀
            Assert.Equal("한독, \"기어\"", ws.Cell(2, 2).GetString());
        }

        var csv = await (await client.PostAsJsonAsync("/api/export", Body("CSV"))).Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], csv[..3]);   // UTF-8 BOM
        var text = Encoding.UTF8.GetString(csv[3..]);
        Assert.Contains("C001,\"한독, \"\"기어\"\"\",\"1,234,567\"", text);

        var pdf = await client.PostAsJsonAsync("/api/export", Body("PDF"));
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/export", new { title = "x", columns = Array.Empty<object>(), rows = Array.Empty<object>() })).StatusCode);
    }

    [Fact]
    public async Task Closing_report_excel_and_unused_progress_sheet_is_hidden()
    {
        var client = await fx.LoginAdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        long closing;
        await using (var c = await fx.OpenAsync())
        {
            var customer = await c.ExecuteScalarAsync<long>("INSERT INTO customer (customer_code, customer_name) VALUES (@code, '한독기어'); SELECT LAST_INSERT_ID();", new { code = $"C-{tag}" });
            closing = await c.ExecuteScalarAsync<long>(
                "INSERT INTO shipment_closing (closing_no, customer_id, closing_date, closing_year, closing_month, total_amount, closing_status) VALUES (@no, @customer, '2026-09-30', 2026, 9, 3000, 'CLOSED'); SELECT LAST_INSERT_ID();",
                new { no = $"CL-{tag}", customer });
            foreach (var n in new[] { 1, 2 })
                await c.ExecuteAsync(
                    """
                    INSERT INTO shipment (shipment_no, shipment_date, customer_id, shipment_closing_id, closing_status, closing_year, closing_month, supply_amount, vat_amount, total_amount)
                    VALUES (@no, CONCAT('2026-09-1', @n), @customer, @closing, 'CLOSED', 2026, 9, 1000, 100, 1100)
                    """, new { no = $"O-{tag}-{n}", n, customer, closing });
        }
        byte[] file;
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("마감");
            ws.Cell("A1").Value = "{{ClosingNo}} {{CustomerName}}";
            ws.Cell("A2").Value = "{{#Slips}}{{ShipmentNo}}";
            ws.Cell("B2").Value = "{{TotalAmount}}{{/Slips}}";
            ws.Cell("A3").Value = "{{TotalAmount}}";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            file = ms.ToArray();
        }
        var created = await client.PostAsJsonAsync("/api/print/templates", new { purposeCode = "CLOSING_REPORT", printTemplateName = $"마감-{tag}", outputFormat = "XLSX" });
        var templateId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printTemplateId").GetInt64();
        Assert.True((await client.PostAsync($"/api/print/templates/{templateId}/versions",
            new MultipartFormDataContent { { new ByteArrayContent(file), "file", "closing.xlsx" } })).IsSuccessStatusCode);

        var res = await client.PostAsJsonAsync("/api/print/documents", new { purposeCode = "CLOSING_REPORT", sourceIds = new[] { closing }, printTemplateId = templateId });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        using (var book = new XLWorkbook(new MemoryStream(await res.Content.ReadAsByteArrayAsync())))
        {
            var ws = book.Worksheet(1);
            Assert.Equal($"CL-{tag} 한독기어", ws.Cell("A1").GetString());
            Assert.Equal($"O-{tag}-2", ws.Cell("A3").GetString());   // 전표 2행
            Assert.Equal(2200m, ws.Cell("A4").GetValue<decimal>());
        }

        // 구 화면에서 쓰지 않던 작업 진행 현황표는 용도 목록에서 빠짐
        var purposes = await client.GetFromJsonAsync<JsonElement>("/api/print/purposes");
        Assert.DoesNotContain(purposes.EnumerateArray(), p => p.GetProperty("purposeCode").GetString() == "PROGRESS_SHEET");
        Assert.All(purposes.EnumerateArray(), p => Assert.True(p.GetProperty("dataSourceImplemented").GetBoolean(), p.GetProperty("purposeCode").GetString()));
    }
}
