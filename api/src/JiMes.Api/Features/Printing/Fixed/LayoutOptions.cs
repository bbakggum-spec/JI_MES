using System.Globalization;
using System.Text.Json.Nodes;
using QuestPDF.Helpers;

namespace JiMes.Api.Features.Printing.Fixed;

/// <summary>
/// 고정 양식 레이아웃 옵션 읽기 (layout_options_json, 설계 §15.3.1). 키가 없거나 형식이 다르면 렌더러 기본값.
/// 경로는 점 구분 ("font.title", "page.margin").
/// </summary>
public sealed class LayoutOptions(JsonNode? root)
{
    public static LayoutOptions Parse(string? json) => new(string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json));

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

    /// <summary>[{key, title, width|relative, visible}] — visible=false 는 뺀다</summary>
    public IReadOnlyList<LayoutColumn> Columns(string path, IReadOnlyList<LayoutColumn> fallback)
    {
        if (Array(path) is not { } a) return fallback;
        var list = a.OfType<JsonObject>()
            .Where(c => c["visible"] is not JsonValue v || !v.TryGetValue(out bool b) || b)
            .Select(c => new LayoutColumn(
                c["key"]?.GetValue<string>() ?? "",
                c["title"]?.GetValue<string>() ?? "",
                c["width"] is JsonValue w && w.TryGetValue(out double wd) ? (float)wd : null,
                c["relative"] is JsonValue r && r.TryGetValue(out double rd) ? (float)rd : 1))
            .ToList();
        return list.Count > 0 ? list : fallback;
    }

    /// <summary>page.size(A4)·orientation 또는 width_mm/height_mm</summary>
    public PageSize A4Page(string defaultOrientation = "PORTRAIT") =>
        Str("page.orientation", defaultOrientation) == "LANDSCAPE" ? PageSizes.A4.Landscape() : PageSizes.A4;

    private JsonNode? Node(string path)
    {
        var node = root;
        foreach (var part in path.Split('.'))
            node = node is JsonObject o && o.TryGetPropertyValue(part, out var next) ? next : null;
        return node;
    }
}

public sealed record LayoutColumn(string Key, string Title, float? Width, float Relative);

/// <summary>렌더러 공통 — 출력 값 꺼내기·표시 형식</summary>
public static class LayoutValues
{
    /// <summary>
    /// 옵션의 키(snake_case, 예: customer_name)와 출력 값 키(PascalCase, CustomerName)를 같은 것으로 본다.
    /// </summary>
    public static object? Get(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (values.TryGetValue(key, out var v)) return v;
        var compact = key.Replace("_", "");
        foreach (var (k, value) in values)
            if (string.Equals(k.Replace("_", ""), compact, StringComparison.OrdinalIgnoreCase)) return value;
        return null;
    }

    public static string Text(object? value, string? format = null) => value switch
    {
        null => "",
        decimal d => d.ToString(format ?? "#,##0.###", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
