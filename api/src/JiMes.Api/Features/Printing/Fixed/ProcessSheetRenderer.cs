using System.Globalization;
using System.Text.Json.Nodes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>
/// 공정이동표 (구 PrintDoc/ProcessSheet, F_IncomeAddForm·F_IncomeForm) — 입고 행 1건 = 1장, 여러 건은 이어서.
/// 구성 (구와 같음): 제목 + 바코드 / 보안품·기종·우선 / 좌측 정보 16행 × 공정 기록 16행 (작업 칸은 현장 수기) / 특기사항 + 바코드.
/// 수치·문구 = layout_options_json (§15.3.1 PROCESS_SHEET), 우선순위 표시명·색 = 공통코드 PRIORITY (공급원이 채움).
/// </summary>
public sealed class ProcessSheetRenderer : IFixedRenderer
{
    public string RendererKey => "PROCESS_SHEET";

    // 좌측 정보 — 구 LeftLabels 순서. 옵션 left_labels 는 문자열(같은 순번 값) 또는 {key, title}
    private static readonly (string Key, string Title)[] DefaultLeft =
    [
        ("CustomerName", "거래처"), ("PartName", "품명"), ("Specification", "규격"), ("Model", "기종"), ("Material", "재질"),
        ("Qty", "수량"), ("Weight", "중량"), ("UnitWeight", "단중"), ("Hardness", "요구경도"), ("CoreHardness", "심부경도"),
        ("CaseDepth", "경화층"), ("Texture", "조직"), ("HeatProcess", "공정"), ("CustomerLot", "고객로트"), ("CoilNo", "코일번호"), ("", ""),
    ];

    private static readonly LayoutColumn[] DefaultColumns =
    [
        new("label", "", 70, 0), new("value", "", 100, 0), new("seq", "작업\n순서", 30, 0), new("unit_process", "공정명", 50, 0),
        new("lot_no", "작업로트\n(작업일/검사일)", 80, 0), new("tray", "T.NO", 40, 0), new("qty", "작업수량\n(합/불)", 60, 0),
        new("worker", "작업자\n(검사자)", 80, 0), new("remark", "비고", null, 1),
    ];

    public void Compose(IDocumentContainer doc, PrintData data, LayoutOptions o)
    {
        var fonts = PdfFonts.Pick(o.Strings("font.family", "맑은 고딕", "Malgun Gothic"));
        var v = data.Values;
        var barcodeKey = o.Str("barcode.content", "sales_order_no");
        string Barcode(int w, int h) => Barcodes.Svg(LayoutValues.Text(LayoutValues.Get(v, barcodeKey)), o.Str("barcode.format", "CODE_128"), w, h);
        var left = LeftRows(o);
        var columns = o.Columns("columns", DefaultColumns);
        var processes = data.Lists.GetValueOrDefault("Processes") ?? [];
        var rows = Math.Max(o.Int("process_rows", 16), left.Count);
        var content = o.Num("font.content", 10);

        doc.Page(page =>
        {
            page.Size(o.A4Page());
            page.Margin(o.Num("page.margin", 20));
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontSize(o.Num("font.default", 12)).FontFamily(fonts));
            page.Content().Column(column =>
            {
                column.Item().PaddingVertical(4);
                // 제목 + 바코드
                column.Item().Row(row =>
                {
                    row.RelativeItem().Padding(5).AlignCenter().Text(o.Str("title", "공정이동표")).FontSize(o.Num("font.title", 40)).SemiBold();
                    row.RelativeItem().AlignRight().AlignMiddle().Height(50).Width(o.Num("barcode.width", 200))
                        .Svg(Barcode(o.Int("barcode.width", 200), o.Int("barcode.height", 70)));
                });
                column.Item().PaddingVertical(4);

                // 보안품 | 기종(세로) | 기종 값 | 우선
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.ConstantColumn(170); c.ConstantColumn(30); c.RelativeColumn(); c.ConstantColumn(100); });
                    table.Cell().Element(Box).AlignMiddle().AlignCenter()
                        .Text(o.Bool("show_security_box", true) ? o.Str("security_label", "보안품") : "").FontSize(o.Num("font.security", 32)).SemiBold();
                    table.Cell().Element(Box).AlignMiddle().AlignCenter().Text("기\n종").FontSize(o.Num("font.header", 10));
                    table.Cell().Element(Box).AlignMiddle().AlignCenter().Text(LayoutValues.Text(v.GetValueOrDefault("Model"))).FontSize(o.Num("font.item_title", 30)).Bold();
                    var priority = table.Cell().Element(Box).AlignMiddle().AlignCenter()
                        .Text(LayoutValues.Text(v.GetValueOrDefault("PriorityName"))).FontSize(o.Num("font.item_title", 30)).Bold();
                    if (v.GetValueOrDefault("PriorityColor") is string color && color.StartsWith('#')) priority.FontColor(color);
                });

                // 좌측 정보 × 공정 기록
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        foreach (var col in columns)
                            if (col.Width is { } w) c.ConstantColumn(w); else c.RelativeColumn(col.Relative);
                    });
                    foreach (var col in columns)
                    {
                        var title = col.Key switch
                        {
                            "label" => string.IsNullOrEmpty(col.Title) ? "수주번호" : col.Title,
                            "value" => LayoutValues.Text(LayoutValues.Get(v, barcodeKey)),
                            _ => col.Title,
                        };
                        table.Cell().Border(1).Padding(2).MinHeight(35).Background(Colors.Grey.Lighten3).AlignCenter().AlignMiddle()
                            .Text(title).FontSize(o.Num("font.header", 10));
                    }
                    for (var r = 0; r < rows; r++)
                    {
                        var info = r < left.Count ? left[r] : (Key: "", Title: "");
                        var step = r < processes.Count ? processes[r] : null;
                        foreach (var col in columns)
                        {
                            var text = col.Key switch
                            {
                                "label" => info.Title,
                                "value" => Format(info.Key, v),
                                "seq" => (r + 1).ToString(CultureInfo.InvariantCulture),
                                "unit_process" => step is null ? "" : LayoutValues.Text(step.GetValueOrDefault("UnitProcess")),
                                _ => "",
                            };
                            table.Cell().Border(1).PaddingHorizontal(4).PaddingVertical(1).MinHeight(20).AlignCenter().AlignMiddle().Text(text).FontSize(content);
                        }
                    }
                });

                // 특기사항 + 바코드
                var notes = new[]
                {
                    v.GetValueOrDefault("SpecialNote") as string,
                    v.GetValueOrDefault("Remark") is string remark && remark.Length > 0 ? $"[비고] {remark}" : null,
                }.Where(s => !string.IsNullOrWhiteSpace(s));
                column.Item().Border(1).Padding(5).MinHeight(80).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("특기사항").FontSize(content).SemiBold();
                        col.Item().Text(string.Join("\n", notes)).FontSize(o.Num("font.special", 30));
                    });
                    row.ConstantItem(150).AlignBottom().Height(40).Svg(Barcode(150, 40));
                });
            });
        });

        static IContainer Box(IContainer c) => c.Border(1).Padding(4).MinHeight(40);
    }

    private static List<(string Key, string Title)> LeftRows(LayoutOptions o)
    {
        if (o.Array("left_labels") is not { } a) return [.. DefaultLeft];
        return a.Select((n, idx) => n switch
        {
            JsonObject obj => (obj["key"]?.GetValue<string>() ?? "", obj["title"]?.GetValue<string>() ?? ""),
            JsonValue s when s.TryGetValue(out string? title) => (idx < DefaultLeft.Length ? DefaultLeft[idx].Key : "", title ?? ""),
            _ => ("", ""),
        }).ToList();
    }

    /// <summary>수량 "ea", 중량 "kg", 단중 소수 3자리 kg (구와 같은 표기)</summary>
    internal static string Format(string key, IReadOnlyDictionary<string, object?> values)
    {
        if (string.IsNullOrEmpty(key)) return "";
        var value = LayoutValues.Get(values, key);
        if (value is null) return "";
        return key.Replace("_", "").ToLowerInvariant() switch
        {
            "qty" => $"{LayoutValues.Text(value, "#,##0.###")} ea",
            "weight" => $"{LayoutValues.Text(value, "#,##0.##")} kg",
            "unitweight" => $"{LayoutValues.Text(value, "0.000")} kg",
            _ => LayoutValues.Text(value),
        };
    }
}
