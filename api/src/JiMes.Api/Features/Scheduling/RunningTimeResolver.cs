using Dapper;
using MySqlConnector;

namespace JiMes.Api.Features.Scheduling;

/// <summary>공통코드 RUNNING_TIME_SOURCE 와 같은 값.</summary>
public static class RunningTimeSource
{
    public const string Standard = "STANDARD";
    public const string PreviousWork = "PREVIOUS_WORK";
    public const string DefaultTime = "DEFAULT_TIME";
    public const string UserInput = "USER_INPUT";
    public const string Setting = "SETTING";
}

/// <param name="Minutes">null 이면 ①~③ 에서 찾지 못함 → 사용자 입력(④) 또는 설정 기본값(⑤)</param>
/// <param name="ChargeQty">작업표준의 1 charge 기준수량 (계획수량 상한)</param>
public sealed record ResolvedRunningTime(decimal? Minutes, string? Source, decimal? ChargeQty);

/// <summary>
/// 품목별 작업시간 결정 (설계 §7, 구 <c>ResolveRunningTime</c>):
/// ① 작업표준 → ② 같은 품목·설비유형의 직전 작업 → ③ 설비(유형) 기준시간.
/// ④ 사용자 입력(기준시간 등록 선택)과 ⑤ 설정 기본값은 호출하는 쪽(API 요청)이 정한다.
/// </summary>
public static class RunningTimeResolver
{
    public static async Task<ResolvedRunningTime> ResolveAsync(
        MySqlConnection conn, MySqlTransaction tx, long partId, long unitProcessId, long equipmentId, long? equipmentTypeId)
    {
        var args = new { partId, unitProcessId, equipmentId, equipmentTypeId };

        // ① 작업표준 현재 버전 — 설비 지정 > 설비유형 지정 > 공통
        var standard = await conn.QueryFirstOrDefaultAsync<(decimal? RunningTimeMin, decimal ChargeQty)>(
            """
            SELECT sv.running_time_min, sv.charge_qty
              FROM standard s
              JOIN standard_version sv ON sv.standard_id = s.standard_id AND sv.is_current = 1
             WHERE s.is_active = 1 AND s.part_id = @partId AND s.unit_process_id = @unitProcessId
               AND (s.equipment_id = @equipmentId
                    OR (s.equipment_id IS NULL AND (s.equipment_type_id <=> @equipmentTypeId OR s.equipment_type_id IS NULL)))
             ORDER BY (s.equipment_id IS NULL), (s.equipment_type_id IS NULL), s.standard_id
             LIMIT 1
            """, args, tx);
        decimal? charge = standard.ChargeQty > 0 ? standard.ChargeQty : null;
        if (standard.RunningTimeMin is > 0)
            return new(standard.RunningTimeMin, RunningTimeSource.Standard, charge);

        // ② 같은 품목·단위공정·설비유형의 가장 최근 완료 작업
        var previous = await conn.ExecuteScalarAsync<decimal?>(
            """
            SELECT COALESCE(w.actual_duration_min, TIMESTAMPDIFF(MINUTE, w.actual_start_at, w.actual_end_at))
              FROM production_work w
              JOIN production_work_input i  ON i.production_work_id = w.production_work_id AND i.status <> 'CANCELLED'
              JOIN sales_order_item soi     ON soi.sales_order_item_id = i.sales_order_item_id
              JOIN equipment e              ON e.equipment_id = w.equipment_id
             WHERE w.status = 'COMPLETED' AND w.is_deleted = 0
               AND soi.part_id = @partId AND w.unit_process_id = @unitProcessId
               AND e.equipment_type_id <=> @equipmentTypeId
               AND w.actual_start_at IS NOT NULL AND w.actual_end_at IS NOT NULL
             ORDER BY w.actual_end_at DESC
             LIMIT 1
            """, args, tx);
        if (previous is > 0)
            return new(previous, RunningTimeSource.PreviousWork, charge);

        // ③ 설비 기준시간 — 설비 지정 > 설비유형, 단위공정 지정 > 공통
        var defaultTime = await conn.ExecuteScalarAsync<decimal?>(
            """
            SELECT running_time_min
              FROM process_default_time
             WHERE (equipment_id = @equipmentId OR (equipment_id IS NULL AND equipment_type_id <=> @equipmentTypeId))
               AND (unit_process_id = @unitProcessId OR unit_process_id IS NULL)
               AND running_time_min > 0
             ORDER BY (equipment_id IS NULL), (unit_process_id IS NULL), process_default_time_id DESC
             LIMIT 1
            """, args, tx);
        if (defaultTime is > 0)
            return new(defaultTime, RunningTimeSource.DefaultTime, charge);

        return new(null, null, charge);
    }
}
