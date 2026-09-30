using System.Globalization;
using System.Text.Json;

namespace JiMes.Api.Infrastructure.Settings;

/// <summary>system_setting.value_type CHECK 제약과 같은 값.</summary>
public static class SettingValueType
{
    public const string String = "STRING";
    public const string Int = "INT";
    public const string Decimal = "DECIMAL";
    public const string Bool = "BOOL";
    public const string Time = "TIME";
    public const string Path = "PATH";
    public const string Json = "JSON";
}

/// <summary>설정값 검증·정규화 — 저장 전 검증과 캐시 적재에 같은 규칙을 쓴다 (설계 §15.4).</summary>
public static class SettingValue
{
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm", "HH:mm:ss"];

    /// <returns>검증을 통과하면 정규화한 값, 아니면 null (<paramref name="error"/>에 사유).</returns>
    public static string? Normalize(string valueType, string? raw, decimal? min, decimal? max, out string? error)
    {
        error = null;
        var value = raw?.Trim();

        switch (valueType)
        {
            case SettingValueType.String:
                return raw ?? "";

            case SettingValueType.Path:
                if (string.IsNullOrEmpty(value)) { error = "경로가 비어 있습니다."; return null; }
                return value;

            case SettingValueType.Int:
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                { error = "정수가 아닙니다."; return null; }
                return CheckRange(l, min, max, out error) ? l.ToString(CultureInfo.InvariantCulture) : null;

            case SettingValueType.Decimal:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                { error = "숫자가 아닙니다."; return null; }
                return CheckRange(d, min, max, out error) ? d.ToString(CultureInfo.InvariantCulture) : null;

            case SettingValueType.Bool:
                switch (value?.ToLowerInvariant())
                {
                    case "true" or "1": return "true";
                    case "false" or "0": return "false";
                    default: error = "true 또는 false 여야 합니다."; return null;
                }

            case SettingValueType.Time:
                if (!TimeOnly.TryParseExact(value, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                { error = "HH:mm 형식이 아닙니다."; return null; }
                return t.ToString(t.Second == 0 ? "HH:mm" : "HH:mm:ss", CultureInfo.InvariantCulture);

            case SettingValueType.Json:
                if (string.IsNullOrEmpty(value)) { error = "JSON 이 비어 있습니다."; return null; }
                try { using var _ = JsonDocument.Parse(value); return value; }
                catch (JsonException) { error = "올바른 JSON 이 아닙니다."; return null; }

            default:
                error = $"알 수 없는 값 형식: {valueType}";
                return null;
        }
    }

    private static bool CheckRange(decimal v, decimal? min, decimal? max, out string? error)
    {
        error = (min, max) switch
        {
            ({ } lo, _) when v < lo => $"{lo.ToString(CultureInfo.InvariantCulture)} 이상이어야 합니다.",
            (_, { } hi) when v > hi => $"{hi.ToString(CultureInfo.InvariantCulture)} 이하여야 합니다.",
            _ => null,
        };
        return error is null;
    }
}
