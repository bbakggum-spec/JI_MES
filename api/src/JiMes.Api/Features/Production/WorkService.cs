using System.Globalization;
using Dapper;
using JiMes.Api.Features.Scheduling;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Production;

/// <summary>
/// 작업(투입) — 구 F_GasForm (단위공정 공통 작업 화면, 설계 §3·§17, legacy_forms/F_GasForm.md).
/// 한 작업 LOT = 단위공정 1회. 투입은 수주번호(주 LOT 전) 또는 주 LOT번호(주 LOT 후)를 스캔해 추가하고,
/// 투입 가능 수량(잔량)은 저장하지 않고 매번 계산한다 (G1·G4). 모든 변경은 작업 LOT row_version 으로 직렬화.
/// </summary>
public sealed class WorkService(
    IDbConnectionFactory db, AuditWriter audit, ICurrentUser currentUser, SettingsCache settings, TimeProvider time, SchedulingService scheduling)
{
    private const string Table = "production_work";

    /// <summary>투입 단계 — 공정 경로에서 이 단위공정의 위치 (설계 §3.3)</summary>
    public static class Phases
    {
        public const string Pre = "PRE";     // 주공정 전 (세척 …) — 수주번호로 투입, 주 LOT 없음
        public const string Main = "MAIN";   // 주공정 (침탄) — 수주번호로 투입, 주 LOT = 자신
        public const string Post = "POST";   // 주공정 후 (템퍼링·쇼트 …) — 주 LOT 투입 행을 골라 투입
    }

    internal const string WorkSql =
        """
        SELECT w.production_work_id, w.lot_no, w.production_schedule_id, w.unit_process_id,
               COALESCE(u.unit_process_name, w.unit_process_name_snapshot) AS unit_process_name, w.equipment_id,
               COALESCE(e.equipment_name, w.equipment_name_snapshot) AS equipment_name, w.is_main_process, w.is_rework,
               w.heat_process_name_snapshot AS heat_process_name, w.work_date, w.status, ps.planned_start_at, ps.planned_end_at,
               w.actual_start_at, w.actual_end_at, w.expected_duration_min, w.actual_duration_min,
               w.standard_version_id, s.standard_name, sv.version_no AS standard_version_no, w.is_standard_fixed, w.standard_fixed_at,
               w.submit_lot_no, w.marking, w.remark,
               (SELECT COUNT(*) FROM production_work_input i WHERE i.production_work_id = w.production_work_id AND i.status <> 'CANCELLED') AS input_count,
               (SELECT COALESCE(SUM(i.input_qty), 0) FROM production_work_input i WHERE i.production_work_id = w.production_work_id AND i.status <> 'CANCELLED') AS input_qty,
               w.row_version
          FROM production_work w
          LEFT JOIN unit_process u ON u.unit_process_id = w.unit_process_id
          LEFT JOIN equipment e ON e.equipment_id = w.equipment_id
          LEFT JOIN production_schedule ps ON ps.production_schedule_id = w.production_schedule_id
          LEFT JOIN standard_version sv ON sv.standard_version_id = w.standard_version_id
          LEFT JOIN standard s ON s.standard_id = sv.standard_id
        """;

    // ───────────────────────── 조회 ─────────────────────────

    /// <summary>작업 화면 왼쪽: 설비별 배정·진행 LOT + 이 작업일 완료 LOT</summary>
    public async Task<object> BoardAsync(long? equipmentTypeId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var dayStart = await scheduling.DayStartAsync(conn, null);
        var workDate = ScheduleCalculator.WorkDateOf(Now(), dayStart).ToDateTime(TimeOnly.MinValue);
        var rows = await conn.QueryAsync<BoardEquipmentRow>(
            """
            SELECT e.equipment_id, e.equipment_name, e.equipment_type_id, t.equipment_type_name
              FROM equipment e LEFT JOIN equipment_type t ON t.equipment_type_id = e.equipment_type_id
             WHERE e.is_active = 1 AND (@equipmentTypeId IS NULL OR e.equipment_type_id = @equipmentTypeId)
             ORDER BY e.sort_order, e.equipment_code
            """, new { equipmentTypeId });
        var works = await conn.QueryAsync<WorkDto>(
            WorkSql + """
             WHERE w.is_deleted = 0 AND (@equipmentTypeId IS NULL OR e.equipment_type_id = @equipmentTypeId)
               AND (w.status IN ('ALLOCATED','INPUT') OR (w.status = 'COMPLETED' AND w.work_date = @workDate))
             ORDER BY w.equipment_id, FIELD(w.status, 'INPUT', 'ALLOCATED', 'COMPLETED'), COALESCE(ps.planned_start_at, w.created_at)
            """, new { equipmentTypeId, workDate });
        return new { workDate, equipment = rows, works };
    }

    private sealed class BoardEquipmentRow
    {
        public long EquipmentId { get; init; }
        public string EquipmentName { get; init; } = "";
        public long? EquipmentTypeId { get; init; }
        public string? EquipmentTypeName { get; init; }
    }

    public async Task<object> DetailAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await DetailCoreAsync(conn, null, id);
    }

    private static async Task<object> DetailCoreAsync(MySqlConnection conn, MySqlTransaction? tx, long id)
    {
        var work = await conn.QuerySingleOrDefaultAsync<WorkDto>(WorkSql + " WHERE w.production_work_id = @id AND w.is_deleted = 0", new { id }, tx)
            ?? throw new NotFoundException(Table, id);
        var inputs = await conn.QueryAsync<WorkInputDto>(
            """
            SELECT pwi.production_work_input_id, pwi.sales_order_item_id, soi.order_item_no, pwi.customer_name_snapshot AS customer_name,
                   pwi.customer_lot_snapshot AS customer_lot, soi.part_id, pwi.part_name_snapshot AS part_name, pwi.part_number_snapshot AS part_number,
                   pwi.main_work_id, mw.lot_no AS main_lot_no, pwi.main_input_id, pwi.is_standard_basis, pwi.input_qty,
                   COALESCE(q.defect_qty, 0) AS defect_qty, COALESCE(q.good_qty, pwi.input_qty) AS good_qty, pwi.tray_mark, pwi.remark,
                   (EXISTS (SELECT 1 FROM production_work_input c WHERE c.main_input_id = pwi.production_work_input_id AND c.status <> 'CANCELLED')
                    OR EXISTS (SELECT 1 FROM inspection_target t WHERE t.production_work_input_id = pwi.production_work_input_id)
                    OR EXISTS (SELECT 1 FROM defect_occurrence d WHERE d.production_work_input_id = pwi.production_work_input_id AND d.is_deleted = 0)) AS is_referenced
              FROM production_work_input pwi
              JOIN sales_order_item soi ON soi.sales_order_item_id = pwi.sales_order_item_id
              LEFT JOIN production_work mw ON mw.production_work_id = pwi.main_work_id
              LEFT JOIN vw_production_work_input_qty q ON q.production_work_input_id = pwi.production_work_input_id
             WHERE pwi.production_work_id = @id AND pwi.status <> 'CANCELLED'
             ORDER BY pwi.production_work_input_id
            """, new { id }, tx);
        var conditions = await conn.QueryAsync<WorkConditionDto>(
            """
            SELECT pc.condition_item_id, ci.condition_item_name, ci.unit_code, ci.value_type, ci.is_active, COALESCE(pc.item_sequence_no, ci.sort_order) AS sort_order,
                   pc.step_sequence_no, pc.step_name_snapshot, pc.set_value
              FROM production_work_condition pc JOIN condition_item ci ON ci.condition_item_id = pc.condition_item_id
             WHERE pc.production_work_id = @id
             ORDER BY COALESCE(pc.item_sequence_no, ci.sort_order), ci.condition_item_id, pc.step_sequence_no
            """, new { id }, tx);
        var events = await conn.QueryAsync<WorkEventDto>(
            """
            SELECT e.event_type, e.event_at, u.user_name, e.remark
              FROM production_work_event e LEFT JOIN app_user u ON u.app_user_id = e.created_by
             WHERE e.production_work_id = @id ORDER BY e.event_at, e.production_work_event_id
            """, new { id }, tx);
        return new { work, inputs, conditions, events };
    }

    // ───────────────────────── 즉시 작업 ─────────────────────────

    public Task<ReleasedLot> CreateAdHocAsync(AdHocWorkRequest r, CancellationToken ct) =>
        scheduling.CreateAdHocAsync(r.EquipmentId, r.UnitProcessId, ct);

    // ───────────────────────── 스캔 · 투입 ─────────────────────────

    private sealed class ItemRow
    {
        public long SalesOrderItemId { get; init; }
        public string OrderItemNo { get; init; } = "";
        public string Status { get; init; } = "";
        public long? HeatProcessVersionId { get; init; }
        public decimal OrderQty { get; init; }
        public decimal? UnitWeight { get; init; }
        public decimal? UnitPrice { get; init; }
        public string? CustomerName { get; init; }
        public string? CustomerLot { get; init; }
        public string? PartName { get; init; }
        public string? PartNumber { get; init; }
        public string? Specification { get; init; }
        public string? Model { get; init; }
        public string? Material { get; init; }
    }

    private const string ItemSql =
        """
        SELECT soi.sales_order_item_id, soi.order_item_no, soi.status, soi.heat_process_version_id, soi.order_qty, soi.unit_weight, soi.unit_price,
               c.customer_name, soi.customer_lot, soi.part_name_snapshot AS part_name, soi.part_number_snapshot AS part_number,
               soi.specification_snapshot AS specification, soi.model_snapshot AS model, soi.material_snapshot AS material
          FROM sales_order_item soi
          JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
          JOIN customer c ON c.customer_id = so.customer_id
        """;

    private sealed class OperationRow
    {
        public long UnitProcessId { get; init; }
        public int SequenceNo { get; init; }
        public bool IsMainProcess { get; init; }
    }

    /// <summary>수주의 공정 경로에서 이 단위공정의 투입 단계 (경로에 없으면 거부)</summary>
    private static async Task<string> PhaseAsync(MySqlConnection conn, MySqlTransaction? tx, ItemRow item, long unitProcessId)
    {
        if (item.HeatProcessVersionId is not { } version)
            throw new BusinessRuleException("ROUTE_MISSING", $"{item.OrderItemNo} 은 공정 경로가 지정되지 않았습니다. 수주에서 공정을 지정하세요.");
        var ops = (await conn.QueryAsync<OperationRow>(
            "SELECT unit_process_id, sequence_no, is_main_process FROM heat_process_operation WHERE heat_process_version_id = @version ORDER BY sequence_no",
            new { version }, tx)).ToList();
        var op = ops.FirstOrDefault(o => o.UnitProcessId == unitProcessId)
            ?? throw new BusinessRuleException("NOT_IN_ROUTE", $"{item.OrderItemNo} 의 공정 경로에 이 단위공정이 없습니다.");
        var main = ops.FirstOrDefault(o => o.IsMainProcess);
        if (main is null) return Phases.Pre;
        if (op.IsMainProcess) return Phases.Main;
        return op.SequenceNo < main.SequenceNo ? Phases.Pre : Phases.Post;
    }

    /// <summary>이 단위공정에 이미 투입한 수량 (재작업·취소 제외) — 주 LOT 전 잔량 계산</summary>
    private static Task<decimal> AlreadyInputAsync(MySqlConnection conn, MySqlTransaction? tx, long itemId, long unitProcessId, long? excludeInputId = null) =>
        conn.ExecuteScalarAsync<decimal>(
            """
            SELECT COALESCE(SUM(pwi.input_qty), 0)
              FROM production_work_input pwi JOIN production_work w ON w.production_work_id = pwi.production_work_id
             WHERE pwi.sales_order_item_id = @itemId AND w.unit_process_id = @unitProcessId AND w.is_rework = 0 AND w.is_deleted = 0
               AND w.status <> 'CANCELLED' AND pwi.status <> 'CANCELLED' AND pwi.production_work_input_id <> @exclude
            """, new { itemId, unitProcessId, exclude = excludeInputId ?? 0 }, tx);

    private sealed class MainRow
    {
        public long ProductionWorkInputId { get; init; }
        public long MainWorkId { get; init; }
        public string MainLotNo { get; init; } = "";
        public long SalesOrderItemId { get; init; }
        public decimal GoodQty { get; init; }
    }

    /// <summary>주 LOT 투입 행 (주공정 LOT 에서 이 수주를 투입한 행) — 양품이 후공정 투입 기준</summary>
    private static Task<IEnumerable<MainRow>> MainRowsAsync(MySqlConnection conn, MySqlTransaction? tx, long? itemId, long? mainWorkId, long? mainInputId = null) =>
        conn.QueryAsync<MainRow>(
            """
            SELECT pwi.production_work_input_id, mw.production_work_id AS main_work_id, mw.lot_no AS main_lot_no, pwi.sales_order_item_id,
                   COALESCE(q.good_qty, pwi.input_qty) AS good_qty
              FROM production_work_input pwi
              JOIN production_work mw ON mw.production_work_id = pwi.production_work_id
              LEFT JOIN vw_production_work_input_qty q ON q.production_work_input_id = pwi.production_work_input_id
             WHERE pwi.main_work_id = pwi.production_work_id AND pwi.status <> 'CANCELLED' AND mw.is_deleted = 0 AND mw.status <> 'CANCELLED'
               AND (@itemId IS NULL OR pwi.sales_order_item_id = @itemId) AND (@mainWorkId IS NULL OR mw.production_work_id = @mainWorkId)
               AND (@mainInputId IS NULL OR pwi.production_work_input_id = @mainInputId)
             ORDER BY mw.work_date, mw.lot_no
            """, new { itemId, mainWorkId, mainInputId }, tx);

    /// <summary>이 단위공정에 주 LOT 투입 행에서 이미 가져간 수량</summary>
    private static Task<decimal> AlreadyFromMainAsync(MySqlConnection conn, MySqlTransaction? tx, long mainInputId, long unitProcessId, long? excludeInputId = null) =>
        conn.ExecuteScalarAsync<decimal>(
            """
            SELECT COALESCE(SUM(pwi.input_qty), 0)
              FROM production_work_input pwi JOIN production_work w ON w.production_work_id = pwi.production_work_id
             WHERE pwi.main_input_id = @mainInputId AND w.unit_process_id = @unitProcessId AND w.is_rework = 0 AND w.is_deleted = 0
               AND w.status <> 'CANCELLED' AND pwi.status <> 'CANCELLED' AND pwi.production_work_input_id <> @exclude
            """, new { mainInputId, unitProcessId, exclude = excludeInputId ?? 0 }, tx);

    private async Task<InputCandidateDto> CandidateOfAsync(MySqlConnection conn, MySqlTransaction? tx, WorkDto work, ItemRow item, string phase, MainRow? main)
    {
        var c = new InputCandidateDto
        {
            SalesOrderItemId = item.SalesOrderItemId, OrderItemNo = item.OrderItemNo, CustomerName = item.CustomerName, PartName = item.PartName,
            PartNumber = item.PartNumber, CustomerLot = item.CustomerLot, MainInputId = main?.ProductionWorkInputId, Phase = phase,
        };
        if (main is null)
        {
            c.MainWorkId = phase == Phases.Main ? work.ProductionWorkId : null;
            c.BaseQty = item.OrderQty;
            c.AlreadyQty = await AlreadyInputAsync(conn, tx, item.SalesOrderItemId, work.UnitProcessId);
        }
        else
        {
            c.MainWorkId = main.MainWorkId;
            c.MainLotNo = main.MainLotNo;
            c.BaseQty = main.GoodQty;
            c.AlreadyQty = await AlreadyFromMainAsync(conn, tx, main.ProductionWorkInputId, work.UnitProcessId);
        }
        c.AlreadyInThisWork = await conn.ExecuteScalarAsync<long>(
            """
            SELECT COUNT(*) FROM production_work_input
             WHERE production_work_id = @workId AND sales_order_item_id = @itemId AND main_key = @mainKey AND status <> 'CANCELLED'
            """, new { workId = work.ProductionWorkId, itemId = item.SalesOrderItemId, mainKey = c.MainWorkId ?? 0 }, tx) > 0;
        return c;
    }

    /// <summary>
    /// 스캔 (구 txtIncomeNo Enter) — 입고번호면 수주 투입, 작업 LOT번호면 그 주 LOT 의 투입 행 목록.
    /// 주공정 후 공정에서 입고번호를 스캔하면 그 수주가 들어간 주 LOT 들을 후보로 (여러 개면 화면이 선택창).
    /// </summary>
    public async Task<ScanResult> ScanAsync(long id, ScanRequest r, CancellationToken ct)
    {
        var code = r.Code?.Trim();
        if (string.IsNullOrEmpty(code)) throw new RequestValidationException("code", "입고번호 또는 LOT번호를 입력하세요.");
        await using var conn = await db.OpenAsync(ct);
        var work = await GetWorkAsync(conn, null, id);

        var item = await conn.QuerySingleOrDefaultAsync<ItemRow>(ItemSql + " WHERE soi.order_item_no = @code", new { code });
        if (item is not null)
        {
            if (item.Status == "CANCELLED") throw new BusinessRuleException("ORDER_ITEM_CANCELLED", $"{code} 은 취소된 입고입니다.");
            var phase = await PhaseAsync(conn, null, item, work.UnitProcessId);
            if (phase != Phases.Post)
                return new ScanResult("ORDER_ITEM", code, [await CandidateOfAsync(conn, null, work, item, phase, null)]);
            var mains = (await MainRowsAsync(conn, null, item.SalesOrderItemId, null)).ToList();
            if (mains.Count == 0)
                throw new BusinessRuleException("NO_MAIN_LOT", $"{code} 은 아직 주공정 LOT 이 없습니다. 주공정을 먼저 투입하세요.");
            var candidates = new List<InputCandidateDto>();
            foreach (var m in mains) candidates.Add(await CandidateOfAsync(conn, null, work, item, phase, m));
            return new ScanResult("ORDER_ITEM", code, candidates);
        }

        var mainWork = await conn.QuerySingleOrDefaultAsync<WorkDto>(WorkSql + " WHERE w.lot_no = @code AND w.is_deleted = 0 AND w.status <> 'CANCELLED'", new { code });
        if (mainWork is null)
            throw new BusinessRuleException("SCAN_NOT_FOUND", $"'{code}' 에 해당하는 입고번호나 작업 LOT 이 없습니다.");
        if (!mainWork.IsMainProcess)
            throw new BusinessRuleException("NOT_MAIN_LOT", $"{code} 은 주공정 LOT 이 아닙니다. 주공정 LOT번호나 입고번호를 스캔하세요.");
        var list = new List<InputCandidateDto>();
        foreach (var m in await MainRowsAsync(conn, null, null, mainWork.ProductionWorkId))
        {
            var mItem = await conn.QuerySingleAsync<ItemRow>(ItemSql + " WHERE soi.sales_order_item_id = @SalesOrderItemId", m);
            var phase = await PhaseAsync(conn, null, mItem, work.UnitProcessId);
            if (phase != Phases.Post)
                throw new BusinessRuleException("NOT_POST_PROCESS", $"이 단위공정은 {mItem.OrderItemNo} 의 주공정 뒤 공정이 아닙니다. 입고번호로 투입하세요.");
            list.Add(await CandidateOfAsync(conn, null, work, mItem, phase, m));
        }
        return new ScanResult("MAIN_LOT", code, list);
    }

    public async Task<object> AddInputAsync(long id, AddInputRequest r, CancellationToken ct)
    {
        if (r.InputQty <= 0) throw new RequestValidationException("inputQty", "투입수량은 0보다 커야 합니다.");
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var work = await LockEditableAsync(conn, tx, id, r.RowVersion);
        // 같은 수주를 동시에 여러 LOT 에 넣는 경우 잔량 계산을 직렬화
        if (await conn.ExecuteScalarAsync<long?>("SELECT sales_order_item_id FROM sales_order_item WHERE sales_order_item_id = @SalesOrderItemId FOR UPDATE", r, tx) is null)
            throw new NotFoundException("sales_order_item", r.SalesOrderItemId);
        var item = await conn.QuerySingleAsync<ItemRow>(ItemSql + " WHERE soi.sales_order_item_id = @SalesOrderItemId", r, tx);
        if (item.Status == "CANCELLED") throw new BusinessRuleException("ORDER_ITEM_CANCELLED", $"{item.OrderItemNo} 은 취소된 입고입니다.");

        var phase = await PhaseAsync(conn, tx, item, work.UnitProcessId);
        MainRow? main = null;
        if (phase == Phases.Post)
        {
            var mains = (await MainRowsAsync(conn, tx, item.SalesOrderItemId, null, r.MainInputId)).ToList();
            if (r.MainInputId is null && mains.Count != 1)
                throw new BusinessRuleException("MAIN_LOT_REQUIRED", mains.Count == 0 ? $"{item.OrderItemNo} 은 아직 주공정 LOT 이 없습니다." : "주 LOT 을 선택하세요.");
            main = mains.FirstOrDefault() ?? throw new BusinessRuleException("MAIN_LOT_REQUIRED", "선택한 주 LOT 투입 행이 없습니다.");
        }
        else if (r.MainInputId is not null)
            throw new BusinessRuleException("NOT_POST_PROCESS", "이 단위공정은 주공정 뒤 공정이 아니라 주 LOT 에서 투입할 수 없습니다.");

        // 한 LOT 의 투입은 같은 단계끼리 (주공정 LOT 에 후공정 투입이 섞이지 않게)
        var existingPhase = await PhaseOfWorkAsync(conn, tx, id);
        if (existingPhase is not null && existingPhase != phase)
            throw new BusinessRuleException("PHASE_MISMATCH", "이 LOT 에 이미 투입된 수주와 공정 단계(주공정 전·주공정·주공정 후)가 다릅니다.");

        var candidate = await CandidateOfAsync(conn, tx, work, item, phase, main);
        if (candidate.AlreadyInThisWork)
            throw new BusinessRuleException("DUPLICATE_INPUT", $"{item.OrderItemNo}{(main is null ? "" : $" ({main.MainLotNo})")} 은 이 LOT 에 이미 투입되어 있습니다. 수량을 고치세요.");
        if (r.InputQty > candidate.RemainingQty)
            throw new BusinessRuleException("INPUT_EXCEEDS_REMAINING", $"투입 가능 수량({candidate.RemainingQty:0.###})을 넘습니다.");

        var inputId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO production_work_input (production_work_id, sales_order_item_id, main_work_id, main_input_id, tray_mark, input_qty, input_weight,
                   unit_price_snapshot, scanned_at, status, customer_name_snapshot, customer_lot_snapshot, part_name_snapshot, part_number_snapshot,
                   specification_snapshot, model_snapshot, material_snapshot, remark, created_by, updated_by)
            VALUES (@id, @SalesOrderItemId, @mainWorkId, @mainInputId, @trayMark, @InputQty, @weight,
                    @UnitPrice, @now, 'INPUT', @CustomerName, @CustomerLot, @PartName, @PartNumber,
                    @Specification, @Model, @Material, @remark, @UserId, @UserId);
            SELECT LAST_INSERT_ID();
            """,
            new
            {
                id, item.SalesOrderItemId, mainWorkId = candidate.MainWorkId, mainInputId = main?.ProductionWorkInputId, trayMark = Trim(r.TrayMark), r.InputQty,
                weight = item.UnitWeight is { } w ? r.InputQty * w : (decimal?)null, item.UnitPrice, now = Now(), item.CustomerName, item.CustomerLot,
                item.PartName, item.PartNumber, item.Specification, item.Model, item.Material, remark = Trim(r.Remark), currentUser.UserId,
            }, tx);
        if (phase == Phases.Main && !work.IsMainProcess)
            await conn.ExecuteAsync("UPDATE production_work SET is_main_process = 1 WHERE production_work_id = @id", new { id }, tx);
        // 즉시 작업 LOT 은 공정 경로가 비어 있다 → 첫 투입 수주의 경로로 (혼적이면 첫 수주 기준, 표시용)
        await conn.ExecuteAsync(
            """
            UPDATE production_work w JOIN sales_order_item soi ON soi.sales_order_item_id = @SalesOrderItemId
               SET w.heat_process_version_id = soi.heat_process_version_id, w.heat_process_name_snapshot = soi.heat_process_name_snapshot
             WHERE w.production_work_id = @id AND w.heat_process_version_id IS NULL
            """, new { id, item.SalesOrderItemId }, tx);
        await BumpAsync(conn, tx, id);
        await audit.WriteAsync(conn, tx, AuditAction.Create, "production_work_input", inputId, null,
            new { lot_no = work.LotNo, item.OrderItemNo, main_lot_no = main?.MainLotNo, r.InputQty, phase });
        await tx.CommitAsync(ct);
        return new { productionWorkInputId = inputId };
    }

    public async Task UpdateInputAsync(long id, long inputId, UpdateInputRequest r, CancellationToken ct)
    {
        if (r.InputQty <= 0) throw new RequestValidationException("inputQty", "투입수량은 0보다 커야 합니다.");
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var work = await LockEditableAsync(conn, tx, id, r.RowVersion);
        var input = await LockInputAsync(conn, tx, id, inputId);
        await conn.ExecuteAsync("SELECT sales_order_item_id FROM sales_order_item WHERE sales_order_item_id = @SalesOrderItemId FOR UPDATE", input, tx);

        var remaining = input.MainInputId is { } mainInputId
            ? (await MainRowsAsync(conn, tx, null, null, mainInputId)).Single().GoodQty - await AlreadyFromMainAsync(conn, tx, mainInputId, work.UnitProcessId, inputId)
            : await conn.ExecuteScalarAsync<decimal>("SELECT order_qty FROM sales_order_item WHERE sales_order_item_id = @SalesOrderItemId", input, tx)
              - await AlreadyInputAsync(conn, tx, input.SalesOrderItemId, work.UnitProcessId, inputId);
        if (r.InputQty > remaining)
            throw new BusinessRuleException("INPUT_EXCEEDS_REMAINING", $"투입 가능 수량({remaining:0.###})을 넘습니다.");
        // 이 행에서 후공정이 가져간 수량 + 부적합 수량 아래로는 줄일 수 없다 (양품 = 투입 − 부적합 ≥ 후공정 투입)
        var used = await conn.ExecuteScalarAsync<decimal>(
            """
            SELECT COALESCE((SELECT SUM(input_qty) FROM production_work_input WHERE main_input_id = @inputId AND status <> 'CANCELLED'), 0)
                 + COALESCE((SELECT SUM(defect_qty) FROM defect_occurrence WHERE production_work_input_id = @inputId AND is_deleted = 0 AND status <> 'CANCELLED'), 0)
            """, new { inputId }, tx);
        if (r.InputQty < used)
            throw new BusinessRuleException("INPUT_BELOW_USED", $"후공정 투입·부적합으로 이미 쓴 수량({used:0.###})보다 줄일 수 없습니다.");

        await conn.ExecuteAsync(
            """
            UPDATE production_work_input
               SET input_qty = @InputQty, input_weight = IF(input_weight IS NULL, NULL, input_weight / NULLIF(input_qty, 0) * @InputQty),
                   tray_mark = @trayMark, remark = @remark, updated_by = @UserId
             WHERE production_work_input_id = @inputId
            """, new { inputId, r.InputQty, trayMark = Trim(r.TrayMark), remark = Trim(r.Remark), currentUser.UserId }, tx);
        await BumpAsync(conn, tx, id);
        await audit.WriteAsync(conn, tx, AuditAction.Update, "production_work_input", inputId, new { input.InputQty }, new { r.InputQty, r.TrayMark, r.Remark });
        await tx.CommitAsync(ct);
    }

    public async Task DeleteInputAsync(long id, long inputId, int rowVersion, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await LockEditableAsync(conn, tx, id, rowVersion);
        var input = await LockInputAsync(conn, tx, id, inputId);
        var referenced = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM production_work_input WHERE main_input_id = @inputId AND status <> 'CANCELLED')
                OR EXISTS (SELECT 1 FROM inspection_target WHERE production_work_input_id = @inputId)
                OR EXISTS (SELECT 1 FROM defect_occurrence WHERE production_work_input_id = @inputId AND is_deleted = 0)
                OR EXISTS (SELECT 1 FROM defect_occurrence WHERE rework_input_id = @inputId AND is_deleted = 0)
            """, new { inputId }, tx);
        if (referenced) throw new BusinessRuleException("INPUT_IN_USE", "후공정 투입·검사·부적합에서 쓰는 투입 행은 지울 수 없습니다.");
        if (input.IsStandardBasis)
            throw new BusinessRuleException("INPUT_IS_STANDARD_BASIS", "표준 확정 기준 품목입니다. 다른 품목으로 표준을 확정한 뒤 지우세요.");
        // 투입 전(배정)·진행 중 LOT 의 오입력 정정 — 참조가 없으므로 행을 지우고 감사 기록에 남긴다
        await conn.ExecuteAsync("DELETE FROM production_work_input WHERE production_work_input_id = @inputId", new { inputId }, tx);
        await BumpAsync(conn, tx, id);
        await audit.WriteAsync(conn, tx, AuditAction.Delete, "production_work_input", inputId,
            new { input.SalesOrderItemId, input.MainInputId, input.InputQty }, null);
        await tx.CommitAsync(ct);
    }

    // ───────────────────────── 시작 · 완료 · 머리 ─────────────────────────

    /// <summary>투입(작업 시작) — 배정 → 투입. 설비당 진행 중 LOT 1건 (DB UNIQUE 와 같은 규칙을 먼저 검사해 알기 쉬운 오류로)</summary>
    public async Task StartAsync(long id, StartWorkRequest r, CancellationToken ct)
    {
        long? equipmentId;
        await using (var conn = await db.OpenAsync(ct))
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            var work = await LockAsync(conn, tx, id, r.RowVersion);
            if (work.Status != "ALLOCATED") throw new BusinessRuleException("WORK_NOT_ALLOCATED", "배정 상태의 LOT 만 투입(시작)할 수 있습니다.");
            if (work.InputCount == 0) throw new BusinessRuleException("NO_INPUT", "투입 품목이 없습니다. 입고번호나 주 LOT 을 먼저 스캔하세요.");
            equipmentId = work.EquipmentId;
            if (equipmentId is not null && await conn.ExecuteScalarAsync<string?>(
                    "SELECT lot_no FROM production_work WHERE equipment_id = @equipmentId AND status = 'INPUT' AND is_deleted = 0 AND production_work_id <> @id LIMIT 1",
                    new { equipmentId, id }, tx) is { } busy)
                throw new BusinessRuleException("EQUIPMENT_BUSY", $"이 설비에서 {busy} 이 진행 중입니다. 완료한 뒤 투입하세요.");
            var start = TrimSeconds(r.StartAt ?? Now());
            await conn.ExecuteAsync(
                "UPDATE production_work SET status = 'INPUT', actual_start_at = @start, updated_by = @UserId, row_version = row_version + 1 WHERE production_work_id = @id",
                new { id, start, currentUser.UserId }, tx);
            await EventAsync(conn, tx, id, "INPUT", start);
            await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id, new { status = work.Status }, new { status = "INPUT", actual_start_at = start });
            await tx.CommitAsync(ct);
        }
        if (equipmentId is { } eq) await scheduling.RecalculateAsync(eq, ct);   // 진행 중 작업의 예상 종료로 뒤 계획 이동
    }

    /// <summary>완료 — 완료시각 기본값 = 지금을 설정 단위(work.complete_time_round_min)로 내림 (구 RoundToNearest5Minutes 동작)</summary>
    public async Task CompleteAsync(long id, CompleteWorkRequest r, CancellationToken ct)
    {
        long? equipmentId;
        await using (var conn = await db.OpenAsync(ct))
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            var work = await LockAsync(conn, tx, id, r.RowVersion);
            if (work.Status != "INPUT") throw new BusinessRuleException("WORK_NOT_STARTED", "투입(진행) 중인 LOT 만 완료할 수 있습니다.");
            equipmentId = work.EquipmentId;
            var end = r.EndAt is { } given ? TrimSeconds(given) : FloorTo(Now(), settings.GetInt(SettingKeys.WorkCompleteTimeRoundMin));
            var start = work.ActualStartAt ?? end;
            // 기본값(내림)이 시작보다 앞이면 (시작 직후 완료) 시작시각으로 — 입력값만 검사
            if (r.EndAt is null && end < start) end = start;
            if (end < start) throw new RequestValidationException("endAt", "완료시각이 시작시각보다 빠릅니다.");
            await conn.ExecuteAsync(
                """
                UPDATE production_work SET status = 'COMPLETED', actual_end_at = @end, actual_duration_min = @minutes, updated_by = @UserId,
                       row_version = row_version + 1
                 WHERE production_work_id = @id
                """, new { id, end, minutes = (decimal)(end - start).TotalMinutes, currentUser.UserId }, tx);
            await EventAsync(conn, tx, id, "COMPLETE", end);
            await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id, new { status = work.Status }, new { status = "COMPLETED", actual_end_at = end });
            await tx.CommitAsync(ct);
        }
        if (equipmentId is { } eq) await scheduling.RecalculateAsync(eq, ct);   // 일찍 끝나면 뒤 계획을 당긴다
    }

    public async Task UpdateAsync(long id, UpdateWorkRequest r, CancellationToken ct)
    {
        if (Longer(r.SubmitLotNo, 100) || Longer(r.Marking, 100)) throw new RequestValidationException("submitLotNo", "100자 이하여야 합니다.");
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id, r.RowVersion);
        if (before.Status == "CANCELLED") throw new BusinessRuleException("WORK_CANCELLED", "취소된 LOT 입니다.");
        await conn.ExecuteAsync(
            """
            UPDATE production_work SET submit_lot_no = @submit, marking = @marking, remark = @remark, updated_by = @UserId, row_version = row_version + 1
             WHERE production_work_id = @id
            """, new { id, submit = Trim(r.SubmitLotNo), marking = Trim(r.Marking), remark = Trim(r.Remark), currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id,
            new { before.SubmitLotNo, before.Marking, before.Remark }, new { submit_lot_no = Trim(r.SubmitLotNo), marking = Trim(r.Marking), remark = Trim(r.Remark) });
        await tx.CommitAsync(ct);
    }

    // ───────────────────────── 표준 확정 · 조건 ─────────────────────────

    private const string StandardCandidateSql =
        """
        SELECT pwi.production_work_input_id, pwi.part_name_snapshot AS part_name, s.standard_id, sv.standard_version_id, s.standard_code, s.standard_name,
               sv.version_no, e.equipment_name, et.equipment_type_name, c.customer_name, sv.running_time_min, sv.charge_qty,
               (CASE WHEN s.equipment_id IS NOT NULL THEN 0 WHEN s.equipment_type_id IS NOT NULL THEN 2 ELSE 4 END
                + CASE WHEN s.customer_id IS NOT NULL THEN 0 ELSE 1 END) AS specificity
          FROM production_work_input pwi
          JOIN production_work w ON w.production_work_id = pwi.production_work_id
          LEFT JOIN equipment we ON we.equipment_id = w.equipment_id
          JOIN sales_order_item soi ON soi.sales_order_item_id = pwi.sales_order_item_id
          JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
          LEFT JOIN heat_process_version hv ON hv.heat_process_version_id = soi.heat_process_version_id
          JOIN standard s ON s.part_id = soi.part_id AND s.unit_process_id = w.unit_process_id AND s.is_active = 1
                         AND (s.equipment_id IS NULL OR s.equipment_id = w.equipment_id)
                         AND (s.equipment_type_id IS NULL OR s.equipment_type_id = we.equipment_type_id)
                         AND (s.customer_id IS NULL OR s.customer_id = so.customer_id)
                         AND (s.heat_process_id IS NULL OR s.heat_process_id = hv.heat_process_id)
          JOIN standard_version sv ON sv.standard_id = s.standard_id AND sv.is_current = 1
          LEFT JOIN equipment e ON e.equipment_id = s.equipment_id
          LEFT JOIN equipment_type et ON et.equipment_type_id = s.equipment_type_id
          LEFT JOIN customer c ON c.customer_id = s.customer_id
         WHERE pwi.production_work_id = @id AND pwi.status <> 'CANCELLED'
        """;

    /// <summary>표준 확정 후보 — 투입 품목마다 조건이 맞는 작업표준 현재 Version (구체적인 것 먼저)</summary>
    public async Task<IEnumerable<StandardCandidateDto>> StandardCandidatesAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.QueryAsync<StandardCandidateDto>(StandardCandidateSql + " ORDER BY pwi.production_work_input_id, specificity, s.standard_code", new { id });
    }

    /// <summary>
    /// 표준 확정 (구 FixStandard) — 투입 품목 1개의 작업표준으로 LOT 조건을 정한다: 기준 품목 표시, 표준 Version·단계 템플릿 기록,
    /// 입력표(관리항목 × [공통 + 스텝]) 를 LOT 조건으로 복사(수정 가능), 예상 작업시간 = 표준 작업시간. 다시 확정하면 조건을 새로 복사.
    /// </summary>
    public async Task FixStandardAsync(long id, FixStandardRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var work = await LockEditableAsync(conn, tx, id, r.RowVersion);
        var candidate = await conn.QuerySingleOrDefaultAsync<StandardCandidateDto>(
            StandardCandidateSql + " AND pwi.production_work_input_id = @inputId AND sv.standard_version_id = @versionId LIMIT 1",
            new { id, inputId = r.ProductionWorkInputId, versionId = r.StandardVersionId }, tx)
            ?? throw new BusinessRuleException("STANDARD_NOT_APPLICABLE", "이 LOT 의 투입 품목·단위공정·설비에 맞는 작업표준이 아닙니다.");

        await conn.ExecuteAsync("UPDATE production_work_input SET is_standard_basis = 0 WHERE production_work_id = @id AND is_standard_basis = 1", new { id }, tx);
        await conn.ExecuteAsync("UPDATE production_work_input SET is_standard_basis = 1 WHERE production_work_input_id = @inputId", new { inputId = r.ProductionWorkInputId }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE production_work w JOIN standard_version sv ON sv.standard_version_id = @versionId
               SET w.standard_version_id = sv.standard_version_id, w.step_template_id = sv.step_template_id, w.is_standard_fixed = 1,
                   w.standard_fixed_at = @now, w.expected_duration_min = COALESCE(sv.running_time_min, w.expected_duration_min),
                   w.updated_by = @UserId, w.row_version = w.row_version + 1
             WHERE w.production_work_id = @id
            """, new { id, versionId = r.StandardVersionId, now = Now(), currentUser.UserId }, tx);

        // 표준 입력표 → LOT 조건: 관리항목(행 순서) × [공통 + 스텝], 값 없는 칸도 행으로 남겨 표 모양을 보존
        await conn.ExecuteAsync("DELETE FROM production_work_condition WHERE production_work_id = @id", new { id }, tx);
        await conn.ExecuteAsync(
            """
            INSERT INTO production_work_condition (production_work_id, condition_item_id, item_sequence_no, step_sequence_no, step_name_snapshot, set_value)
            SELECT @id, vi.condition_item_id, vi.sequence_no, NULL, NULL, sc.condition_value
              FROM standard_version_item vi
              LEFT JOIN standard_condition sc ON sc.standard_version_id = vi.standard_version_id AND sc.condition_item_id = vi.condition_item_id AND sc.step_no IS NULL
             WHERE vi.standard_version_id = @versionId
            UNION ALL
            SELECT @id, vi.condition_item_id, vi.sequence_no, st.sequence_no, st.step_name, sc.condition_value
              FROM standard_version_item vi
              JOIN standard_version_step st ON st.standard_version_id = vi.standard_version_id
              LEFT JOIN standard_condition sc ON sc.standard_version_id = vi.standard_version_id AND sc.condition_item_id = vi.condition_item_id
                                             AND sc.step_no = st.sequence_no
             WHERE vi.standard_version_id = @versionId
            """, new { id, versionId = r.StandardVersionId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id,
            new { standard_version_id = work.StandardVersionId },
            new { standard_version_id = r.StandardVersionId, candidate.StandardCode, candidate.VersionNo, basis_input_id = r.ProductionWorkInputId });
        await tx.CommitAsync(ct);
    }

    /// <summary>LOT 조건 수정 (표준에서 복사한 값 수정·스텝·항목 가변, 작업표준과 같은 입력표) — 완료 전까지</summary>
    public async Task SaveConditionsAsync(long id, SaveConditionsRequest r, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var steps = (r.Steps ?? []).Select(s => s?.Trim() ?? "").ToList();
        if (steps.Any(s => s.Length is 0 or > 100)) errors["steps"] = ["스텝 이름은 1~100자여야 합니다."];
        var rowIds = (r.Items ?? []).ToList();
        if (rowIds.Distinct().Count() != rowIds.Count) errors["items"] = ["같은 관리항목이 두 번 있습니다."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await LockEditableAsync(conn, tx, id, r.RowVersion);
        var types = rowIds.Count == 0 ? new Dictionary<long, string>() : (await conn.QueryAsync<(long Id, string ValueType)>(
            "SELECT CAST(condition_item_id AS SIGNED), value_type FROM condition_item WHERE condition_item_id IN @ids", new { ids = rowIds.ToArray() }, tx))
            .ToDictionary(x => x.Id, x => x.ValueType);
        if (rowIds.Any(x => !types.ContainsKey(x))) throw new RequestValidationException("items", "없는 조건 항목이 있습니다.");
        var values = new Dictionary<(int, long), string>();
        foreach (var c in r.Conditions ?? [])
        {
            var v = c.ConditionValue?.Trim();
            if (string.IsNullOrEmpty(v)) continue;
            if (!types.TryGetValue(c.ConditionItemId, out var type)) throw new RequestValidationException("conditions", "입력표 행에 없는 관리항목의 값이 있습니다.");
            if (c.StepNo is { } s && (s < 1 || s > steps.Count)) throw new RequestValidationException("conditions", "입력표에 없는 스텝의 값이 있습니다.");
            if (v.Length > 100) throw new RequestValidationException("conditions", "조건값은 100자 이하여야 합니다.");
            if (type == "NUMBER" && !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                throw new RequestValidationException("conditions", $"숫자 항목에 숫자가 아닌 값이 있습니다: '{v}'");
            values[(c.StepNo ?? 0, c.ConditionItemId)] = v;
        }

        await conn.ExecuteAsync("DELETE FROM production_work_condition WHERE production_work_id = @id", new { id }, tx);
        for (var i = 0; i < rowIds.Count; i++)
            for (var s = 0; s <= steps.Count; s++)
                await conn.ExecuteAsync(
                    """
                    INSERT INTO production_work_condition (production_work_id, condition_item_id, item_sequence_no, step_sequence_no, step_name_snapshot, set_value)
                    VALUES (@id, @item, @seq, @stepNo, @stepName, @value)
                    """,
                    new { id, item = rowIds[i], seq = i + 1, stepNo = s == 0 ? (int?)null : s, stepName = s == 0 ? null : steps[s - 1], value = values.GetValueOrDefault((s, rowIds[i])) }, tx);
        await BumpAsync(conn, tx, id);
        await audit.WriteAsync(conn, tx, AuditAction.Update, "production_work_condition", id, null, new { steps, items = rowIds, values = values.Count });
        await tx.CommitAsync(ct);
    }

    // ───────────────────────── 공통 ─────────────────────────

    private static async Task<WorkDto> GetWorkAsync(MySqlConnection conn, MySqlTransaction? tx, long id) =>
        await conn.QuerySingleOrDefaultAsync<WorkDto>(WorkSql + " WHERE w.production_work_id = @id AND w.is_deleted = 0", new { id }, tx)
        ?? throw new NotFoundException(Table, id);

    /// <summary>작업 LOT 잠금 + 클라이언트가 본 버전 확인</summary>
    private static async Task<WorkDto> LockAsync(MySqlConnection conn, MySqlTransaction tx, long id, int rowVersion)
    {
        var version = await conn.ExecuteScalarAsync<int?>(
            "SELECT row_version FROM production_work WHERE production_work_id = @id AND is_deleted = 0 FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException(Table, id);
        if (version != rowVersion) throw new ConcurrencyConflictException(Table, id);
        return await GetWorkAsync(conn, tx, id);
    }

    /// <summary>투입·조건을 고칠 수 있는 LOT (배정·진행 중) — 완료 후 잠금 (G12)</summary>
    private static async Task<WorkDto> LockEditableAsync(MySqlConnection conn, MySqlTransaction tx, long id, int rowVersion)
    {
        var work = await LockAsync(conn, tx, id, rowVersion);
        if (work.Status is not ("ALLOCATED" or "INPUT"))
            throw new BusinessRuleException("WORK_NOT_EDITABLE", "완료되었거나 취소된 LOT 은 투입·조건을 바꿀 수 없습니다.");
        return work;
    }

    private sealed class InputRow
    {
        public long SalesOrderItemId { get; init; }
        public long? MainInputId { get; init; }
        public decimal InputQty { get; init; }
        public bool IsStandardBasis { get; init; }
    }

    private static async Task<InputRow> LockInputAsync(MySqlConnection conn, MySqlTransaction tx, long id, long inputId) =>
        await conn.QuerySingleOrDefaultAsync<InputRow>(
            """
            SELECT sales_order_item_id, main_input_id, input_qty, is_standard_basis FROM production_work_input
             WHERE production_work_input_id = @inputId AND production_work_id = @id AND status <> 'CANCELLED' FOR UPDATE
            """, new { id, inputId }, tx)
        ?? throw new NotFoundException("production_work_input", inputId);

    /// <summary>이미 투입된 행으로 본 이 LOT 의 단계 (없으면 null)</summary>
    private static Task<string?> PhaseOfWorkAsync(MySqlConnection conn, MySqlTransaction tx, long id) =>
        conn.ExecuteScalarAsync<string?>(
            """
            SELECT CASE WHEN main_work_id IS NULL THEN 'PRE' WHEN main_work_id = production_work_id THEN 'MAIN' ELSE 'POST' END
              FROM production_work_input WHERE production_work_id = @id AND status <> 'CANCELLED' LIMIT 1
            """, new { id }, tx);

    private Task BumpAsync(MySqlConnection conn, MySqlTransaction tx, long id) =>
        conn.ExecuteAsync("UPDATE production_work SET row_version = row_version + 1, updated_by = @UserId WHERE production_work_id = @id",
            new { id, currentUser.UserId }, tx);

    private Task EventAsync(MySqlConnection conn, MySqlTransaction tx, long id, string type, DateTime at) =>
        conn.ExecuteAsync("INSERT INTO production_work_event (production_work_id, event_type, event_at, created_by) VALUES (@id, @type, @at, @UserId)",
            new { id, type, at, currentUser.UserId }, tx);

    private DateTime Now() => time.GetLocalNow().DateTime;
    private static DateTime TrimSeconds(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);

    private static DateTime FloorTo(DateTime t, int minutes)
    {
        var trimmed = TrimSeconds(t);
        return minutes <= 1 ? trimmed : trimmed.AddMinutes(-((trimmed.Hour * 60 + trimmed.Minute) % minutes));
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static bool Longer(string? s, int max) => s is not null && s.Trim().Length > max;
}
