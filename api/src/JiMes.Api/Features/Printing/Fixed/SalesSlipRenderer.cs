using System.Globalization;
using System.Text.Json.Nodes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>
/// 거래명세표 (구 PrintDoc/OutputSheet + OutputComSheet + MultiOutputSheet → 렌더러 1개, §15.3 F5).
/// 레이아웃 수치는 layout_options_json (초기값 = 구 const, §15.3.1), 없으면 아래 기본값.
/// 금액은 전표 저장값만 표시 (F2 — 세율 계산 없음). 일자는 출하일 (구 코드는 인쇄 시각을 찍었음).
/// </summary>
public sealed class SalesSlipRenderer : IFixedRenderer
{
    public string RendererKey => "SALES_SLIP";

    private sealed class Options(JsonNode? root)
    {
        private readonly JsonNode? _root = root;

        public float Num(string path, float fallback) => Node(path) is JsonValue v && v.TryGetValue(out double d) ? (float)d : fallback;
        public int Int(string path, int fallback) => (int)Num(path, fallback);
        public bool Bool(string path, bool fallback) => Node(path) is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
        public string Str(string path, string fallback) => Node(path) is JsonValue v && v.TryGetValue(out string? s) && s is not null ? s : fallback;

        public JsonArray? Array(string path) => Node(path) as JsonArray;

        /// <summary>문자열 하나 또는 문자열 목록</summary>
        public IReadOnlyList<string> Strings(string path, params string[] fallback) => Node(path) switch
        {
            JsonValue v when v.TryGetValue(out string? s) && s is not null => [s],
            JsonArray a => a.Select(x => x?.GetValue<string>()).OfType<string>().ToList() is { Count: > 0 } list ? list : fallback,
            _ => fallback,
        };

        private JsonNode? Node(string path)
        {
            var node = _root;
            foreach (var part in path.Split('.'))
                node = node is JsonObject o && o.TryGetPropertyValue(part, out var next) ? next : null;
            return node;
        }
    }

    private sealed record Copy(string Label, string BorderColor);

    private sealed record Column(string Key, string Title, float? Width, float Relative, string? Format);

    // 옵션이 없을 때의 품목 열 (구 BuildItemsTable 과 같은 순서·너비)
    private static readonly Column[] DefaultColumns =
    [
        new("No", "No", 20, 0, null), new("PartName", "품목", null, 1, null), new("Model", "기종", 80, 0, null),
        new("ProcessName", "공정", 60, 0, null), new("Qty", "수량", 40, 0, "#,##0"), new("Weight", "중량", 50, 0, "#,##0.0"),
        new("PriceUnit", "단위", 30, 0, null), new("UnitPrice", "단가", 40, 0, "#,##0"), new("Amount", "금액", 75, 0, "#,##0"),
        new("Vat", "세액", 50, 0, "#,##0"),
    ];

    public byte[] Render(PrintData data, string? optionsJson)
    {
        var o = new Options(string.IsNullOrWhiteSpace(optionsJson) ? null : JsonNode.Parse(optionsJson));
        var perPage = Math.Max(o.Int("items_per_page", 6), 1);
        var items = data.Lists.GetValueOrDefault("Items") ?? [];
        var pages = items.Count == 0 ? [[]] : items.Chunk(perPage).Select(c => c.ToList()).ToList();
        var copies = (o.Array("copies")?.OfType<JsonObject>()
                .Select(c => new Copy(c["label"]?.GetValue<string>() ?? "", c["border_color"]?.GetValue<string>() ?? "#000000"))
                .ToList() is { Count: > 0 } list ? list : [new Copy("(공급자 보관용)", "#000000"), new Copy("(거래처 보관용)", "#E53935")]);
        var columns = ReadColumns(o);
        var stamp = o.Bool("stamp.show", true) ? data.Images.GetValueOrDefault("Stamp") : null;
        var fonts = PdfFonts.Pick(o.Strings("font.family", "굴림체", "GulimChe", "맑은 고딕"));

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(o.Str("page.orientation", "PORTRAIT") == "LANDSCAPE" ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(o.Num("page.margin", 20));
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(o.Num("font.default", 11)).FontFamily(fonts));

                page.Content().Column(column =>
                {
                    for (var p = 0; p < pages.Count; p++)
                    {
                        var pageIndex = p + 1;
                        var chunk = pages[p];
                        column.Item().Layers(layers =>
                        {
                            layers.PrimaryLayer().Column(col =>
                            {
                                col.Item().Height(o.Num("spacing.top", 10));
                                for (var c = 0; c < copies.Count; c++)
                                {
                                    if (c > 0)
                                    {
                                        col.Item().Height(o.Num("spacing.cut_line_top", 15));
                                        col.Item().LineHorizontal(o.Num("border.divider", 1)).LineColor(Colors.Grey.Medium);
                                        col.Item().Height(o.Num("spacing.cut_line_bottom", 20));
                                    }
                                    var copy = copies[c];
                                    col.Item().Element(e => Slip(e, o, data, chunk, columns, copy, pageIndex, pages.Count, perPage));
                                }
                            });
                            if (stamp is not null)
                            {
                                for (var c = 0; c < Math.Min(copies.Count, 2); c++)
                                {
                                    var y = c == 0 ? o.Num("stamp.top_y", 65) : o.Num("stamp.bottom_y", 480);
                                    layers.Layer().AlignRight().AlignTop()
                                        .OffsetX(o.Num("stamp.offset_x", -250)).OffsetY(y)
                                        .Width(o.Num("stamp.size", 50)).Height(o.Num("stamp.size", 50))
                                        .Image(stamp);
                                }
                            }
                        });
                        if (p < pages.Count - 1)
                            column.Item().PageBreak();
                    }
                });
            });
        }).GeneratePdf();
    }

    private static Column[] ReadColumns(Options o)
    {
        var array = o.Array("columns");
        if (array is null) return DefaultColumns;
        return array.OfType<JsonObject>()
            .Where(c => c["visible"]?.GetValue<bool>() ?? true)
            .Select(c => new Column(
                c["key"]?.GetValue<string>() ?? "",
                c["title"]?.GetValue<string>() ?? "",
                c["width"] is JsonValue w && w.TryGetValue(out double wd) ? (float)wd : null,
                c["relative"] is JsonValue r && r.TryGetValue(out double rd) ? (float)rd : 1,
                c["format"]?.GetValue<string>()))
            .ToArray();
    }

    private static void Slip(
        IContainer container, Options o, PrintData data, List<Dictionary<string, object?>> items, Column[] columns,
        Copy copy, int pageIndex, int pageCount, int perPage)
    {
        var border = o.Num("border.cell", 0.5f);
        var background = o.Str("background_color", "#EEEEEE");
        IContainer Cell(IContainer c, float minHeight, bool shaded = false) =>
            (shaded ? c.Border(border).BorderColor(copy.BorderColor).Background(background) : c.Border(border).BorderColor(copy.BorderColor))
                .MinHeight(minHeight).Padding(4).AlignMiddle();
        string V(string key) => Text(data.Values.GetValueOrDefault(key), null);

        container.Column(column =>
        {
            // 헤더: 전표번호·일자 | 제목 | 보관 구분
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c => { c.ConstantColumn(60); c.ConstantColumn(140); c.ConstantColumn(140); c.RelativeColumn(); });
                var h = o.Num("row_height.header", 15);
                table.Cell().Element(c => Cell(c, h)).Text("전표번호").FontSize(o.Num("font.header_label", 9));
                table.Cell().Element(c => Cell(c, h)).Text(V("ShipmentNo")).FontSize(o.Num("font.header_value", 9));
                table.Cell().RowSpan(2).Element(c => Cell(c, h)).AlignCenter()
                    .Text(o.Str("title", "거 래 명 세 표")).FontSize(o.Num("font.title", 15)).SemiBold();
                table.Cell().Element(c => Cell(c, h)).Text("");
                table.Cell().Element(c => Cell(c, h)).Text("일자").FontSize(o.Num("font.header_label", 9));
                table.Cell().Element(c => Cell(c, h)).Text(V("ShipmentDate")).FontSize(o.Num("font.header_value", 9));
                table.Cell().Element(c => Cell(c, h)).AlignRight().Text(copy.Label).FontSize(o.Num("font.storage_type", 9));
            });
            column.Item().Height(o.Num("spacing.header_to_company", 4));

            // 공급자 | 공급받는자
            column.Item().DefaultTextStyle(x => x.FontSize(o.Num("font.company_label", 7))).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    for (var side = 0; side < 2; side++)
                    {
                        c.ConstantColumn(15); c.ConstantColumn(25); c.ConstantColumn(100); c.ConstantColumn(15);
                        c.RelativeColumn(); c.ConstantColumn(15); c.ConstantColumn(80);
                    }
                });
                var h = o.Num("row_height.company", 12);
                var valueSize = o.Num("font.company_value", 9);
                string[] prefixes = ["Supplier", "Customer"];
                string[] sideLabels = ["공\n\n급\n\n자", "공\n급\n받\n는\n자"];

                void Label(string text) => table.Cell().Element(c => Cell(c, h, shaded: true)).AlignCenter().Text(text);
                void Value(string key, uint span) => table.Cell().ColumnSpan(span).Element(c => Cell(c, h)).Text(V(key)).FontSize(valueSize);

                for (var side = 0; side < 2; side++)
                {
                    table.Cell().RowSpan(4).Element(c => Cell(c, h, shaded: true)).AlignCenter().Text(sideLabels[side]);
                    Label("등록\n번호"); Value($"{prefixes[side]}BusinessNo", 5);
                }
                for (var side = 0; side < 2; side++)
                {
                    Label("상호"); Value(side == 0 ? "SupplierName" : "CustomerName", 3);
                    Label("성명"); Value($"{prefixes[side]}CeoName", 1);
                }
                for (var side = 0; side < 2; side++)
                {
                    Label("주소"); Value($"{prefixes[side]}Address", 5);
                }
                for (var side = 0; side < 2; side++)
                {
                    Label("업태"); Value($"{prefixes[side]}BusinessType", 1);
                    Label("종목"); Value($"{prefixes[side]}BusinessItem", 3);
                }
            });
            column.Item().Height(o.Num("spacing.company_to_items", 4));

            // 품목 (빈 행으로 페이지당 행 수 채움)
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    foreach (var col in columns)
                    {
                        if (col.Width is { } w) c.ConstantColumn(w);
                        else c.RelativeColumn(col.Relative);
                    }
                });
                table.Header(header =>
                {
                    foreach (var col in columns)
                        header.Cell().Element(c => Cell(c, o.Num("row_height.item_header", 14), shaded: true)).AlignCenter()
                            .Text(col.Title).FontSize(o.Num("font.item_header", 9)).SemiBold();
                });
                var rowHeight = o.Num("row_height.item", 30);
                for (var i = 0; i < perPage; i++)
                {
                    var item = i < items.Count ? items[i] : null;
                    foreach (var col in columns)
                        table.Cell().Element(c => Cell(c, rowHeight)).AlignCenter()
                            .Text(item is null ? " " : Text(item.GetValueOrDefault(col.Key), col.Format)).FontSize(o.Num("font.item", 9));
                }
            });

            // 합계 — 마지막 페이지에만 금액 (구 동일)
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    foreach (var w in new[] { 40f, 40f, 40f, 50f, 50f }) { c.ConstantColumn(w); c.RelativeColumn(); }
                });
                var last = pageIndex == pageCount;
                var rowHeight = o.Num("row_height.item", 30);
                void Pair(string label, string? key)
                {
                    table.Cell().Element(c => Cell(c, rowHeight)).AlignCenter().Text(label).FontSize(o.Num("font.summary_label", 9));
                    table.Cell().Element(c => Cell(c, rowHeight)).AlignCenter()
                        .Text(last && key is not null ? Text(data.Values.GetValueOrDefault(key), "#,##0") : "").FontSize(o.Num("font.summary_value", 9));
                }
                Pair("공급\n가액", "SupplyAmount");
                Pair("세액", "VatAmount");
                Pair("합계", "TotalAmount");
                Pair("미수금", null);
                Pair("인수자", null);
            });

            column.Item().AlignRight().Text($"{pageIndex} / {pageCount}").FontSize(o.Num("font.page_no", 9));
        });
    }

    private static string Text(object? value, string? format) => value switch
    {
        null => "",
        decimal d => d.ToString(format ?? "#,##0.###", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
