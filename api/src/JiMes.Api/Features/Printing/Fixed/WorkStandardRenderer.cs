using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static JiMes.Api.Features.Printing.Fixed.SheetParts;

namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>
/// 작업표준서 (구 PrintDoc/WorkStandardSheet, F_WorkStandardForm) — 작업표준 Version 1건, A4 가로.
/// 품목 정보 / 요구사항 | 작업 설비·사이클 | 작업 공정 (나란히) / 작업표준 (관리항목 × 스텝).
/// 구역 제목·표시·글꼴·폭 = layout_options_json (§15.3.1 WORK_STANDARD). 작업시간은 구와 같이 시간(hr).
/// </summary>
public sealed class WorkStandardRenderer : IFixedRenderer
{
    public string RendererKey => "WORK_STANDARD";

    public void Compose(IDocumentContainer doc, PrintData data, LayoutOptions o)
    {
        var fonts = PdfFonts.Pick(o.Strings("font.family", "맑은 고딕", "Malgun Gothic"));
        float section = o.Num("font.section", 7), label = o.Num("font.label", 7), value = o.Num("font.value", 7);
        var labelWidth = o.Num("label_width", 42);
        string V(string key) => LayoutValues.Text(data.Values.GetValueOrDefault(key));

        doc.Page(page =>
        {
            page.Size(o.A4Page("LANDSCAPE"));
            page.Margin(o.Num("page.margin", 8));
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontSize(section).FontFamily(fonts));
            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Text(o.Str("title", "작  업  표  준  서")).FontSize(o.Num("font.title", 14)).Bold();

                if (SectionOf(o, "part_info", "품  목  정  보") is (true, var partTitle))
                {
                    col.Item().PaddingVertical(2);
                    Section(col, partTitle, section);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.ConstantColumn(labelWidth); c.RelativeColumn(3); c.ConstantColumn(38); c.RelativeColumn(2); c.ConstantColumn(38); c.RelativeColumn(2); });
                        Label(t, "거 래 처", label); Value(t, V("CustomerName"), value);
                        Label(t, "품     명", label); Value(t, V("PartName"), value);
                        Label(t, "작  성  일", label); Value(t, V("WriteDate"), value);
                        Label(t, "품     번", label); Value(t, V("PartNumber"), value);
                        Label(t, "규     격", label); Value(t, V("Specification"), value);
                        Label(t, "기     종", label); Value(t, V("Model"), value);
                        Label(t, "표준코드", label); Value(t, $"{V("StandardCode")}  (Version {V("VersionNo")})", value);
                        Label(t, "표준명", label); Value(t, V("StandardName"), value, 3);
                    });
                }

                var requirement = SectionOf(o, "requirement", "요  구  사  항");
                var cycle = SectionOf(o, "equipment_cycle", "작  업  설  비  /  사이클");
                var process = SectionOf(o, "process", "작  업  공  정");
                if (requirement.Visible || cycle.Visible || process.Visible)
                {
                    col.Item().PaddingVertical(2);
                    col.Item().Row(row =>
                    {
                        var first = true;
                        void Gap() { if (!first) row.ConstantItem(4); first = false; }
                        if (requirement.Visible)
                        {
                            Gap();
                            row.RelativeItem(5).Column(inner =>
                            {
                                Section(inner, requirement.Title, section);
                                inner.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c => { c.ConstantColumn(labelWidth); c.RelativeColumn(); c.ConstantColumn(labelWidth); c.RelativeColumn(); });
                                    Label(t, "재     질", label); Value(t, V("Material"), value);
                                    Label(t, "요구경도", label); Value(t, V("Hardness"), value);
                                    Label(t, "심부경도", label); Value(t, V("CoreHardness"), value);
                                    Label(t, "경 화 층", label); Value(t, V("CaseDepth"), value);
                                    Label(t, "조     직", label); Value(t, V("Texture"), value, 3);
                                });
                            });
                        }
                        if (cycle.Visible)
                        {
                            Gap();
                            row.RelativeItem(3).Column(inner =>
                            {
                                Section(inner, cycle.Title, section);
                                inner.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c => { c.ConstantColumn(labelWidth); c.RelativeColumn(); });
                                    Label(t, "설비구분", label); Value(t, V("EquipmentTypeName"), value);
                                    Label(t, "적용설비", label); Value(t, V("EquipmentName"), value);
                                    Label(t, "단위공정", label); Value(t, V("UnitProcessName"), value);
                                    Label(t, "투입수량", label); Value(t, V("Charge"), value);
                                    Label(t, "작업시간", label); Value(t, V("RunningHours") is { Length: > 0 } h ? $"{h} hr" : "", value);
                                });
                            });
                        }
                        if (process.Visible)
                        {
                            Gap();
                            row.RelativeItem(4).Column(inner =>
                            {
                                Section(inner, process.Title, section);
                                inner.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c => { c.ConstantColumn(labelWidth); c.RelativeColumn(); });
                                    Label(t, "열처리\n공정명", label); Value(t, V("HeatProcessName"), value);
                                    Label(t, "공정순서", label); Value(t, V("ProcessFlow"), value);
                                });
                            });
                        }
                    });
                }

                if (SectionOf(o, "standard", "작  업  표  준") is (true, var standardTitle))
                {
                    col.Item().PaddingVertical(2);
                    Section(col, standardTitle, section);
                    if (!Matrix(col, data, "Standard", o.Str("condition_item_header", "관리항목"), o.Num("condition_item_column_width", 48), o.Num("font.standard_table", 7)))
                        SheetParts.Empty(col, o.Str("empty_text", "작업표준 상세 데이터가 없습니다."), section);
                }
            });
        });
    }
}
