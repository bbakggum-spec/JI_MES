using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace JiMes.Api.Features.Printing;

/// <summary>
/// EXCEL 양식 채우기 (§5.3.1, §15.2). 셀 서식은 양식 그대로 두고 값만 바꾼다.
/// <list type="number">
/// <item>반복행: <c>{{#목록}}</c> 이 있는 행부터 <c>{{/목록}}</c> 이 있는 행까지를 데이터 수만큼 복제 (0건이면 행 삭제) — P4</item>
/// <item>치환: 모든 시트·모든 셀. 셀 전체가 치환자 하나이고 값이 숫자면 숫자 셀로 넣어 양식의 숫자 형식이 적용되게 한다</item>
/// <item>이미지: IMAGE 치환자가 있는 셀의 병합 영역에 맞춰 삽입 — 모든 시트·모든 위치 (P7)</item>
/// </list>
/// 사전에 없는 치환자는 그대로 남겨 양식 오류가 출력물에서 보이게 한다. 단 구 좌표형 키는 값이 없으면 빈칸 (데이터 행보다 양식 행이 많을 때).
/// </summary>
public static class ExcelTemplateRenderer
{
    public static byte[] Render(byte[] template, PrintData data, FieldDictionary fields)
    {
        using var input = new MemoryStream(template);
        using var workbook = new XLWorkbook(input);
        foreach (var ws in workbook.Worksheets)
        {
            ExpandLists(ws, data, fields);
            ReplaceCells(ws, data, fields, list: null, item: null, rows: null);
            InsertImages(ws, data, fields);
        }
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private sealed record Block(string List, int StartRow, int EndRow);

    private static void ExpandLists(IXLWorksheet ws, PrintData data, FieldDictionary fields)
    {
        // 아래 블록부터 처리해야 위쪽 행 번호가 바뀌지 않는다
        foreach (var block in FindBlocks(ws, fields).OrderByDescending(b => b.StartRow))
        {
            var items = data.Lists.GetValueOrDefault(block.List) ?? [];
            var height = block.EndRow - block.StartRow + 1;
            if (items.Count == 0)
            {
                ws.Rows(block.StartRow, block.EndRow).Delete();
                continue;
            }

            var lastColumn = Math.Max(ws.LastColumnUsed()?.ColumnNumber() ?? 1, 1);
            var source = ws.Range(block.StartRow, 1, block.EndRow, lastColumn);
            if (items.Count > 1)
                ws.Row(block.EndRow).InsertRowsBelow(height * (items.Count - 1));
            for (var i = 1; i < items.Count; i++)
            {
                var destRow = block.StartRow + i * height;
                source.CopyTo(ws.Cell(destRow, 1));
                for (var r = 0; r < height; r++)
                    ws.Row(destRow + r).Height = ws.Row(block.StartRow + r).Height;
            }
            for (var i = 0; i < items.Count; i++)
            {
                var first = block.StartRow + i * height;
                ReplaceCells(ws, data, fields, block.List, items[i], (first, first + height - 1));
            }
        }
    }

    private static List<Block> FindBlocks(IXLWorksheet ws, FieldDictionary fields)
    {
        var blocks = new List<Block>();
        var open = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in ws.CellsUsed(c => c.DataType == XLDataType.Text).OrderBy(c => c.Address.RowNumber))
        {
            foreach (Match m in Placeholders.Token().Matches(cell.GetString()))
            {
                var list = fields.Resolve(m.Groups["key"].Value);
                switch (m.Groups["marker"].Value)
                {
                    case "#":
                        open[list] = cell.Address.RowNumber;
                        break;
                    case "/" when open.Remove(list, out var start):
                        blocks.Add(new Block(list, start, cell.Address.RowNumber));
                        break;
                }
            }
        }
        return blocks;
    }

    private static void ReplaceCells(
        IXLWorksheet ws, PrintData data, FieldDictionary fields,
        string? list, Dictionary<string, object?>? item, (int From, int To)? rows)
    {
        var cells = rows is { } r
            ? ws.Range(r.From, 1, r.To, Math.Max(ws.LastColumnUsed()?.ColumnNumber() ?? 1, 1)).CellsUsed(c => c.DataType == XLDataType.Text)
            : ws.CellsUsed(c => c.DataType == XLDataType.Text);

        foreach (var cell in cells.ToList())
        {
            var text = cell.GetString();
            if (!text.Contains("{{")) continue;

            // 목록 블록 안: 표시자({{#…}} {{/…}})를 먼저 지우고 남은 내용으로 판단
            if (list is not null)
            {
                text = Placeholders.Token().Replace(text, m => m.Groups["marker"].Value.Length > 0 ? "" : m.Value);
                if (!text.Contains("{{"))
                {
                    cell.Value = text.Trim();
                    continue;
                }
            }

            var matches = Placeholders.Token().Matches(text);
            if (matches.All(m => m.Groups["marker"].Value.Length > 0))
                continue;   // 짝이 맞지 않는 표시자 — 양식 오류가 보이게 그대로

            // 셀 전체가 치환자 하나 → 값 형식 그대로 (숫자 셀 유지)
            if (matches.Count == 1 && matches[0].Value == text.Trim() && matches[0].Groups["marker"].Value.Length == 0)
            {
                var key = matches[0].Groups["key"].Value;
                if (fields.IsImage(key)) continue;   // 이미지 단계에서 처리
                if (TryResolve(key, data, fields, list, item, out var value))
                {
                    cell.Value = value switch
                    {
                        null => Blank.Value,
                        decimal d => d,
                        _ => value.ToString(),
                    };
                }
                continue;
            }

            var replaced = Placeholders.Token().Replace(text, m =>
            {
                if (m.Groups["marker"].Value.Length > 0) return "";
                var key = m.Groups["key"].Value;
                if (fields.IsImage(key)) return m.Value;
                return TryResolve(key, data, fields, list, item, out var value)
                    ? Format(value, fields.FormatOf(key, list))
                    : m.Value;
            });
            cell.Value = replaced;
        }
    }

    private static bool TryResolve(
        string rawKey, PrintData data, FieldDictionary fields, string? list, Dictionary<string, object?>? item, out object? value)
    {
        var key = fields.Resolve(rawKey);
        if (item is not null && list is not null)
        {
            var shortKey = key.StartsWith(list + ".", StringComparison.OrdinalIgnoreCase) ? key[(list.Length + 1)..] : key;
            if (item.TryGetValue(shortKey, out value))
                return true;
        }
        if (data.Values.TryGetValue(key, out value))
            return true;
        if (fields.AllowLegacyGridKeys && Placeholders.LegacyGridKey().IsMatch(key))
        {
            value = null;   // 데이터보다 양식 행이 많으면 빈칸
            return true;
        }
        // 사전에 있지만 공급원이 값을 주지 않은 키도 빈칸
        value = null;
        return fields.Get(key) is not null || (list is not null && fields.IsListField(list, key));
    }

    private static string Format(object? value, string? pattern) => value switch
    {
        null => "",
        decimal d => d.ToString(pattern ?? "#,##0.###", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static void InsertImages(IXLWorksheet ws, PrintData data, FieldDictionary fields)
    {
        foreach (var cell in ws.CellsUsed(c => c.DataType == XLDataType.Text).ToList())
        {
            var match = Placeholders.Token().Match(cell.GetString());
            if (!match.Success || !fields.IsImage(match.Groups["key"].Value))
                continue;

            var key = fields.Resolve(match.Groups["key"].Value);
            cell.Value = Blank.Value;
            if (!data.Images.TryGetValue(key, out var image))
                continue;

            var area = cell.IsMerged() ? cell.MergedRange() : ws.Range(cell.Address, cell.Address);
            using var stream = new MemoryStream(image);
            ws.AddPicture(stream).MoveTo(area.FirstCell(), area.LastCell().CellRight().CellBelow());
        }
    }
}
