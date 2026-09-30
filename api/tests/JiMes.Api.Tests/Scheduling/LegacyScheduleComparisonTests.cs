using Dapper;
using JiMes.Api.Features.Scheduling;
using JiMes.Api.Infrastructure.Numbering;
using MySqlConnector;

namespace JiMes.Api.Tests.Scheduling;

/// <summary>
/// 구 SP 를 개발 인스턴스의 비교 전용 DB(jimes_legacy_ref_test)에서 실제로 실행해 신규 계산과 1:1 비교한다 (설계 §7, §15.1 검증 방법).
/// 같은 규칙인 경우는 같아야 하고, 신규 설계에서 의도적으로 바꾼 동작은 차이를 명시적으로 확인한다.
/// </summary>
public sealed class LegacyDbFixture : IAsyncLifetime
{
    public const string DatabaseName = "jimes_legacy_ref_test";
    public string ConnectionString { get; } = $"{ApiFixture.ServerConnection};Database={DatabaseName};AllowUserVariables=true";

    public async Task InitializeAsync()
    {
        await using (var conn = new MySqlConnection(ApiFixture.ServerConnection))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync($"DROP DATABASE IF EXISTS `{DatabaseName}`; CREATE DATABASE `{DatabaseName}`");
        }
        await using var db = new MySqlConnection(ConnectionString);
        await db.OpenAsync();
        foreach (var statement in SplitScript(await File.ReadAllTextAsync(ApiFixture.FindRepoFile("db", "test", "legacy", "legacy_schedule_sp.sql"))))
            await db.ExecuteAsync(statement);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>mariadb 클라이언트의 DELIMITER 문법을 해석해 문장 단위로 나눈다.</summary>
    private static IEnumerable<string> SplitScript(string script)
    {
        var delimiter = ";";
        var current = new List<string>();
        foreach (var raw in script.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("DELIMITER ", StringComparison.OrdinalIgnoreCase))
            {
                delimiter = line["DELIMITER ".Length..].Trim();
                continue;
            }
            if (current.Count == 0 && (line.Length == 0 || line.TrimStart().StartsWith("--")))
                continue;
            if (line.EndsWith(delimiter))
            {
                current.Add(line[..^delimiter.Length]);
                yield return string.Join('\n', current);
                current.Clear();
            }
            else
            {
                current.Add(line);
            }
        }
    }
}

public sealed class LegacyScheduleComparisonTests(LegacyDbFixture fx) : IClassFixture<LegacyDbFixture>, IAsyncLifetime
{
    private const int EquipmentId = 1;
    private static readonly DateTime WorkDate = new(2026, 10, 5);
    private static readonly TimeOnly DayStart = new(8, 0);   // 구 코드 하드코딩 값과 같은 설정값으로 비교

    private MySqlConnection _db = null!;

    public async Task InitializeAsync()
    {
        _db = new MySqlConnection(fx.ConnectionString);
        await _db.OpenAsync();
        await _db.ExecuteAsync("DELETE FROM workplan; DELETE FROM t_work;");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<List<(int PlanNo, DateTime Start, DateTime End, DateTime WorkDate)>> RunSpAsync(params decimal[] hours)
    {
        var seq = 1;
        foreach (var h in hours)
        {
            await _db.ExecuteAsync(
                "INSERT INTO workplan (workdate, equipmentid, runningtime, starttime, endtime, sequence) VALUES (@WorkDate, @EquipmentId, @h, @WorkDate, @WorkDate, @seq)",
                new { WorkDate, EquipmentId, h, seq = seq++ });
        }
        await _db.ExecuteAsync("CALL sp_RecalculateWorkPlanTimes(@EquipmentId, @WorkDate, 1)", new { EquipmentId, WorkDate });
        return (await _db.QueryAsync<(int, DateTime, DateTime, DateTime)>(
            "SELECT planno, starttime, endtime, workdate FROM workplan ORDER BY planno")).ToList();
    }

    private static IReadOnlyList<PlannedTime> RunNew(DateTime anchor, params decimal[] hours) =>
        ScheduleCalculator.Calculate(
            hours.Select((h, i) => new PlanBlock(i + 1, h * 60, false, default)).ToList(),
            anchor, new CalendarRules(DayStart, new HashSet<DateOnly>(), []));

    [Fact]
    public async Task Same_day_chain_matches_legacy_sp_and_core()
    {
        var sp = await RunSpAsync(2, 3, 1);
        var anchor = WorkDate.AddHours(8);   // 실적 없음 → SP 는 작업일 08:00
        var result = RunNew(anchor, 2, 3, 1);
        var core = LegacyRecalculateCore.Recalculate([new(1, 2), new(2, 3), new(3, 1)], anchor, "B01", _ => 0);

        Assert.Equal(sp.Select(x => (x.Start, x.End, x.WorkDate)), result.Select(x => (x.Start, x.End, x.WorkDate.ToDateTime(TimeOnly.MinValue))));
        Assert.Equal(core.Select(x => (x.StartTime, x.EndTime, x.WorkDate)), result.Select(x => (x.Start, x.End, x.WorkDate.ToDateTime(TimeOnly.MinValue))));
    }

    [Fact]
    public async Task Start_after_actual_work_matches_legacy_sp()
    {
        await _db.ExecuteAsync("INSERT INTO t_work (equipmentid, starttime, endtime) VALUES (@EquipmentId, @s, @e)",
            new { EquipmentId, s = WorkDate.AddHours(8), e = WorkDate.AddHours(10.5) });
        var sp = await RunSpAsync(2, 1);
        var result = RunNew(WorkDate.AddHours(10.5), 2, 1);   // 신규: 앵커 = 진행·완료 작업 종료
        Assert.Equal(sp.Select(x => (x.Start, x.End)), result.Select(x => (x.Start, x.End)));
    }

    [Fact]
    public async Task Midnight_crossing_matches_legacy_core_but_intentionally_differs_from_sp()
    {
        var anchor = WorkDate.AddHours(8);
        var sp = await RunSpAsync(6, 6, 6, 6);
        var result = RunNew(anchor, 6, 6, 6, 6);
        var core = LegacyRecalculateCore.Recalculate([new(1, 6), new(2, 6), new(3, 6), new(4, 6)], anchor, "B01", _ => 0);

        // 신규 = 구 C# 재계산: 연속 배치, 02:00 시작 블록은 08:00 전이라 전날 작업일
        Assert.Equal(core.Select(x => (x.StartTime, x.EndTime, x.WorkDate)), result.Select(x => (x.Start, x.End, x.WorkDate.ToDateTime(TimeOnly.MinValue))));
        Assert.Equal(WorkDate.AddHours(20), result[2].Start);

        // 구 SP: 자정을 넘기는 블록을 다음날 08:00 으로 점프 (20:00~08:00 공백) — 신규 설계에서 폐기 (휴일·비가동은 달력으로 표현)
        Assert.Equal((WorkDate.AddDays(1).AddHours(8), WorkDate.AddDays(1)), (sp[2].Start, sp[2].WorkDate));
        Assert.NotEqual(sp[2].Start, result[2].Start);
    }

    [Fact]
    public async Task Fractional_hours_are_exact_in_new_service_unlike_legacy_sp()
    {
        var sp = await RunSpAsync(1.5m);
        var result = RunNew(WorkDate.AddHours(8), 1.5m);

        Assert.Equal(WorkDate.AddHours(9.5), result[0].End);   // 신규: 90분 그대로
        // 구 SP: INTERVAL <decimal> HOUR 가 정수 시간으로 반올림됨 → 1.5h 가 2h 로 계산되는 결함
        Assert.Equal(WorkDate.AddHours(10), sp[0].End);
    }

    [Fact]
    public void Temp_lot_numbers_match_legacy_core_with_default_settings()
    {
        // 설정 초기값 schedule.temp_lot_prefix = "P", lot.number_format = "{yyMMdd}-{EQUIP}-{SEQ:000}"
        var anchor = WorkDate.AddHours(20);
        var core = LegacyRecalculateCore.Recalculate([new(1, 6), new(2, 6), new(3, 6)], anchor, "B01", d => d == WorkDate ? 4 : 0);
        var result = RunNew(anchor, 6, 6, 6);
        var tokens = new Dictionary<string, string> { ["EQUIP"] = "B01" };
        var lots = result.GroupBy(r => r.WorkDate).SelectMany(g => g.OrderBy(r => r.Start).Select((r, i) =>
            NumberFormat.Format("P{yyMMdd}-{EQUIP}-{SEQ:000}", g.Key, (g.Key == DateOnly.FromDateTime(WorkDate) ? 4 : 0) + i + 1, tokens)));
        Assert.Equal(core.Select(c => c.TempLotNo), lots);
    }
}
