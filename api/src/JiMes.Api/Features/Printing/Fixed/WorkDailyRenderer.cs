using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static JiMes.Api.Features.Printing.Fixed.SheetParts;

namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>
/// 작업일보 (구 PrintDoc/WorkDailySheet, F_GasForm) — 작업 LOT 1건, A4 가로.
/// 로트 정보 / 투입 정보 / 분할 정보 / 작업표준사항 | 작업조건 (나란히) / 검사 내역 / 불량 내역 / 특기사항.
/// 구역 순서는 고정, 제목·표시 여부·글꼴·투입·분할 열 = layout_options_json (§15.3.1 WORK_DAILY).
/// </summary>
public sealed class WorkDailyRenderer : IFixedRenderer
{
    public string RendererKey => "WORK_DAILY";

    private static readonly LayoutColumn[] DefaultInputColumns =
    [
        new("sales_order_no", "수주번호", 70, 0), new("customer_name", "거래처", null, 2), new("part_name", "품명", null, 3),
        new("part_number", "품번", null, 2), new("specification", "규격", null, 2), new("qty", "수량", 40, 0),
        new("defect_qty", "불량수량", 40, 0), new("weight", "중량", 55, 0),
    ];

    private static readonly LayoutColumn[] DefaultSplitColumns =
        [new("tray", "트레이번호", null, 2), new("qty", "수량", 60, 0), new("loaded_at", "투입시간", 80, 0), new("unloaded_at", "출고시간", 80, 0)];

    public void Compose(IDocumentContainer doc, PrintData data, LayoutOptions o)
    {
        var fonts = PdfFonts.Pick(o.Strings("font.family", "맑은 고딕", "Malgun Gothic"));
        var v = data.Values;
        float section = o.Num("font.section", 8), label = o.Num("font.label", 7), value = o.Num("font.value", 7), tableSize = o.Num("font.table", 7);
        string Empty(string key, string fallback) => o.Str($"empty_text.{key}", fallback);
        string V(string key) => LayoutValues.Text(v.GetValueOrDefault(key));

        doc.Page(page =>
        {
            page.Size(o.A4Page("LANDSCAPE"));
            page.Margin(o.Num("page.margin", 8));
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontSize(tableSize).FontFamily(fonts));
            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Text(o.Str("title", "작  업  일  보")).FontSize(o.Num("font.title", 14)).Bold();

                if (SectionOf(o, "lot_info", "로  트  정  보") is (true, var lotTitle))
                {
                    col.Item().PaddingVertical(2);
                    Section(col, lotTitle, section);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.ConstantColumn(46); c.RelativeColumn(3); c.ConstantColumn(46); c.RelativeColumn(2); c.ConstantColumn(46); c.RelativeColumn(2); });
                        Label(t, "로트번호", label); Value(t, $"{V("LotNo")} {V("IsMainProcess")}".Trim(), value);
                        Label(t, "작업일자", label); Value(t, V("WorkDate"), value);
                        Label(t, "진행상태", label); Value(t, V("StatusName"), value);
                        Label(t, "설비명", label); Value(t, V("EquipmentName"), value);
                        Label(t, "단위공정", label); Value(t, V("UnitProcessName"), value);
                        Label(t, "작업자", label); Value(t, V("WorkerName"), value);
                        Label(t, "시작시간", label); Value(t, V("StartedAt"), value);
                        Label(t, "완료시간", label); Value(t, V("EndedAt"), value);
                        Label(t, "재작업", label); Value(t, V("IsRework"), value);
                        Label(t, "주 LOT", label); Value(t, V("MainLotNo"), value);
                        Label(t, "제출LOT", label); Value(t, V("SubmitLotNo"), value);
                        Label(t, "공정", label); Value(t, V("HeatProcessName"), value);
                    });
                }

                if (SectionOf(o, "input", "투  입  정  보") is (true, var inputTitle))
                {
                    col.Item().PaddingVertical(2);
                    Section(col, inputTitle, section);
                    ListTable(col, data.Lists.GetValueOrDefault("Inputs") ?? [], o.Columns("input_columns", DefaultInputColumns), Empty("input", "투입 내역 없음"), tableSize);
                }

                if (SectionOf(o, "split", "분  할  정  보") is (true, var splitTitle))
                {
                    col.Item().PaddingVertical(2);
                    Section(col, splitTitle, section);
                    var splits = data.Lists.GetValueOrDefault("Splits") ?? [];
                    if (splits.Count == 0) SheetParts.Empty(col, Empty("split", "분할 데이터 없음"), tableSize);
                    else ListTable(col, splits, o.Columns("split_columns", DefaultSplitColumns), "", tableSize);
                }

                var standard = SectionOf(o, "standard", "작  업  표  준  사  항");
                var condition = SectionOf(o, "condition", "작  업  조  건");
                if (standard.Visible || condition.Visible)
                {
                    col.Item().PaddingVertical(2);
                    col.Item().Row(row =>
                    {
                        var itemWidth = o.Num("condition_item_column_width", 60);
                        if (standard.Visible)
                            row.RelativeItem().Column(left =>
                            {
                                Section(left, standard.Title, section);
                                if (!Matrix(left, data, "Standard", "관리항목", itemWidth, tableSize)) SheetParts.Empty(left, Empty("standard", "작업표준 데이터 없음"), tableSize);
                            });
                        if (standard.Visible && condition.Visible) row.ConstantItem(6);
                        if (condition.Visible)
                            row.RelativeItem().Column(right =>
                            {
                                Section(right, condition.Title, section);
                                if (!Matrix(right, data, "Conditions", "관리항목", itemWidth, tableSize)) SheetParts.Empty(right, Empty("condition", "작업조건 데이터 없음"), tableSize);
                            });
                    });
                }

                if (SectionOf(o, "inspection", "검  사  내  역") is (true, var inspectionTitle))
                {
                    col.Item().PaddingVertical(2);
                    Section(col, inspectionTitle, section);
                    ListTable(col, data.Lists.GetValueOrDefault("Inspections") ?? [],
                        [new("inspection_no", "검사번호", 80, 0), new("item", "항목", null, 2), new("specification", "기준", null, 2),
                         new("values", "측정값", null, 4), new("result", "결과", 50, 0), new("decision", "판정", 45, 0)],
                        Empty("inspection", "검사 내역 없음"), tableSize);
                }

                if (SectionOf(o, "defect", "불  량  내  역") is (true, var defectTitle))
                {
                    col.Item().PaddingVertical(2);
                    Section(col, defectTitle, section);
                    ListTable(col, data.Lists.GetValueOrDefault("Defects") ?? [],
                        [new("sales_order_no", "수주번호", 80, 0), new("qty", "불량수량", 60, 0), new("detail", "불량내역", null, 2),
                         new("reason", "불량원인", null, 3), new("decision", "처리", 60, 0)],
                        Empty("defect", "불량 내역 없음"), tableSize);
                }

                if (SectionOf(o, "remark", "특  기  사  항") is (true, var remarkTitle))
                {
                    col.Item().PaddingVertical(2);
                    Section(col, remarkTitle, section);
                    col.Item().Border(1).PaddingHorizontal(4).PaddingVertical(3).MinHeight(30).Text(V("Remark")).FontSize(value);
                }
            });
        });
    }

    /// <summary>목록 표 — 열 키(snake_case)로 행 값을 찾고, 수량·중량은 숫자 형식</summary>
    private static void ListTable(ColumnDescriptor col, List<Dictionary<string, object?>> rows, IReadOnlyList<LayoutColumn> columns, string emptyText, float size)
    {
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { foreach (var x in columns) if (x.Width is { } w) c.ConstantColumn(w); else c.RelativeColumn(x.Relative); });
            foreach (var x in columns) Header(t, x.Title, size);
            if (rows.Count == 0)
            {
                t.Cell().ColumnSpan((uint)columns.Count).Element(DataCell).AlignCenter().AlignMiddle().Text(emptyText).FontSize(size);
                return;
            }
            foreach (var row in rows)
                foreach (var x in columns)
                {
                    var raw = LayoutValues.Get(row, x.Key);
                    var text = raw is decimal d ? LayoutValues.Text(d, x.Key.Contains("weight") ? "#,##0.##" : "#,##0.###") : LayoutValues.Text(raw);
                    if (raw is decimal) Center(t, text, size); else Value(t, text, size);
                }
        });
    }
}
