using System.Globalization;

namespace JiMes.Api.Features.Printing.Providers;

/// <summary>
/// 관리항목(행) × 스텝(열) 표를 출력 값으로 (작업표준·작업조건 공통).
/// 목록 {name}Steps = [{No, Name}], {name} = [{Item, Unit, S0, S1 … Sn}] — S0 = 스텝 무관(공통) 값, 있을 때만 Steps 맨 앞에 "공통".
/// 엑셀 양식은 {{#Conditions}}{{Item}} {{S1}} …{{/Conditions}} 처럼 쓴다.
/// </summary>
public static class ConditionMatrix
{
    public sealed record Step(int No, string Name);
    public sealed record Cell(int? StepNo, long ItemId, string? Value);
    public sealed record Item(long ItemId, string Name, string? Unit);

    public static void Fill(PrintData data, string name, IReadOnlyList<Step> steps, IReadOnlyList<Item> items, IReadOnlyList<Cell> cells, string commonTitle = "공통")
    {
        var hasCommon = cells.Any(c => c.StepNo is null && !string.IsNullOrEmpty(c.Value));
        var stepRows = new List<Dictionary<string, object?>>();
        if (hasCommon) stepRows.Add(Row(("No", "0"), ("Name", commonTitle)));
        stepRows.AddRange(steps.Select(s => Row(("No", s.No.ToString(CultureInfo.InvariantCulture)), ("Name", s.Name))));
        data.Lists[name + "Steps"] = stepRows;
        var byItem = cells.ToLookup(c => c.ItemId);
        data.Lists[name] = items.Select(i =>
        {
            var row = Row(("Item", i.Name), ("Unit", i.Unit));
            foreach (var c in byItem[i.ItemId])
                row[$"S{c.StepNo ?? 0}"] = c.Value;
            return row;
        }).ToList();
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] values)
    {
        var row = PrintData.Row();
        foreach (var (k, v) in values) row[k] = v;
        return row;
    }
}
