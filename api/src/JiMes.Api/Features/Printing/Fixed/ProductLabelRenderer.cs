using System.Text.Json.Nodes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>
/// 제품표시 라벨 (구 PrintDoc/ProductLabel, F_IncomeAddForm·F_IncomeForm) — 65×80mm 라벨 1건 = 1장.
/// 제목 / 항목명·값 14행 (값은 한 줄, 넘치면 …) / 바코드. 크기·항목·글꼴 = layout_options_json (§15.3.1 PRODUCT_LABEL).
/// </summary>
public sealed class ProductLabelRenderer : IFixedRenderer
{
    public string RendererKey => "PRODUCT_LABEL";

    private static readonly (string Key, string Title)[] DefaultFields =
    [
        ("sales_order_no", "수 주 번 호"), ("customer_name", "거  래  처"), ("part_name", "품       명"), ("model", "기       종"),
        ("material", "재       질"), ("qty", "수       량"), ("weight", "중       량"), ("unit_weight", "단       중"),
        ("hardness", "요 구 시 험"), ("core_hardness", "심 부 경 도"), ("case_depth", "경  화  층"), ("heat_process", "공       정"),
        ("customer_lot", "고 객 로 트"), ("coil_no", "코 일 번 호"),
    ];

    public void Compose(IDocumentContainer doc, PrintData data, LayoutOptions o)
    {
        var fonts = PdfFonts.Pick(o.Strings("font.family", "맑은 고딕", "Malgun Gothic"));
        var fields = o.Array("fields") is { } a
            ? a.OfType<JsonObject>().Select(f => (f["key"]?.GetValue<string>() ?? "", f["title"]?.GetValue<string>() ?? "")).ToList()
            : [.. DefaultFields];
        var barcodeKey = o.Str("barcode.content", "sales_order_no");

        doc.Page(page =>
        {
            page.Size(o.Num("page.width_mm", 65), o.Num("page.height_mm", 80), Unit.Millimetre);
            page.Margin(o.Num("page.margin_mm", 5), Unit.Millimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontFamily(fonts).FontSize(o.Num("font.value", 8)));
            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Text(o.Str("title", "제  품  표  시")).FontSize(o.Num("font.title", 14)).Bold();
                col.Item().Border(1).Table(table =>
                {
                    table.ColumnsDefinition(c => { c.ConstantColumn(o.Num("label_column_width_mm", 22), Unit.Millimetre); c.RelativeColumn(); });
                    foreach (var (key, title) in fields)
                    {
                        // 수주번호는 길어서 조금 작게 (구 INCNO_FONT_SIZE)
                        var size = key == barcodeKey ? o.Num("font.order_no", 7) : o.Num("font.value", 8);
                        table.Cell().BorderRight(1).BorderBottom(0.5f).PaddingHorizontal(1).MinHeight(2.5f, Unit.Millimetre).AlignMiddle()
                            .Text(title).FontSize(o.Num("font.label", 7));
                        table.Cell().BorderBottom(0.5f).PaddingHorizontal(2).MinHeight(2.5f, Unit.Millimetre).AlignMiddle()
                            .Text(t => { t.ClampLines(1, "…"); t.Span(ProcessSheetRenderer.Format(key, data.Values)).FontSize(size).Bold(); });
                    }
                });
                col.Item().AlignCenter().Height(10, Unit.Millimetre)
                    .Svg(Barcodes.Svg(LayoutValues.Text(LayoutValues.Get(data.Values, barcodeKey)), o.Str("barcode.format", "CODE_128"),
                        o.Int("barcode.width", 300), o.Int("barcode.height", 60)));
            });
        });
    }
}
