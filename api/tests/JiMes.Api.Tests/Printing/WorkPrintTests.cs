using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;

namespace JiMes.Api.Tests.Printing;

/// <summary>작업일보·작업표준서 (설계 §29.2) + 엑셀 양식 여러 건 = 시트 모음</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkPrintTests(ApiFixture fx)
{
    private sealed record Seed(long Work1, long Work2, long StandardVersion);

    private static int PageCount(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?![s\w])").Count;

    private async Task<Seed> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var c = await fx.OpenAsync();
        async Task<long> Insert(string sql, object? args = null) => await c.ExecuteScalarAsync<long>(sql + "; SELECT LAST_INSERT_ID();", args);
        var customer = await Insert("INSERT INTO customer (customer_code, customer_name) VALUES (@code, '한독기어')", new { code = $"C-{tag}" });
        var part = await Insert("INSERT INTO part (part_code, part_name, material, hardness) VALUES (@code, '헬리컬 기어', 'SCM420', 'HRC 58~62')", new { code = $"P-{tag}" });
        var up = await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@code, '침탄')", new { code = $"U-{tag}" });
        var temp = await Insert("INSERT INTO condition_item (condition_item_code, condition_item_name, unit_code) VALUES (@code, '온도', '℃')", new { code = $"T-{tag}" });
        var time = await Insert("INSERT INTO condition_item (condition_item_code, condition_item_name, unit_code) VALUES (@code, '시간', 'min')", new { code = $"M-{tag}" });
        var std = await Insert("INSERT INTO standard (standard_code, standard_name, part_id, unit_process_id) VALUES (@code, '헬리컬 침탄', @part, @up)", new { code = $"WS-{tag}", part, up });
        var version = await Insert("INSERT INTO standard_version (standard_id, version_no, charge_qty, running_time_min, effective_from, is_current) VALUES (@std, 1, 500, 570, '2026-01-01', 1)", new { std });
        await c.ExecuteAsync("INSERT INTO standard_version_step (standard_version_id, sequence_no, step_name) VALUES (@version, 1, '승온'), (@version, 2, '침탄')", new { version });
        await c.ExecuteAsync("INSERT INTO standard_version_item (standard_version_id, sequence_no, condition_item_id) VALUES (@version, 1, @temp), (@version, 2, @time)", new { version, temp, time });
        await c.ExecuteAsync(
            "INSERT INTO standard_condition (standard_version_id, step_no, condition_item_id, condition_value) VALUES (@version, 1, @temp, '880'), (@version, 2, @temp, '930'), (@version, 2, @time, '240')",
            new { version, temp, time });
        var order = await Insert("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@no, CURDATE(), @customer)", new { no = $"SO-{tag}", customer });
        var item = await Insert("INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, order_qty) VALUES (@no, @order, 1, @part, 400)", new { no = $"I-{tag}", order, part });
        var works = new List<long>();
        for (var n = 1; n <= 2; n++)
        {
            var work = await Insert(
                "INSERT INTO production_work (lot_no, unit_process_id, work_date, status, standard_version_id, is_standard_fixed, remark) VALUES (@lot, @up, '2026-09-30', 'COMPLETED', @version, 1, '노 내 점검')",
                new { lot = $"L-{tag}-{n}", up, version });
            var input = await Insert("INSERT INTO production_work_input (production_work_id, sales_order_item_id, input_qty, tray_mark, is_standard_basis) VALUES (@work, @item, 200, 'T1', 1)", new { work, item });
            await c.ExecuteAsync(
                "INSERT INTO production_work_condition (production_work_id, condition_item_id, item_sequence_no, step_sequence_no, step_name_snapshot, set_value) VALUES (@work, @temp, 1, 1, '승온', '880'), (@work, @temp, 1, 2, '침탄', '935')",
                new { work, temp });
            await c.ExecuteAsync("INSERT INTO defect_occurrence (sales_order_item_id, production_work_id, production_work_input_id, defect_date, defect_qty, remark) VALUES (@item, @work, @input, '2026-09-30', 3, '찍힘')",
                new { item, work, input });
            works.Add(work);
        }
        return new Seed(works[0], works[1], version);
    }

    [Fact]
    public async Task Work_daily_and_standard_sheet_render()
    {
        var client = await fx.LoginAdminAsync();
        var s = await SeedAsync();

        var res = await client.PostAsJsonAsync("/api/print/documents", new { purposeCode = "WORK_DAILY", sourceIds = new[] { s.Work1, s.Work2 } });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        Assert.Equal(2, PageCount(await res.Content.ReadAsByteArrayAsync()));

        res = await client.PostAsJsonAsync("/api/print/documents", new { purposeCode = "WORK_STANDARD", sourceIds = new[] { s.StandardVersion } });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        Assert.Equal(1, PageCount(await res.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task Excel_template_for_many_lots_collects_sheets()
    {
        var client = await fx.LoginAdminAsync();
        var s = await SeedAsync();
        byte[] file;
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("라벨");
            ws.Cell("A1").Value = "{{LotNo}}";
            ws.Cell("A2").Value = "{{#Conditions}}{{Item}}";
            ws.Cell("B2").Value = "{{S2}}{{/Conditions}}";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            file = ms.ToArray();
        }
        var created = await client.PostAsJsonAsync("/api/print/templates", new { purposeCode = "LOT_LABEL", printTemplateName = $"라벨-{Guid.NewGuid():N}"[..16], outputFormat = "XLSX" });
        var templateId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printTemplateId").GetInt64();
        var form = new MultipartFormDataContent { { new ByteArrayContent(file), "file", "label.xlsx" } };
        Assert.True((await client.PostAsync($"/api/print/templates/{templateId}/versions", form)).IsSuccessStatusCode);

        var res = await client.PostAsJsonAsync("/api/print/documents", new { purposeCode = "LOT_LABEL", sourceIds = new[] { s.Work1, s.Work2 }, printTemplateId = templateId });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        using var book = new XLWorkbook(new MemoryStream(await res.Content.ReadAsByteArrayAsync()));
        Assert.Equal(2, book.Worksheets.Count);
        Assert.EndsWith("-1", book.Worksheet(1).Cell("A1").GetString());
        Assert.EndsWith("-2", book.Worksheet(2).Cell("A1").GetString());
        Assert.Equal("온도", book.Worksheet(2).Cell("A2").GetString());
        Assert.Equal("935", book.Worksheet(2).Cell("B2").GetString());
    }
}
