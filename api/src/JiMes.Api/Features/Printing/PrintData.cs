using System.Text.Json;

namespace JiMes.Api.Features.Printing;

/// <summary>
/// 데이터 공급원이 채우는 출력 값 — EXCEL 양식 치환과 FIXED 렌더러가 같은 값을 쓴다 (§5.3 "호출하는 쪽은 방식을 구분하지 않음").
/// 값은 string / decimal / null. 날짜는 공급원이 사전(print_field.format_pattern) 형식으로 문자열화한다.
/// </summary>
public sealed class PrintData
{
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>LIST 치환자 — 항목 키는 목록 안 짧은 키 (예: Measurements → Item, P1 …)</summary>
    public Dictionary<string, List<Dictionary<string, object?>>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>IMAGE 치환자 — 스냅샷에는 넣지 않는다 (재발행 시 원본 첨부를 다시 읽음)</summary>
    public Dictionary<string, byte[]> Images { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, object?> Row() => new(StringComparer.OrdinalIgnoreCase);

    // ── print_log.data_snapshot_json (재발행 시 같은 값 보장, §15.2 P8) ──

    private sealed record Snapshot(Dictionary<string, object?> Values, Dictionary<string, List<Dictionary<string, object?>>> Lists);

    public string ToSnapshotJson() => JsonSerializer.Serialize(new Snapshot(Values, Lists));

    public static PrintData FromSnapshotJson(string json)
    {
        var data = new PrintData();
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.GetProperty(nameof(Snapshot.Values)).EnumerateObject())
            data.Values[p.Name] = FromJson(p.Value);
        foreach (var list in doc.RootElement.GetProperty(nameof(Snapshot.Lists)).EnumerateObject())
        {
            data.Lists[list.Name] = list.Value.EnumerateArray().Select(item =>
            {
                var row = Row();
                foreach (var p in item.EnumerateObject())
                    row[p.Name] = FromJson(p.Value);
                return row;
            }).ToList();
        }
        return data;
    }

    private static object? FromJson(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDecimal(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => e.ToString(),
    };
}

/// <summary>치환자 사전 1행 (print_field)</summary>
public sealed class PrintFieldRow
{
    public string FieldKey { get; init; } = "";
    public string? FieldAlias { get; init; }
    public string FieldType { get; init; } = "TEXT";
    public string? FieldGroup { get; init; }
    public string? FormatPattern { get; init; }
    public string? Description { get; init; }
    public string? SampleValue { get; init; }
}

/// <summary>데이터 공급원의 치환자 사전 — 별칭 해석·형식·검증에 쓴다.</summary>
public sealed class FieldDictionary
{
    private readonly Dictionary<string, PrintFieldRow> _byKey;
    private readonly Dictionary<string, string> _aliasToKey;

    public FieldDictionary(IEnumerable<PrintFieldRow> fields, bool allowLegacyGridKeys)
    {
        Fields = fields.ToList();
        _byKey = Fields.ToDictionary(f => f.FieldKey, StringComparer.OrdinalIgnoreCase);
        _aliasToKey = Fields.Where(f => f.FieldAlias is not null)
            .ToDictionary(f => f.FieldAlias!, f => f.FieldKey, StringComparer.OrdinalIgnoreCase);
        AllowLegacyGridKeys = allowLegacyGridKeys;
    }

    public IReadOnlyList<PrintFieldRow> Fields { get; }

    /// <summary>검사 대상의 구 좌표형 키 {{T1_3_P2}}, {{C1_2_Spec}} 호환 (§15.2 P4)</summary>
    public bool AllowLegacyGridKeys { get; }

    public string Resolve(string keyOrAlias) => _aliasToKey.TryGetValue(keyOrAlias, out var key) ? key : keyOrAlias;

    public PrintFieldRow? Get(string key) => _byKey.GetValueOrDefault(Resolve(key));

    public bool IsImage(string key) => Get(key)?.FieldType == "IMAGE";

    public bool IsList(string key) => Get(key)?.FieldType == "LIST";

    /// <summary>목록 안에서 쓸 수 있는 항목 키인지 (짧은 키 또는 목록.키)</summary>
    public bool IsListField(string list, string key)
    {
        var k = Resolve(key);
        return _byKey.ContainsKey($"{list}.{k}") || (k.StartsWith(list + ".", StringComparison.OrdinalIgnoreCase) && _byKey.ContainsKey(k));
    }

    public string? FormatOf(string key, string? list = null) =>
        (list is not null ? Get($"{list}.{Resolve(key)}")?.FormatPattern : null) ?? Get(key)?.FormatPattern;
}
