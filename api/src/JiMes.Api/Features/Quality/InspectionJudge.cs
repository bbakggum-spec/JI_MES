using System.Globalization;

namespace JiMes.Api.Features.Quality;

/// <summary>
/// 측정값 판정 (구 DetermineOverallDecision + 행별 판정) — DB 없는 순수 함수.
/// 기준 판정 방식(공통코드 RANGE_TYPE): BETWEEN 하한~상한 / MIN 하한 이상 / MAX 상한 이하 / NONE 기록만.
/// 숫자 기준이 있고 숫자 측정값이 있으면 자동 판정, 그 밖에는(외관 등) 사용자가 고른 판정.
/// </summary>
public static class InspectionJudge
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string NotApplicable = "NA";

    public sealed record Sample(int SampleNo, decimal? Value, string? Text, string? Result);

    public sealed record Judged(string? Decision, string? Result, IReadOnlyList<Sample> Samples);

    private static readonly string[] RangeTypes = ["BETWEEN", "MIN", "MAX"];

    public static Judged Judge(string? rangeType, decimal? lower, decimal? upper, IReadOnlyList<string?> values, string? manualDecision)
    {
        var samples = new List<Sample>();
        var judged = rangeType is not null && RangeTypes.Contains(rangeType)
                     && (rangeType != "BETWEEN" || (lower is not null && upper is not null))
                     && (rangeType != "MIN" || lower is not null) && (rangeType != "MAX" || upper is not null);
        for (var i = 0; i < values.Count; i++)
        {
            var raw = values[i]?.Trim();
            if (string.IsNullOrEmpty(raw)) continue;
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
            {
                var ok = !judged || ((lower is null || rangeType == "MAX" || v >= lower) && (upper is null || rangeType == "MIN" || v <= upper));
                samples.Add(new Sample(i + 1, v, null, judged ? (ok ? "OK" : "NG") : null));
            }
            else
                samples.Add(new Sample(i + 1, null, raw, null));
        }

        var numbers = samples.Where(s => s.Value is not null).Select(s => s.Value!.Value).ToList();
        var summary = numbers.Count == 0 ? null
            : numbers.Min() == numbers.Max() ? Format(numbers[0]) : $"{Format(numbers.Min())}~{Format(numbers.Max())}";
        string? decision;
        if (judged && numbers.Count > 0)
            decision = samples.Any(s => s.Result == "NG") ? Fail : Pass;
        else
            decision = manualDecision is Pass or Fail or NotApplicable ? manualDecision : null;
        return new Judged(decision, summary, samples);
    }

    /// <summary>검사 종합 판정 — 하나라도 FAIL 이면 FAIL, PASS 가 있으면 PASS, 판정한 항목이 없으면 null</summary>
    public static string? Overall(IEnumerable<string?> itemDecisions)
    {
        var list = itemDecisions.ToList();
        if (list.Contains(Fail)) return Fail;
        return list.Contains(Pass) ? Pass : null;
    }

    private static string Format(decimal d) => d.ToString("0.######", CultureInfo.InvariantCulture);
}
