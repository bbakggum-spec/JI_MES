using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace JiMes.Api.Features.Printing;

/// <param name="List">LIST 블록 안이면 그 목록명</param>
/// <param name="Marker">'#' 블록 시작 / '/' 블록 끝 / null 일반 치환자</param>
public sealed record PlaceholderUse(string Sheet, string Cell, string Key, string? List, char? Marker);

public sealed record PlaceholderCheck(IReadOnlyList<string> Placeholders, IReadOnlyList<string> Unknown, IReadOnlyList<string> Errors);

/// <summary>
/// 엑셀 양식 치환자 문법 (§15.2 작성 규칙) — <c>{{키}}</c> / <c>{{한글별칭}}</c> / 반복행 <c>{{#목록}} … {{/목록}}</c> / 이미지 <c>{{이미지키}}</c>.
/// 구 문법 <c>{키}</c> 는 지원하지 않는다 (P2 통일).
/// </summary>
public static partial class Placeholders
{
    [GeneratedRegex(@"\{\{\s*(?<marker>[#/]?)\s*(?<key>[^{}#/\s][^{}]*?)\s*\}\}")]
    public static partial Regex Token();

    [GeneratedRegex(@"^[TC]\d+_\d+_[A-Za-z0-9]+$")]
    public static partial Regex LegacyGridKey();

    /// <summary>모든 시트의 치환자를 목록 블록 문맥과 함께 추출</summary>
    public static IReadOnlyList<PlaceholderUse> Scan(XLWorkbook workbook)
    {
        var uses = new List<PlaceholderUse>();
        foreach (var ws in workbook.Worksheets)
        {
            string? openList = null;
            foreach (var row in ws.RowsUsed())
            {
                string? closeAfterRow = null;
                foreach (var cell in row.CellsUsed())
                {
                    if (cell.DataType != XLDataType.Text) continue;
                    foreach (Match m in Token().Matches(cell.GetString()))
                    {
                        var key = m.Groups["key"].Value;
                        var marker = m.Groups["marker"].Value is { Length: 1 } s ? s[0] : (char?)null;
                        if (marker == '#') openList = key;
                        uses.Add(new PlaceholderUse(ws.Name, cell.Address.ToString()!, key, marker is null ? openList : key, marker));
                        if (marker == '/') closeAfterRow = key;
                    }
                }
                if (closeAfterRow is not null) openList = null;
            }
        }
        return uses;
    }

    /// <summary>사전과 대조 — 사전에 없는 치환자는 경고, 블록 짝이 맞지 않으면 오류</summary>
    public static PlaceholderCheck Check(IReadOnlyList<PlaceholderUse> uses, FieldDictionary fields)
    {
        var errors = new List<string>();
        foreach (var group in uses.Where(u => u.Marker is not null).GroupBy(u => (u.Sheet, fields.Resolve(u.Key))))
        {
            var opens = group.Count(u => u.Marker == '#');
            var closes = group.Count(u => u.Marker == '/');
            if (opens != closes)
                errors.Add($"[{group.Key.Sheet}] 목록 {{{{#{group.Key.Item2}}}}} 과 {{{{/{group.Key.Item2}}}}} 의 개수가 맞지 않습니다.");
            if (!fields.IsList(group.Key.Item2))
                errors.Add($"[{group.Key.Sheet}] '{group.Key.Item2}' 는 목록(LIST) 치환자가 아닙니다.");
        }

        var unknown = uses
            .Where(u => u.Marker is null && !IsKnown(u, fields))
            .Select(u => u.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var all = uses.Where(u => u.Marker is null).Select(u => u.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new PlaceholderCheck(all, unknown, errors);
    }

    private static bool IsKnown(PlaceholderUse u, FieldDictionary fields)
    {
        if (u.List is not null && fields.IsListField(fields.Resolve(u.List), u.Key))
            return true;
        if (fields.Get(u.Key) is not null)
            return true;
        return fields.AllowLegacyGridKeys && LegacyGridKey().IsMatch(u.Key);
    }
}
