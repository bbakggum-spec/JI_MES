using JiMes.Api.Features.Scheduling;
using JiMes.Api.Infrastructure.Numbering;

namespace JiMes.Api.Tests.Scheduling;

public sealed class ScheduleCalculatorTests
{
    private static readonly TimeOnly DayStart = new(8, 0);
    private static readonly DateTime Oct5 = new(2026, 10, 5);

    private static CalendarRules Rules(IEnumerable<DateOnly>? holidays = null, IEnumerable<TimeRange>? blocked = null) =>
        new(DayStart, (holidays ?? []).ToHashSet(), (blocked ?? []).ToList());

    private static PlanBlock Block(long id, decimal minutes, DateTime? lockedAt = null) =>
        new(id, minutes, lockedAt is not null, lockedAt ?? default);

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    [Fact]
    public void Blocks_chain_from_anchor_in_order()
    {
        var r = ScheduleCalculator.Calculate([Block(1, 120), Block(2, 90), Block(3, 30)], At(5, 9), Rules());
        Assert.Equal([(1L, At(5, 9), At(5, 11)), (2L, At(5, 11), At(5, 12, 30)), (3L, At(5, 12, 30), At(5, 13))],
            r.Select(x => (x.Id, x.Start, x.End)));
    }

    [Fact]
    public void Crossing_midnight_is_continuous_and_work_date_follows_day_start()
    {
        // 20:00~02:00 은 10-05 작업일, 02:00 에 시작한 블록도 08:00 이전이므로 10-05 작업일 (구 Hour < 8)
        var r = ScheduleCalculator.Calculate([Block(1, 360), Block(2, 360), Block(3, 60)], At(5, 20), Rules());
        Assert.Equal((At(6, 2), DateOnly.FromDateTime(Oct5)), (r[1].Start, r[1].WorkDate));
        Assert.Equal((At(6, 8), new DateOnly(2026, 10, 6)), (r[2].Start, r[2].WorkDate));
    }

    [Fact]
    public void Holiday_work_date_is_skipped_but_running_block_is_not_cut()
    {
        var holidays = new[] { new DateOnly(2026, 10, 6) };
        // 10-05 작업일 안에서 시작한 블록은 휴일(10-06 08:00~)로 넘어가도 끊지 않는다
        var r = ScheduleCalculator.Calculate([Block(1, 240), Block(2, 60)], At(6, 6), Rules(holidays));
        Assert.Equal((At(6, 6), At(6, 10)), (r[0].Start, r[0].End));
        // 다음 블록은 휴일 작업일에 시작하지 않고 10-07 08:00
        Assert.Equal(At(7, 8), r[1].Start);
    }

    [Fact]
    public void Planned_downtime_is_not_overlapped()
    {
        var down = new TimeRange(At(5, 12), At(5, 13));
        var r = ScheduleCalculator.Calculate([Block(1, 120), Block(2, 120)], At(5, 9), Rules(blocked: [down]));
        Assert.Equal(At(5, 9), r[0].Start);                 // 09~11
        Assert.Equal(At(5, 13), r[1].Start);                // 11~13 은 비가동과 겹침 → 13:00
    }

    [Fact]
    public void Locked_block_keeps_time_and_others_flow_around_it()
    {
        var locked = At(5, 11);
        var r = ScheduleCalculator.Calculate([Block(1, 60), Block(2, 120, locked), Block(3, 120), Block(4, 30)], At(5, 9), Rules());
        var byId = r.ToDictionary(x => x.Id);
        Assert.Equal((At(5, 11), At(5, 13)), (byId[2].Start, byId[2].End));   // 고정
        Assert.Equal(At(5, 9), byId[1].Start);
        Assert.Equal(At(5, 13), byId[3].Start);   // 10:00 에 시작하면 고정 블록과 겹침 → 고정 종료 뒤
        Assert.Equal(At(5, 15), byId[4].Start);
        Assert.Equal([1L, 2L, 3L, 4L], r.Select(x => x.Id));   // 결과는 시작 순
    }

    [Fact]
    public void Short_block_fills_gap_before_locked_block()
    {
        var r = ScheduleCalculator.Calculate([Block(1, 60), Block(2, 60, At(5, 12))], At(5, 9), Rules());
        Assert.Equal(At(5, 9), r.Single(x => x.Id == 1).Start);
    }

    [Fact]
    public void Endless_holidays_fail_instead_of_looping()
    {
        var holidays = Enumerable.Range(0, 20_000).Select(i => new DateOnly(2026, 10, 5).AddDays(i));
        Assert.Throws<InvalidOperationException>(() => ScheduleCalculator.Calculate([Block(1, 60)], At(5, 9), Rules(holidays)));
    }

    [Theory]
    [InlineData("P{yyMMdd}-{EQUIP}-{SEQ:000}", "P261005-B01-007")]
    [InlineData("{yyMMdd}-{EQUIP}-{SEQ:000}", "261005-B01-007")]
    [InlineData("I{yyMMdd}-{SEQ:000}", "I261005-007")]
    [InlineData("{EQUIP}{yyyyMMdd}{SEQ}", "B01202610057")]
    public void Number_format_tokens(string format, string expected) =>
        Assert.Equal(expected, NumberFormat.Format(format, new DateOnly(2026, 10, 5), 7, new Dictionary<string, string> { ["EQUIP"] = "B01" }));

    [Fact]
    public void Number_format_unknown_token_fails() =>
        Assert.Throws<FormatException>(() => NumberFormat.Format("{TYPE}-{SEQ}", new DateOnly(2026, 10, 5), 1));
}
