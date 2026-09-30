using System.Globalization;
using System.Text.RegularExpressions;

namespace JiMes.Api.Infrastructure.Numbering;

/// <summary>
/// 번호 형식 설정값(lot.number_format, sales_order.item_number_format …)을 실제 번호로 바꾼다.
/// <list type="bullet">
/// <item><c>{yyMMdd}</c> 처럼 y·M·d 로만 된 토큰 = 기준일 형식</item>
/// <item><c>{SEQ}</c>, <c>{SEQ:000}</c> = 순번 (숫자 형식)</item>
/// <item>그 밖의 <c>{이름}</c> = <paramref name="tokens"/> 값 (예: EQUIP, TYPE)</item>
/// </list>
/// </summary>
public static partial class NumberFormat
{
    public static string Format(string template, DateOnly date, int seq, IReadOnlyDictionary<string, string>? tokens = null) =>
        TokenPattern().Replace(template, m =>
        {
            var name = m.Groups["name"].Value;
            var format = m.Groups["format"].Success ? m.Groups["format"].Value : null;
            if (name == "SEQ")
                return seq.ToString(format ?? "0", CultureInfo.InvariantCulture);
            if (name.All(c => c is 'y' or 'M' or 'd'))
                return date.ToString(name, CultureInfo.InvariantCulture);
            if (tokens is not null && tokens.TryGetValue(name, out var value))
                return value;
            throw new FormatException($"번호 형식 '{template}' 의 토큰 {{{name}}} 값이 없습니다.");
        });

    /// <summary>같은 접두 번호를 찾는 SQL LIKE 패턴 — {SEQ} 자리 = %, 나머지는 문자 그대로 (\ % _ 이스케이프)</summary>
    public static string LikePattern(string template, DateOnly date, IReadOnlyDictionary<string, string>? tokens = null)
    {
        const char marker = '\u0001';
        var text = TokenPattern().Replace(template, m => m.Groups["name"].Value == "SEQ"
            ? marker.ToString()
            : Format(m.Value, date, 0, tokens));
        return text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace(marker, '%');
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z]+)(?::(?<format>[^}]+))?\}")]
    private static partial Regex TokenPattern();
}
