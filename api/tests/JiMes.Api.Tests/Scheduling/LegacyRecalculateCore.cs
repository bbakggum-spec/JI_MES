namespace JiMes.Api.Tests.Scheduling;

/// <summary>
/// 구 WinForms <c>WorkPlanRepository.RecalculateEquipmentCore</c> (ProductManager\Repository\WorkPlanRepository.cs 2343~2451행)의
/// 계산 부분을 DB 없이 그대로 옮긴 비교 기준. 동작을 바꾸지 않는다 (08시 하드코딩 포함).
/// </summary>
internal static class LegacyRecalculateCore
{
    public sealed record Plan(int PlanNo, decimal RunningTimeHours);

    public sealed record Result(int PlanNo, DateTime StartTime, DateTime EndTime, DateTime WorkDate, int Sequence, string TempLotNo);

    /// <param name="plans">sequence, starttime 순</param>
    /// <param name="cursor">진행 중 작업 종료(현재 이후면) 또는 DateTime.Now</param>
    /// <param name="lastSubNo">작업일별 t_work 마지막 subno</param>
    public static List<Result> Recalculate(IReadOnlyList<Plan> plans, DateTime cursor, string initial, Func<DateTime, int> lastSubNo)
    {
        var timed = new List<(Plan Plan, DateTime Start, DateTime End, DateTime WorkDate)>();
        foreach (var plan in plans)
        {
            var start = cursor;
            var end = cursor.AddHours((double)plan.RunningTimeHours);
            var workDate = start.Hour < 8 ? start.Date.AddDays(-1) : start.Date;
            timed.Add((plan, start, end, workDate));
            cursor = end;
        }

        var results = new List<Result>();
        foreach (var group in timed.GroupBy(t => t.WorkDate).OrderBy(g => g.Key))
        {
            var lotSeq = lastSubNo(group.Key) + 1;
            var planSeq = 1;
            foreach (var t in group.OrderBy(t => t.Start))
            {
                results.Add(new Result(t.Plan.PlanNo, t.Start, t.End, t.WorkDate, planSeq, $"P{t.WorkDate:yyMMdd}-{initial}-{lotSeq:D3}"));
                lotSeq++;
                planSeq++;
            }
        }
        return results;
    }
}
