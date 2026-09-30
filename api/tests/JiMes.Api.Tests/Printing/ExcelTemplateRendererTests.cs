using ClosedXML.Excel;
using JiMes.Api.Features.Printing;

namespace JiMes.Api.Tests.Printing;

public sealed class ExcelTemplateRendererTests
{
    internal static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    internal static FieldDictionary Fields(bool legacy = true) => new(
    [
        new PrintFieldRow { FieldKey = "InspectionNo", FieldAlias = "검사번호" },
        new PrintFieldRow { FieldKey = "ChargeQt", FieldType = "NUMBER", FormatPattern = "#,##0" },
        new PrintFieldRow { FieldKey = "PartName", FieldAlias = "품명" },
        new PrintFieldRow { FieldKey = "Measurements", FieldAlias = "측정", FieldType = "LIST" },
        new PrintFieldRow { FieldKey = "Measurements.Item" },
        new PrintFieldRow { FieldKey = "Measurements.P1" },
        new PrintFieldRow { FieldKey = "HardnessChart", FieldAlias = "경화층차트", FieldType = "IMAGE" },
    ], legacy);

    private static byte[] Template(Action<XLWorkbook> build)
    {
        using var wb = new XLWorkbook();
        build(wb);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static XLWorkbook Open(byte[] bytes) => new(new MemoryStream(bytes));

    private static PrintData Data(int measurements)
    {
        var data = new PrintData();
        data.Values["InspectionNo"] = "TO260930-001";
        data.Values["ChargeQt"] = 1234m;
        data.Values["PartName"] = "기어";
        data.Values["T1_1_P1"] = "60.1";
        data.Lists["Measurements"] = Enumerable.Range(1, measurements).Select(i =>
        {
            var row = PrintData.Row();
            row["Item"] = $"항목{i}";
            row["P1"] = 58m + i;
            return row;
        }).ToList();
        data.Images["HardnessChart"] = Png1x1;
        return data;
    }

    [Fact]
    public void Scalars_aliases_and_numbers_are_filled_in_every_sheet()
    {
        var template = Template(wb =>
        {
            var a = wb.AddWorksheet("A");
            a.Cell("A1").Value = "{{InspectionNo}}";
            a.Cell("A2").Value = "품명: {{품명}} / 수량 {{ChargeQt}}";
            a.Cell("A3").Value = "{{ChargeQt}}";
            a.Cell("A3").Style.NumberFormat.Format = "#,##0";
            wb.AddWorksheet("B").Cell("B2").Value = "{{검사번호}}";
        });

        using var wb = Open(ExcelTemplateRenderer.Render(template, Data(0), Fields()));
        Assert.Equal("TO260930-001", wb.Worksheet("A").Cell("A1").GetString());
        Assert.Equal("품명: 기어 / 수량 1,234", wb.Worksheet("A").Cell("A2").GetString());
        Assert.Equal(1234d, wb.Worksheet("A").Cell("A3").GetDouble());   // 숫자 셀 유지 → 양식 서식 적용
        Assert.Equal("TO260930-001", wb.Worksheet("B").Cell("B2").GetString());
    }

    [Fact]
    public void List_block_rows_are_copied_per_item_with_styles_and_following_rows_shift()
    {
        var template = Template(wb =>
        {
            var ws = wb.AddWorksheet("S");
            ws.Cell("A1").Value = "항목";
            ws.Cell("A2").Value = "{{#Measurements}}{{Item}}";
            ws.Cell("B2").Value = "{{Measurements.P1}}{{/Measurements}}";
            ws.Cell("B2").Style.Font.Bold = true;
            ws.Cell("A3").Value = "합계 아래 {{InspectionNo}}";
        });

        using var wb = Open(ExcelTemplateRenderer.Render(template, Data(3), Fields()));
        var ws = wb.Worksheet("S");
        Assert.Equal(["항목1", "항목2", "항목3"], [ws.Cell("A2").GetString(), ws.Cell("A3").GetString(), ws.Cell("A4").GetString()]);
        Assert.Equal(61d, ws.Cell("B4").GetDouble());
        Assert.True(ws.Cell("B4").Style.Font.Bold);   // 복사한 행도 서식 유지
        Assert.Equal("합계 아래 TO260930-001", ws.Cell("A5").GetString());
    }

    [Fact]
    public void Empty_list_removes_block_rows()
    {
        var template = Template(wb =>
        {
            var ws = wb.AddWorksheet("S");
            ws.Cell("A1").Value = "{{#측정}}{{Item}}{{/측정}}";   // 별칭 목록명
            ws.Cell("A2").Value = "다음";
        });
        using var wb = Open(ExcelTemplateRenderer.Render(template, Data(0), Fields()));
        Assert.Equal("다음", wb.Worksheet("S").Cell("A1").GetString());
    }

    [Fact]
    public void Images_go_into_merged_area_on_every_sheet()
    {
        var template = Template(wb =>
        {
            var a = wb.AddWorksheet("A");
            a.Cell("B2").Value = "{{HardnessChart}}";
            a.Range("B2:D6").Merge();
            wb.AddWorksheet("B").Cell("A1").Value = "{{경화층차트}}";
        });
        using var wb = Open(ExcelTemplateRenderer.Render(template, Data(0), Fields()));
        Assert.Single(wb.Worksheet("A").Pictures);
        Assert.Single(wb.Worksheet("B").Pictures);   // 구 코드는 첫 시트·첫 셀만 (P7)
        Assert.Equal("", wb.Worksheet("A").Cell("B2").GetString());
    }

    [Fact]
    public void Legacy_grid_keys_fill_or_blank_and_unknown_keys_stay_visible()
    {
        var template = Template(wb =>
        {
            var ws = wb.AddWorksheet("S");
            ws.Cell("A1").Value = "{{T1_1_P1}}";
            ws.Cell("A2").Value = "{{T1_9_P1}}";   // 데이터 없는 행 → 빈칸
            ws.Cell("A3").Value = "{{Unknown}}";
        });
        using var wb = Open(ExcelTemplateRenderer.Render(template, Data(0), Fields()));
        var ws2 = wb.Worksheet("S");
        Assert.Equal("60.1", ws2.Cell("A1").GetString());
        Assert.Equal("", ws2.Cell("A2").GetString());
        Assert.Equal("{{Unknown}}", ws2.Cell("A3").GetString());
    }

    [Fact]
    public void Check_reports_unknown_placeholders_and_unbalanced_blocks()
    {
        var template = Template(wb =>
        {
            var ws = wb.AddWorksheet("S");
            ws.Cell("A1").Value = "{{InspectionNo}} {{Nope}} {{T2_3_Spec}}";
            ws.Cell("A2").Value = "{{#Measurements}}{{Item}} {{Bad}}";
            ws.Cell("A3").Value = "{{/Measurements}}";
            ws.Cell("A4").Value = "{{#PartName}}";
        });
        using var wb = Open(template);
        var check = Placeholders.Check(Placeholders.Scan(wb), Fields());
        Assert.Equal(["Nope", "Bad"], check.Unknown);
        Assert.Equal(2, check.Errors.Count);   // PartName 은 목록이 아니고 닫힘도 없음

        var noLegacy = Placeholders.Check(Placeholders.Scan(wb), Fields(legacy: false));
        Assert.Contains("T2_3_Spec", noLegacy.Unknown);
    }

    [Fact]
    public void Snapshot_round_trips_values_and_lists()
    {
        var data = Data(2);
        var back = PrintData.FromSnapshotJson(data.ToSnapshotJson());
        Assert.Equal(1234m, back.Values["ChargeQt"]);
        Assert.Equal("기어", back.Values["partname"]);   // 대소문자 무시
        Assert.Equal(60m, back.Lists["Measurements"][1]["P1"]);
        Assert.Empty(back.Images);
    }
}
