using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>작업일보·작업표준서 공통 부품 (구 WorkDailySheet·WorkStandardSheet 의 같은 스타일)</summary>
internal static class SheetParts
{
    public static void Section(ColumnDescriptor col, string title, float size) =>
        col.Item().Background(Colors.Grey.Darken2).PaddingHorizontal(3).PaddingVertical(2).Text(title).FontSize(size).Bold().FontColor(Colors.White);

    public static IContainer LabelCell(IContainer c) => c.Border(1).Background(Colors.Grey.Lighten3).PaddingHorizontal(2).PaddingVertical(1).MinHeight(14);
    public static IContainer DataCell(IContainer c) => c.Border(1).PaddingHorizontal(2).PaddingVertical(1).MinHeight(14);
    public static IContainer HeaderCell(IContainer c) => c.Border(1).Background(Colors.Blue.Lighten4).PaddingHorizontal(2).PaddingVertical(1).MinHeight(16);

    public static void Label(TableDescriptor t, string text, float size) =>
        t.Cell().Element(LabelCell).AlignCenter().AlignMiddle().Text(text).FontSize(size).Bold();

    public static void Value(TableDescriptor t, string? text, float size, uint span = 1) =>
        t.Cell().ColumnSpan(span).Element(DataCell).AlignLeft().AlignMiddle().PaddingLeft(2).Text(text ?? "").FontSize(size);

    public static void Center(TableDescriptor t, string? text, float size) =>
        t.Cell().Element(DataCell).AlignCenter().AlignMiddle().Text(text ?? "").FontSize(size);

    public static void Header(TableDescriptor t, string text, float size) =>
        t.Cell().Element(HeaderCell).AlignCenter().AlignMiddle().Text(text).FontSize(size).Bold();

    public static void Empty(ColumnDescriptor col, string message, float size) =>
        col.Item().Border(1).Padding(6).AlignCenter().Text(message).FontSize(size).FontColor(Colors.Grey.Medium);

    /// <summary>관리항목 × 스텝 표 (목록 {name}Steps / {name}, ConditionMatrix 형식). 비었으면 false</summary>
    public static bool Matrix(ColumnDescriptor col, PrintData data, string name, string itemHeader, float itemWidth, float size)
    {
        var steps = data.Lists.GetValueOrDefault(name + "Steps") ?? [];
        var rows = data.Lists.GetValueOrDefault(name) ?? [];
        if (steps.Count == 0 || rows.Count == 0) return false;
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(itemWidth);
                foreach (var _ in steps) c.RelativeColumn();
            });
            Header(table, itemHeader, size);
            foreach (var s in steps) Header(table, LayoutValues.Text(s.GetValueOrDefault("Name")), size);
            foreach (var row in rows)
            {
                var unit = LayoutValues.Text(row.GetValueOrDefault("Unit"));
                Label(table, unit.Length > 0 ? $"{LayoutValues.Text(row.GetValueOrDefault("Item"))} ({unit})" : LayoutValues.Text(row.GetValueOrDefault("Item")), size);
                foreach (var s in steps)
                    Center(table, LayoutValues.Text(row.GetValueOrDefault($"S{LayoutValues.Text(s.GetValueOrDefault("No"))}")), size);
            }
        });
        return true;
    }

    /// <summary>sections 옵션 — 표시 여부와 제목 (없으면 기본 제목)</summary>
    public static (bool Visible, string Title) SectionOf(LayoutOptions o, string key, string defaultTitle)
    {
        if (o.Array("sections") is not { } a) return (true, defaultTitle);
        foreach (var s in a.OfType<System.Text.Json.Nodes.JsonObject>())
            if (s["key"]?.GetValue<string>() == key)
                return (s["visible"] is not System.Text.Json.Nodes.JsonValue v || !v.TryGetValue(out bool b) || b, s["title"]?.GetValue<string>() ?? defaultTitle);
        return (true, defaultTitle);
    }
}
