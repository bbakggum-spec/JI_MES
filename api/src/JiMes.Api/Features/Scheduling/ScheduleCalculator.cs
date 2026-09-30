namespace JiMes.Api.Features.Scheduling;

/// <summary>재계산 대상 블록 (체인 순서대로 넘긴다).</summary>
/// <param name="LockedStart">고정 블록의 현재 계획 시작 — 고정이면 이 시각을 그대로 쓴다.</param>
public sealed record PlanBlock(long Id, decimal DurationMin, bool IsLocked, DateTime LockedStart);

public sealed record TimeRange(DateTime Start, DateTime End);

/// <param name="DayStart">작업일 시작 시각 (첫 교대, 없으면 설정 schedule.day_start_time)</param>
/// <param name="Holidays">휴일 작업일 (work_calendar HOLIDAY) — 이 작업일에는 새 블록을 시작하지 않는다</param>
/// <param name="Blocked">블록이 겹칠 수 없는 구간 (계획 비가동 등)</param>
public sealed record CalendarRules(TimeOnly DayStart, IReadOnlySet<DateOnly> Holidays, IReadOnlyList<TimeRange> Blocked);

public sealed record PlannedTime(long Id, DateTime Start, DateTime End, DateOnly WorkDate);

/// <summary>
/// 스케줄 계산 규칙 (설계 §7) — 설비 1대의 계획 블록 체인에 시각을 배정한다. DB·시계에 의존하지 않는다.
/// <list type="bullet">
/// <item>체인 순서대로 이전 블록 종료에 이어 붙인다. 첫 블록은 <c>anchor</c>(현재 또는 진행 중 작업의 예상 종료)부터.</item>
/// <item>고정 블록(<c>IsLocked</c>)은 시각을 바꾸지 않고, 다른 블록이 겹치지 않는 장애물이 된다.</item>
/// <item>휴일 작업일에는 시작하지 않고 다음 작업일 시작으로 넘긴다. 시작한 블록은 휴일로 넘어가도 끊지 않는다 (열처리 사이클은 중단 불가).</item>
/// <item>계획 비가동과는 겹치지 않게 비가동 종료 뒤로 민다.</item>
/// <item>자정을 넘어도 연속으로 배치한다 — 작업일은 시작 시각이 작업일 시작 이전이면 전날 (구 <c>Hour &lt; 8</c>).</item>
/// </list>
/// </summary>
public static class ScheduleCalculator
{
    // 휴일·비가동이 끝없이 이어지는 잘못된 달력에서 무한 반복을 막는 상한
    private const int MaxSearchSteps = 10_000;

    public static DateOnly WorkDateOf(DateTime at, TimeOnly dayStart)
    {
        var date = DateOnly.FromDateTime(at);
        return TimeOnly.FromDateTime(at) < dayStart ? date.AddDays(-1) : date;
    }

    public static DateTime WorkDayStart(DateOnly workDate, TimeOnly dayStart) => workDate.ToDateTime(dayStart);

    public static TimeSpan Duration(decimal minutes) => TimeSpan.FromMinutes((double)minutes);

    public static IReadOnlyList<PlannedTime> Calculate(IReadOnlyList<PlanBlock> chain, DateTime anchor, CalendarRules rules)
    {
        var obstacles = rules.Blocked
            .Concat(chain.Where(b => b.IsLocked).Select(b => new TimeRange(b.LockedStart, b.LockedStart + Duration(b.DurationMin))))
            .Where(o => o.End > o.Start)
            .OrderBy(o => o.Start)
            .ToList();

        var results = new List<PlannedTime>(chain.Count);
        var cursor = anchor;
        foreach (var block in chain)
        {
            var duration = Duration(block.DurationMin);
            if (block.IsLocked)
            {
                results.Add(new PlannedTime(block.Id, block.LockedStart, block.LockedStart + duration,
                    WorkDateOf(block.LockedStart, rules.DayStart)));
                continue;
            }

            var start = EarliestStart(cursor, duration, obstacles, rules);
            var end = start + duration;
            results.Add(new PlannedTime(block.Id, start, end, WorkDateOf(start, rules.DayStart)));
            cursor = end;
        }

        return results.OrderBy(r => r.Start).ThenBy(r => r.Id).ToList();
    }

    private static DateTime EarliestStart(DateTime from, TimeSpan duration, List<TimeRange> obstacles, CalendarRules rules)
    {
        var t = from;
        for (var step = 0; step < MaxSearchSteps; step++)
        {
            var workDate = WorkDateOf(t, rules.DayStart);
            if (rules.Holidays.Contains(workDate))
            {
                t = WorkDayStart(workDate.AddDays(1), rules.DayStart);
                continue;
            }

            var end = t + duration;
            var hit = obstacles.FirstOrDefault(o => o.Start < end && t < o.End
                                                 // 길이 0 블록도 비가동 안에서 시작하지 않게
                                                 || (duration == TimeSpan.Zero && o.Start <= t && t < o.End));
            if (hit is not null)
            {
                t = hit.End;
                continue;
            }
            return t;
        }
        throw new InvalidOperationException($"{from:yyyy-MM-dd HH:mm} 이후로 배치할 수 있는 시각을 찾지 못했습니다. 휴일·비가동 설정을 확인하세요.");
    }
}
