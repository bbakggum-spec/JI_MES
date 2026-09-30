using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Numbering;
using JiMes.Api.Infrastructure.Realtime;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Scheduling;

public sealed record CreateBlockRequest(
    long SalesOrderItemId, long UnitProcessId, long EquipmentId, long? BeforeBlockId,
    decimal? DurationMin, bool SaveAsDefault, bool UseDefaultDuration, string? Reason);

public sealed record MoveBlockRequest(int RowVersion, long EquipmentId, long? BeforeBlockId, string? Reason);

public sealed record MergeItemRequest(
    int RowVersion, long SalesOrderItemId, decimal? DurationMin, bool SaveAsDefault, bool UseDefaultDuration, string? Reason);

public sealed record VersionedRequest(int RowVersion, string? Reason);

public sealed record LockBlockRequest(int RowVersion, bool Locked, string? Reason);

public sealed record ConfirmBlockRequest(int RowVersion, bool Confirmed, string? Reason);

/// <param name="IncludePrevious">같은 설비 체인에서 이 계획보다 앞선 계획도 함께 작업지시</param>
public sealed record ReleaseBlockRequest(int RowVersion, bool IncludePrevious, string? Reason);

public sealed record ReleasedLot(long ProductionScheduleId, long ProductionWorkId, string LotNo);

/// <summary>
/// 스케줄 계산의 유일한 위치 (설계 §7, §15.1 S1). 모든 변경은
/// 설비 행 잠금(<c>SELECT … FOR UPDATE</c>) → 사용자 의도 저장(row_version 확인·증가) → 설비 체인 재계산 → 커밋 → SignalR 순서로 한 트랜잭션에서 처리한다.
/// 재계산이 바꾸는 시각·순번·임시 LOT은 파생값이라 row_version 을 올리지 않는다 (지연 반영 재계산이 사용자 편집을 409 로 만들지 않게).
/// </summary>
public sealed class SchedulingService(
    IDbConnectionFactory db, SettingsCache settings, AuditWriter audit, EventPublisher events,
    ICurrentUser currentUser, TimeProvider time)
{
    public const string ActiveStatuses = "'PLANNED','CONFIRMED'";
    private const string Table = "production_schedule";

    internal sealed class BlockRow
    {
        public long ProductionScheduleId { get; init; }
        public long EquipmentId { get; init; }
        public long UnitProcessId { get; init; }
        public DateTime WorkDate { get; init; }
        public int SequenceNo { get; init; }
        public decimal PlannedQty { get; init; }
        public decimal PlannedDurationMin { get; init; }
        public string? DurationSource { get; init; }
        public DateTime PlannedStartAt { get; init; }
        public DateTime PlannedEndAt { get; init; }
        public string? PlannedLotNo { get; init; }
        public bool IsTimeLocked { get; init; }
        public string Status { get; init; } = "";
        public int RowVersion { get; init; }
    }

    private const string BlockColumns =
        """
        production_schedule_id, equipment_id, unit_process_id, work_date, sequence_no, planned_qty, planned_duration_min,
        duration_source, planned_start_at, planned_end_at, planned_lot_no, is_time_locked, status, row_version
        """;

    // ───────────────────────── 사용자 조작 ─────────────────────────

    public async Task<long> CreateAsync(CreateBlockRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var equipment = await LockEquipmentAsync(conn, tx, [r.EquipmentId]);
        var target = equipment[r.EquipmentId];

        var item = await LoadPlanItemAsync(conn, tx, r.SalesOrderItemId, r.UnitProcessId);
        var resolved = await RunningTimeResolver.ResolveAsync(conn, tx, item.PartId, r.UnitProcessId, r.EquipmentId, target.EquipmentTypeId);
        var (minutes, source) = await DecideDurationAsync(conn, tx, resolved, r.DurationMin, r.SaveAsDefault, r.UseDefaultDuration,
            r.UnitProcessId, target.EquipmentTypeId, r.EquipmentId);
        var qty = resolved.ChargeQty is { } charge ? Math.Min(item.RemainingQty, charge) : item.RemainingQty;

        var chain = await LoadChainAsync(conn, tx, r.EquipmentId);
        var order = chain.Select(b => b.ProductionScheduleId).ToList();
        var insertAt = PositionOf(order, r.BeforeBlockId);

        var now = Now();
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO production_schedule
                (work_date, equipment_id, unit_process_id, sequence_no, planned_qty, planned_duration_min, duration_source,
                 planned_start_at, planned_end_at, status, created_by, updated_by)
            VALUES (@workDate, @EquipmentId, @UnitProcessId, 0, @qty, @minutes, @source, @now, @now, 'PLANNED', @userId, @userId);
            SELECT LAST_INSERT_ID();
            """,
            new { workDate = now.Date, r.EquipmentId, r.UnitProcessId, qty, minutes, source, now, userId = currentUser.UserId }, tx);
        await conn.ExecuteAsync(
            "INSERT INTO production_schedule_item (production_schedule_id, sales_order_item_id, planned_qty) VALUES (@id, @itemId, @qty)",
            new { id, itemId = r.SalesOrderItemId, qty }, tx);

        order.Insert(insertAt, id);
        await RecalculateCoreAsync(conn, tx, r.EquipmentId, order);
        await audit.WriteAsync(conn, tx, AuditAction.Create, Table, id, null,
            new { equipment_id = r.EquipmentId, unit_process_id = r.UnitProcessId, sales_order_item_id = r.SalesOrderItemId,
                  planned_qty = qty, planned_duration_min = minutes, duration_source = source, before_block_id = r.BeforeBlockId },
            r.Reason);
        await tx.CommitAsync(ct);
        await PublishAsync([r.EquipmentId], ct);
        return id;
    }

    public async Task MoveAsync(long id, MoveBlockRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var sourceEquipmentId = await EquipmentOfAsync(conn, tx, id);
        await LockEquipmentAsync(conn, tx, [sourceEquipmentId, r.EquipmentId]);
        var block = await LoadEditableAsync(conn, tx, id, r.RowVersion);
        if (block.IsTimeLocked)
            throw new BusinessRuleException("BLOCK_LOCKED", "고정된 계획은 이동할 수 없습니다. 고정을 먼저 해제하세요.");
        if (r.BeforeBlockId == id)
            return;

        await conn.ExecuteVersionedUpdateAsync(
            """
            UPDATE production_schedule SET equipment_id = @EquipmentId, updated_by = @UserId, row_version = row_version + 1
             WHERE production_schedule_id = @Id AND row_version = @RowVersion
            """,
            new { Id = id, r.EquipmentId, r.RowVersion, currentUser.UserId }, Table, id, tx);

        var targetOrder = (await LoadChainAsync(conn, tx, r.EquipmentId))
            .Select(b => b.ProductionScheduleId).Where(x => x != id).ToList();
        targetOrder.Insert(PositionOf(targetOrder, r.BeforeBlockId), id);
        await RecalculateCoreAsync(conn, tx, r.EquipmentId, targetOrder);
        if (sourceEquipmentId != r.EquipmentId)
            await RecalculateCoreAsync(conn, tx, sourceEquipmentId);

        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id,
            new { equipment_id = sourceEquipmentId, sequence_no = block.SequenceNo, work_date = block.WorkDate },
            new { equipment_id = r.EquipmentId, before_block_id = r.BeforeBlockId }, r.Reason);
        await tx.CommitAsync(ct);
        await PublishAsync([sourceEquipmentId, r.EquipmentId], ct);
    }

    /// <summary>블록에 수주품목 추가 (병합). 작업시간 = 담긴 품목 작업시간 중 최대값 (설계 §7, 구 비례 계산 폐기).</summary>
    public async Task MergeAsync(long id, MergeItemRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var equipmentId = await EquipmentOfAsync(conn, tx, id);
        var equipment = await LockEquipmentAsync(conn, tx, [equipmentId]);
        var block = await LoadEditableAsync(conn, tx, id, r.RowVersion);

        if (await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM production_schedule_item WHERE production_schedule_id = @id AND sales_order_item_id = @itemId",
                new { id, itemId = r.SalesOrderItemId }, tx) > 0)
            throw new BusinessRuleException("ALREADY_IN_BLOCK", "이미 이 계획에 담긴 수주입니다.");

        var item = await LoadPlanItemAsync(conn, tx, r.SalesOrderItemId, block.UnitProcessId);
        var resolved = await RunningTimeResolver.ResolveAsync(conn, tx, item.PartId, block.UnitProcessId, equipmentId,
            equipment[equipmentId].EquipmentTypeId);
        var (minutes, source) = await DecideDurationAsync(conn, tx, resolved, r.DurationMin, r.SaveAsDefault, r.UseDefaultDuration,
            block.UnitProcessId, equipment[equipmentId].EquipmentTypeId, equipmentId);
        var qty = resolved.ChargeQty is { } charge ? Math.Min(item.RemainingQty, charge) : item.RemainingQty;

        var (newMinutes, newSource) = minutes > block.PlannedDurationMin
            ? (minutes, source)
            : (block.PlannedDurationMin, block.DurationSource);

        await conn.ExecuteAsync(
            "INSERT INTO production_schedule_item (production_schedule_id, sales_order_item_id, planned_qty) VALUES (@id, @itemId, @qty)",
            new { id, itemId = r.SalesOrderItemId, qty }, tx);
        await conn.ExecuteVersionedUpdateAsync(
            """
            UPDATE production_schedule
               SET planned_qty = planned_qty + @Qty, planned_duration_min = @Minutes, duration_source = @Source,
                   updated_by = @UserId, row_version = row_version + 1
             WHERE production_schedule_id = @Id AND row_version = @RowVersion
            """,
            new { Id = id, Qty = qty, Minutes = newMinutes, Source = newSource, r.RowVersion, currentUser.UserId }, Table, id, tx);

        await RecalculateCoreAsync(conn, tx, equipmentId);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id,
            new { planned_qty = block.PlannedQty, planned_duration_min = block.PlannedDurationMin },
            new { merged_sales_order_item_id = r.SalesOrderItemId, added_qty = qty, planned_duration_min = newMinutes, duration_source = newSource },
            r.Reason);
        await tx.CommitAsync(ct);
        await PublishAsync([equipmentId], ct);
    }

    /// <summary>계획 취소 (구 배정 회수). 계획수량이 배정 대기로 돌아간다 (잔량은 계산값).</summary>
    public async Task CancelAsync(long id, VersionedRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var equipmentId = await EquipmentOfAsync(conn, tx, id);
        await LockEquipmentAsync(conn, tx, [equipmentId]);
        var block = await LoadEditableAsync(conn, tx, id, r.RowVersion);

        await conn.ExecuteVersionedUpdateAsync(
            """
            UPDATE production_schedule SET status = 'CANCELLED', updated_by = @UserId, row_version = row_version + 1
             WHERE production_schedule_id = @Id AND row_version = @RowVersion
            """,
            new { Id = id, r.RowVersion, currentUser.UserId }, Table, id, tx);
        await RecalculateCoreAsync(conn, tx, equipmentId);
        await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id,
            new { status = block.Status }, new { status = "CANCELLED" }, r.Reason);
        await tx.CommitAsync(ct);
        await PublishAsync([equipmentId], ct);
    }

    /// <summary>시각 고정/해제. 고정 블록은 재계산에서 움직이지 않고 다른 블록이 비켜 간다.</summary>
    public async Task SetLockAsync(long id, LockBlockRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var equipmentId = await EquipmentOfAsync(conn, tx, id);
        await LockEquipmentAsync(conn, tx, [equipmentId]);
        var block = await LoadEditableAsync(conn, tx, id, r.RowVersion);

        await conn.ExecuteVersionedUpdateAsync(
            """
            UPDATE production_schedule SET is_time_locked = @Locked, updated_by = @UserId, row_version = row_version + 1
             WHERE production_schedule_id = @Id AND row_version = @RowVersion
            """,
            new { Id = id, r.Locked, r.RowVersion, currentUser.UserId }, Table, id, tx);
        await RecalculateCoreAsync(conn, tx, equipmentId);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id,
            new { is_time_locked = block.IsTimeLocked, planned_start_at = block.PlannedStartAt },
            new { is_time_locked = r.Locked }, r.Reason);
        await tx.CommitAsync(ct);
        await PublishAsync([equipmentId], ct);
    }

    /// <summary>계획 확정/확정 해제 (PLANNED ↔ CONFIRMED). 확정해도 재계산 대상이며 수정 가능 — 현장에 "확정된 계획"을 알리는 표시.</summary>
    public async Task ConfirmAsync(long id, ConfirmBlockRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var equipmentId = await EquipmentOfAsync(conn, tx, id);
        await LockEquipmentAsync(conn, tx, [equipmentId]);
        var block = await LoadEditableAsync(conn, tx, id, r.RowVersion);
        var status = r.Confirmed ? "CONFIRMED" : "PLANNED";
        if (block.Status == status) return;

        await conn.ExecuteVersionedUpdateAsync(
            """
            UPDATE production_schedule SET status = @status, updated_by = @UserId, row_version = row_version + 1
             WHERE production_schedule_id = @Id AND row_version = @RowVersion
            """,
            new { Id = id, status, r.RowVersion, currentUser.UserId }, Table, id, tx);
        await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id, new { status = block.Status }, new { status }, r.Reason);
        await tx.CommitAsync(ct);
        await PublishAsync([equipmentId], ct);
    }

    /// <summary>
    /// 작업지시 (RELEASE, 설계 §4) — 계획 블록 1개 = 작업 LOT 1개를 "배정(ALLOCATED)" 상태로 만든다.
    /// LOT번호 = 설정 lot.number_format (작업일, 설비 이니셜, 설비·작업일 순번). includePrevious 면 같은 설비 체인에서 앞선 계획도 함께.
    /// 작업지시된 블록은 재계산에서 빠지고(시각 고정), 체인 시작점은 그 블록의 계획 종료 뒤가 된다.
    /// </summary>
    public async Task<IReadOnlyList<ReleasedLot>> ReleaseAsync(long id, ReleaseBlockRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var equipmentId = await EquipmentOfAsync(conn, tx, id);
        await LockEquipmentAsync(conn, tx, [equipmentId]);
        var block = await LoadEditableAsync(conn, tx, id, r.RowVersion);
        var chain = await LoadChainAsync(conn, tx, equipmentId);
        var targets = r.IncludePrevious
            ? chain.TakeWhile(b => b.ProductionScheduleId != id).Append(block).ToList()
            : [block];

        var equipment = await conn.QuerySingleAsync<EquipmentInfoRow>(
            "SELECT equipment_name, COALESCE(equipment_initial, equipment_code) AS initial FROM equipment WHERE equipment_id = @equipmentId",
            new { equipmentId }, tx);
        var now = Now();
        var lots = new List<ReleasedLot>();
        foreach (var b in targets)
        {
            var route = await conn.QuerySingleAsync<RouteInfoRow>(
                """
                SELECT COUNT(DISTINCT soi.heat_process_version_id) AS route_count, MIN(soi.heat_process_version_id) AS route_id,
                       MIN(soi.heat_process_name_snapshot) AS route_name, COUNT(*) AS item_count,
                       COALESCE(MAX(op.is_main_process), 0) AS is_main_process,
                       (SELECT unit_process_name FROM unit_process WHERE unit_process_id = @UnitProcessId) AS unit_process_name
                  FROM production_schedule_item psi
                  JOIN sales_order_item soi ON soi.sales_order_item_id = psi.sales_order_item_id
                  LEFT JOIN heat_process_operation op ON op.heat_process_version_id = soi.heat_process_version_id AND op.unit_process_id = @UnitProcessId
                 WHERE psi.production_schedule_id = @ProductionScheduleId
                """, new { b.ProductionScheduleId, b.UnitProcessId }, tx);
            if (route.ItemCount == 0)
                throw new BusinessRuleException("BLOCK_EMPTY", "수주가 담기지 않은 계획은 작업지시할 수 없습니다.");

            var isRework = await conn.ExecuteScalarAsync<bool>("SELECT is_rework FROM production_schedule WHERE production_schedule_id = @ProductionScheduleId", b, tx);
            var (workId, lotNo) = await InsertWorkAsync(conn, tx, new NewWork(b.ProductionScheduleId, b.UnitProcessId, equipmentId, b.WorkDate,
                b.PlannedDurationMin, isRework, route.IsMainProcess, route.RouteCount == 1 ? route.RouteId : null,
                route.RouteCount == 1 ? route.RouteName : null, route.UnitProcessName, equipment.EquipmentName, equipment.Initial), now);
            await conn.ExecuteAsync(
                """
                UPDATE production_schedule SET status = 'RELEASED', updated_by = @UserId, row_version = row_version + 1
                 WHERE production_schedule_id = @ProductionScheduleId
                """, new { b.ProductionScheduleId, currentUser.UserId }, tx);
            await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, b.ProductionScheduleId,
                new { status = b.Status }, new { status = "RELEASED", production_work_id = workId, lot_no = lotNo }, r.Reason);
            lots.Add(new ReleasedLot(b.ProductionScheduleId, workId, lotNo));
        }
        await RecalculateCoreAsync(conn, tx, equipmentId);
        await tx.CommitAsync(ct);
        await PublishAsync([equipmentId], ct);
        return lots;
    }

    private sealed record NewWork(long ScheduleId, long UnitProcessId, long EquipmentId, DateTime WorkDate, decimal DurationMin, bool IsRework,
        bool IsMainProcess, long? RouteId, string? RouteName, string? UnitProcessName, string EquipmentName, string EquipmentInitial);

    /// <summary>작업 LOT 생성 (작업지시·즉시 작업 공용) — LOT번호 = lot.number_format, 설비·작업일 순번 (취소된 순번도 다시 쓰지 않음)</summary>
    private async Task<(long WorkId, string LotNo)> InsertWorkAsync(MySqlConnection conn, MySqlTransaction tx, NewWork w, DateTime now)
    {
        var tokens = new Dictionary<string, string> { ["EQUIP"] = w.EquipmentInitial };
        var lotFormat = settings.GetString(SettingKeys.LotNumberFormat);
        var seq = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(lot_seq), 0) FROM production_work WHERE equipment_id = @EquipmentId AND work_date = @WorkDate", w, tx);
        string lotNo;
        do lotNo = NumberFormat.Format(lotFormat, DateOnly.FromDateTime(w.WorkDate), ++seq, tokens);
        while (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM production_work WHERE lot_no = @lotNo", new { lotNo }, tx) > 0);

        var workId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO production_work (lot_no, lot_seq, production_schedule_id, unit_process_id, equipment_id, is_main_process,
                   heat_process_version_id, work_date, status, expected_duration_min, is_rework,
                   unit_process_name_snapshot, equipment_name_snapshot, heat_process_name_snapshot, created_by, updated_by)
            VALUES (@lotNo, @seq, @ScheduleId, @UnitProcessId, @EquipmentId, @IsMainProcess,
                    @RouteId, @WorkDate, 'ALLOCATED', @DurationMin, @IsRework,
                    @UnitProcessName, @EquipmentName, @RouteName, @UserId, @UserId);
            SELECT LAST_INSERT_ID();
            """,
            new
            {
                lotNo, seq, w.ScheduleId, w.UnitProcessId, w.EquipmentId, w.IsMainProcess, w.RouteId, w.WorkDate, w.DurationMin, w.IsRework,
                w.UnitProcessName, w.EquipmentName, w.RouteName, currentUser.UserId,
            }, tx);
        await conn.ExecuteAsync(
            "INSERT INTO production_work_event (production_work_id, event_type, event_at, created_by) VALUES (@workId, 'ALLOCATE', @now, @UserId)",
            new { workId, now, currentUser.UserId }, tx);
        return (workId, lotNo);
    }

    /// <summary>
    /// 즉시 작업 (구 F_GasForm NEW — 계획 없이 바로 작업) — 계획 1:1 규칙을 지키려고 지금 시각의 작업지시된 계획 블록을 함께 만든다.
    /// 작업시간은 설정 기본값, 투입 후 표준 확정 때 표준 작업시간으로 바뀐다.
    /// </summary>
    /// <param name="isRework">재작업 LOT (부적합 재처리, 설계 §3.3)</param>
    /// <param name="afterCreate">같은 트랜잭션에서 이어 할 일 (재작업 투입 행 생성 등) — 실패하면 LOT 도 만들어지지 않는다</param>
    public async Task<ReleasedLot> CreateAdHocAsync(long equipmentId, long unitProcessId, CancellationToken ct, bool isRework = false,
        Func<MySqlConnection, MySqlTransaction, long, Task>? afterCreate = null)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await LockEquipmentAsync(conn, tx, [equipmentId]);
        var unitName = await conn.ExecuteScalarAsync<string?>(
            "SELECT unit_process_name FROM unit_process WHERE unit_process_id = @unitProcessId AND is_active = 1", new { unitProcessId }, tx)
            ?? throw new RequestValidationException("unitProcessId", "단위공정을 선택하세요.");
        var equipment = await conn.QuerySingleAsync<EquipmentInfoRow>(
            "SELECT equipment_name, COALESCE(equipment_initial, equipment_code) AS initial FROM equipment WHERE equipment_id = @equipmentId",
            new { equipmentId }, tx);
        var now = Now();
        var minutes = settings.GetDecimal(SettingKeys.ScheduleDefaultRunningTimeMin);
        var workDate = ScheduleCalculator.WorkDateOf(now, await DayStartAsync(conn, tx)).ToDateTime(TimeOnly.MinValue);
        var blockId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO production_schedule (work_date, equipment_id, unit_process_id, sequence_no, planned_qty, planned_duration_min, duration_source,
                   planned_start_at, planned_end_at, status, remark, created_by, updated_by)
            VALUES (@workDate, @equipmentId, @unitProcessId, 0, 0, @minutes, 'SETTING', @now, @end, 'RELEASED', @remark, @UserId, @UserId);
            SELECT LAST_INSERT_ID();
            """, new { workDate, equipmentId, unitProcessId, minutes, now, end = now + ScheduleCalculator.Duration(minutes), remark = isRework ? "재작업" : "즉시 작업", currentUser.UserId }, tx);
        if (isRework)
            await conn.ExecuteAsync("UPDATE production_schedule SET is_rework = 1 WHERE production_schedule_id = @blockId", new { blockId }, tx);
        var (workId, lotNo) = await InsertWorkAsync(conn, tx, new NewWork(blockId, unitProcessId, equipmentId, workDate, minutes, isRework, false, null, null,
            unitName, equipment.EquipmentName, equipment.Initial), now);
        await audit.WriteAsync(conn, tx, AuditAction.Create, "production_work", workId, null,
            new { lot_no = lotNo, equipment_id = equipmentId, unit_process_id = unitProcessId, production_schedule_id = blockId, ad_hoc = true, is_rework = isRework });
        if (afterCreate is not null) await afterCreate(conn, tx, workId);
        await RecalculateCoreAsync(conn, tx, equipmentId);
        await tx.CommitAsync(ct);
        await PublishAsync([equipmentId], ct);
        return new ReleasedLot(blockId, workId, lotNo);
    }

    /// <summary>
    /// 작업지시 취소 — 투입 전(ALLOCATED) LOT 만. 작업 LOT 은 취소 상태로 남기고(감사), 계획은 확정 상태로 돌아가 다시 재계산된다.
    /// LOT번호는 다시 쓰지 않는다 (다음 작업지시는 새 순번).
    /// </summary>
    public async Task UnreleaseAsync(long id, VersionedRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var equipmentId = await EquipmentOfAsync(conn, tx, id);
        await LockEquipmentAsync(conn, tx, [equipmentId]);
        var block = await conn.QuerySingleOrDefaultAsync<BlockRow>(
            $"SELECT {BlockColumns} FROM production_schedule WHERE production_schedule_id = @id AND is_deleted = 0 FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException(Table, id);
        if (block.RowVersion != r.RowVersion) throw new ConcurrencyConflictException(Table, id);
        if (block.Status != "RELEASED") throw new BusinessRuleException("BLOCK_NOT_RELEASED", "작업지시된 계획이 아닙니다.");
        var work = await conn.QuerySingleOrDefaultAsync<WorkStatusRow>(
            "SELECT production_work_id, lot_no, status FROM production_work WHERE production_schedule_id = @id AND is_deleted = 0 FOR UPDATE", new { id }, tx);
        if (work is not null && work.Status != "ALLOCATED")
            throw new BusinessRuleException("WORK_STARTED", $"작업 LOT {work.LotNo} 은 이미 투입되어 작업지시를 취소할 수 없습니다.");

        var now = Now();
        if (work is not null)
        {
            await conn.ExecuteAsync(
                """
                UPDATE production_work SET status = 'CANCELLED', production_schedule_id = NULL, is_deleted = 1, deleted_at = @now,
                       deleted_by = @UserId, updated_by = @UserId, row_version = row_version + 1
                 WHERE production_work_id = @ProductionWorkId
                """, new { work.ProductionWorkId, now, currentUser.UserId }, tx);
            await conn.ExecuteAsync(
                "INSERT INTO production_work_event (production_work_id, event_type, event_at, created_by, remark) VALUES (@ProductionWorkId, 'CANCEL', @now, @UserId, @Reason)",
                new { work.ProductionWorkId, now, currentUser.UserId, r.Reason }, tx);
        }
        await conn.ExecuteAsync(
            "UPDATE production_schedule SET status = 'CONFIRMED', updated_by = @UserId, row_version = row_version + 1 WHERE production_schedule_id = @id",
            new { id, currentUser.UserId }, tx);
        await RecalculateCoreAsync(conn, tx, equipmentId);
        await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id,
            new { status = "RELEASED", lot_no = work?.LotNo }, new { status = "CONFIRMED" }, r.Reason);
        await tx.CommitAsync(ct);
        await PublishAsync([equipmentId], ct);
    }

    /// <summary>설비 체인 재계산. 바뀐 것이 있으면 true (SignalR 알림).</summary>
    public async Task<bool> RecalculateAsync(long equipmentId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await LockEquipmentAsync(conn, tx, [equipmentId]);
        var changed = await RecalculateCoreAsync(conn, tx, equipmentId);
        await tx.CommitAsync(ct);
        if (changed)
            await PublishAsync([equipmentId], ct);
        return changed;
    }

    /// <summary>계획이 남아 있는 모든 설비 (지연 반영 주기 재계산 대상).</summary>
    public async Task<IReadOnlyList<long>> EquipmentWithPlansAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return (await conn.QueryAsync<long>(
            $"SELECT DISTINCT equipment_id FROM production_schedule WHERE is_deleted = 0 AND status IN ({ActiveStatuses})")).ToList();
    }

    // ───────────────────────── 재계산 ─────────────────────────

    /// <param name="order">체인 순서 지정 (삽입·이동 시). null 이면 현재 순서 (작업일, 순번).</param>
    internal async Task<bool> RecalculateCoreAsync(MySqlConnection conn, MySqlTransaction tx, long equipmentId, IReadOnlyList<long>? order = null)
    {
        var chain = await LoadChainAsync(conn, tx, equipmentId);
        if (order is not null)
        {
            var byId = chain.ToDictionary(b => b.ProductionScheduleId);
            if (order.Count != chain.Count || order.Any(id => !byId.ContainsKey(id)))
                throw new InvalidOperationException($"설비 {equipmentId} 체인 순서가 현재 계획과 맞지 않습니다.");
            chain = order.Select(id => byId[id]).ToList();
        }
        if (chain.Count == 0)
            return false;

        var now = Now();
        var dayStart = await DayStartAsync(conn, tx);
        var anchor = await AnchorAsync(conn, tx, equipmentId, now);
        var rules = await CalendarAsync(conn, tx, equipmentId, anchor, dayStart);
        var results = ScheduleCalculator.Calculate(
            chain.Select(b => new PlanBlock(b.ProductionScheduleId, b.PlannedDurationMin, b.IsTimeLocked, b.PlannedStartAt)).ToList(),
            anchor, rules);

        // 순번: 작업일별로 시작 순 (작업지시된 블록 포함)
        var workDates = results.Select(r => r.WorkDate.ToDateTime(TimeOnly.MinValue)).Distinct().ToList();
        var released = (await conn.QueryAsync<ReleasedRow>(
            """
            SELECT production_schedule_id, work_date, planned_start_at FROM production_schedule
             WHERE equipment_id = @equipmentId AND is_deleted = 0 AND status = 'RELEASED' AND work_date IN @workDates
            """, new { equipmentId, workDates }, tx)).ToList();
        var sequence = results.Select(r => (r.Id, WorkDate: r.WorkDate.ToDateTime(TimeOnly.MinValue), r.Start))
            .Concat(released.Select(x => (Id: x.ProductionScheduleId, x.WorkDate, Start: x.PlannedStartAt)))
            .GroupBy(x => x.WorkDate)
            .SelectMany(g => g.OrderBy(x => x.Start).ThenBy(x => x.Id).Select((x, i) => (x.Id, Seq: i + 1)))
            .ToDictionary(x => x.Id, x => x.Seq);

        // 임시 LOT: 작업일별 실제 LOT 마지막 순번 다음부터 (구 WorkPlanRepository 동일)
        var initial = await conn.ExecuteScalarAsync<string?>(
            "SELECT COALESCE(equipment_initial, equipment_code) FROM equipment WHERE equipment_id = @equipmentId", new { equipmentId }, tx) ?? "";
        var lastLotSeq = (await conn.QueryAsync<LastLotSeqRow>(
            """
            SELECT work_date, CAST(COALESCE(MAX(lot_seq), 0) AS SIGNED) AS last_seq FROM production_work
             WHERE equipment_id = @equipmentId AND work_date IN @workDates   -- 취소된 LOT 순번도 다시 쓰지 않음 (작업지시와 같은 기준)
             GROUP BY work_date
            """, new { equipmentId, workDates }, tx)).ToDictionary(x => x.WorkDate, x => (int)x.LastSeq);
        var lotFormat = settings.GetString(SettingKeys.ScheduleTempLotPrefix) + settings.GetString(SettingKeys.LotNumberFormat);
        var tokens = new Dictionary<string, string> { ["EQUIP"] = initial };
        var lotNo = results
            .GroupBy(r => r.WorkDate)
            .SelectMany(g => g.OrderBy(r => r.Start).Select((r, i) => (r.Id, Lot: NumberFormat.Format(lotFormat, g.Key,
                lastLotSeq.GetValueOrDefault(g.Key.ToDateTime(TimeOnly.MinValue)) + i + 1, tokens))))
            .ToDictionary(x => x.Id, x => x.Lot);

        var current = chain.ToDictionary(b => b.ProductionScheduleId);
        var changed = false;
        foreach (var r in results)
        {
            var b = current[r.Id];
            var workDate = r.WorkDate.ToDateTime(TimeOnly.MinValue);
            if (b.PlannedStartAt == r.Start && b.PlannedEndAt == r.End && b.WorkDate == workDate
                && b.SequenceNo == sequence[r.Id] && b.PlannedLotNo == lotNo[r.Id])
                continue;
            await conn.ExecuteAsync(
                """
                UPDATE production_schedule
                   SET planned_start_at = @Start, planned_end_at = @End, work_date = @workDate, sequence_no = @seq, planned_lot_no = @lot
                 WHERE production_schedule_id = @Id
                """, new { r.Start, r.End, workDate, seq = sequence[r.Id], lot = lotNo[r.Id], r.Id }, tx);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// 체인 시작점 = 현재 시각과 이 설비에서 진행 중·작업지시된 작업의 예상 종료 중 늦은 것.
    /// 진행 중 작업이 예상보다 길어지면 now 가 커지므로 뒤 계획이 자동으로 밀린다 (설계 §7 지연 반영).
    /// </summary>
    private static async Task<DateTime> AnchorAsync(MySqlConnection conn, MySqlTransaction tx, long equipmentId, DateTime now)
    {
        var cursor = now;
        var running = await conn.QueryAsync<(DateTime? ActualStartAt, decimal? DurationMin)>(
            """
            SELECT w.actual_start_at, COALESCE(w.expected_duration_min, ps.planned_duration_min)
              FROM production_work w
              LEFT JOIN production_schedule ps ON ps.production_schedule_id = w.production_schedule_id
             WHERE w.equipment_id = @equipmentId AND w.status = 'INPUT' AND w.is_deleted = 0
            """, new { equipmentId }, tx);
        foreach (var w in running)
        {
            var start = w.ActualStartAt ?? now;
            var end = start + ScheduleCalculator.Duration(w.DurationMin ?? 0);
            if (end > cursor) cursor = end;
        }

        // 작업지시(RELEASED)됐지만 아직 투입 전인 블록은 계획대로 먼저 수행한다고 본다
        var releasedWaiting = await conn.QueryAsync<(DateTime Start, decimal DurationMin)>(
            """
            SELECT ps.planned_start_at, ps.planned_duration_min
              FROM production_schedule ps
              LEFT JOIN production_work w ON w.production_schedule_id = ps.production_schedule_id AND w.is_deleted = 0
             WHERE ps.equipment_id = @equipmentId AND ps.is_deleted = 0 AND ps.status = 'RELEASED'
               AND (w.production_work_id IS NULL OR w.status = 'ALLOCATED')
             ORDER BY ps.planned_start_at
            """, new { equipmentId }, tx);
        foreach (var b in releasedWaiting)
        {
            var start = b.Start > cursor ? b.Start : cursor;
            cursor = start + ScheduleCalculator.Duration(b.DurationMin);
        }
        return cursor;
    }

    /// <summary>작업일 시작 = 첫 교대(work_shift 최소 sort_order) 시작, 없으면 설정 (설계 §15.4 H1).</summary>
    internal async Task<TimeOnly> DayStartAsync(MySqlConnection conn, MySqlTransaction? tx)
    {
        var shiftStart = await conn.ExecuteScalarAsync<TimeSpan?>(
            "SELECT start_time FROM work_shift WHERE is_active = 1 ORDER BY sort_order, work_shift_id LIMIT 1", transaction: tx);
        return shiftStart is { } s ? TimeOnly.FromTimeSpan(s) : settings.GetTime(SettingKeys.ScheduleDayStartTime);
    }

    private static async Task<CalendarRules> CalendarAsync(
        MySqlConnection conn, MySqlTransaction tx, long equipmentId, DateTime anchor, TimeOnly dayStart)
    {
        var fromDate = ScheduleCalculator.WorkDateOf(anchor, dayStart).ToDateTime(TimeOnly.MinValue);
        var holidays = (await conn.QueryAsync<DateTime>(
                "SELECT calendar_date FROM work_calendar WHERE day_type = 'HOLIDAY' AND calendar_date >= @fromDate",
                new { fromDate }, tx))
            .Select(DateOnly.FromDateTime).ToHashSet();
        var downtime = (await conn.QueryAsync<(DateTime Start, DateTime End)>(
                """
                SELECT started_at, ended_at FROM equipment_downtime
                 WHERE equipment_id = @equipmentId AND is_planned = 1
                   AND started_at IS NOT NULL AND ended_at IS NOT NULL AND ended_at > @anchor
                """, new { equipmentId, anchor }, tx))
            .Select(d => new TimeRange(d.Start, d.End)).ToList();
        return new CalendarRules(dayStart, holidays, downtime);
    }

    // ───────────────────────── 공통 ─────────────────────────

    private sealed class EquipmentLockRow
    {
        public long EquipmentId { get; init; }
        public long? EquipmentTypeId { get; init; }
        public bool IsActive { get; init; }
    }

    /// <summary>설비 행 잠금 — 같은 설비의 계획 변경을 직렬화 (구 임시 고순번 방식 대체, §15.1 S4). id 순으로 잠가 교착을 피한다.</summary>
    private static async Task<IReadOnlyDictionary<long, EquipmentLockRow>> LockEquipmentAsync(
        MySqlConnection conn, MySqlTransaction tx, IEnumerable<long> equipmentIds)
    {
        var ids = equipmentIds.Distinct().Order().ToList();
        var rows = (await conn.QueryAsync<EquipmentLockRow>(
            "SELECT equipment_id, equipment_type_id, is_active FROM equipment WHERE equipment_id IN @ids ORDER BY equipment_id FOR UPDATE",
            new { ids }, tx)).ToDictionary(e => e.EquipmentId);
        foreach (var id in ids)
        {
            if (!rows.TryGetValue(id, out var e))
                throw new NotFoundException("equipment", id);
            if (!e.IsActive)
                throw new BusinessRuleException("EQUIPMENT_INACTIVE", "사용하지 않는 설비에는 계획할 수 없습니다.");
        }
        return rows;
    }

    private static async Task<long> EquipmentOfAsync(MySqlConnection conn, MySqlTransaction tx, long id) =>
        await conn.ExecuteScalarAsync<long?>(
            "SELECT equipment_id FROM production_schedule WHERE production_schedule_id = @id AND is_deleted = 0", new { id }, tx)
        ?? throw new NotFoundException(Table, id);

    private static async Task<List<BlockRow>> LoadChainAsync(MySqlConnection conn, MySqlTransaction tx, long equipmentId) =>
        (await conn.QueryAsync<BlockRow>(
            $"""
            SELECT {BlockColumns} FROM production_schedule
             WHERE equipment_id = @equipmentId AND is_deleted = 0 AND status IN ({ActiveStatuses})
             ORDER BY work_date, sequence_no, planned_start_at, production_schedule_id
            """, new { equipmentId }, tx)).ToList();

    /// <summary>계획 상태(PLANNED/CONFIRMED)이고 클라이언트가 본 버전과 같아야 수정 가능.</summary>
    private static async Task<BlockRow> LoadEditableAsync(MySqlConnection conn, MySqlTransaction tx, long id, int rowVersion)
    {
        var block = await conn.QuerySingleOrDefaultAsync<BlockRow>(
            $"SELECT {BlockColumns} FROM production_schedule WHERE production_schedule_id = @id AND is_deleted = 0 FOR UPDATE",
            new { id }, tx) ?? throw new NotFoundException(Table, id);
        if (block.RowVersion != rowVersion)
            throw new ConcurrencyConflictException(Table, id);
        if (block.Status is not ("PLANNED" or "CONFIRMED"))
            throw new BusinessRuleException("BLOCK_NOT_EDITABLE", "작업지시되었거나 취소된 계획은 바꿀 수 없습니다.");
        return block;
    }

    private sealed record PlanItem(long PartId, decimal RemainingQty);

    // Dapper 는 BIGINT UNSIGNED 를 생성자·튜플의 long 으로 바꾸지 못하므로 조회 행은 속성 클래스로 받는다
    private sealed class PlanItemRow
    {
        public long PartId { get; init; }
        public decimal OrderQty { get; init; }
        public string Status { get; init; } = "";
        public long? RouteId { get; init; }
    }

    private sealed class ReleasedRow
    {
        public long ProductionScheduleId { get; init; }
        public DateTime WorkDate { get; init; }
        public DateTime PlannedStartAt { get; init; }
    }

    private sealed class EquipmentInfoRow
    {
        public string EquipmentName { get; init; } = "";
        public string Initial { get; init; } = "";
    }

    private sealed class RouteInfoRow
    {
        public long RouteCount { get; init; }
        public long? RouteId { get; init; }
        public string? RouteName { get; init; }
        public long ItemCount { get; init; }
        public bool IsMainProcess { get; init; }
        public string? UnitProcessName { get; init; }
    }

    private sealed class WorkStatusRow
    {
        public long ProductionWorkId { get; init; }
        public string LotNo { get; init; } = "";
        public string Status { get; init; } = "";
    }

    private sealed class LastLotSeqRow
    {
        public DateTime WorkDate { get; init; }
        public long LastSeq { get; init; }
    }

    /// <summary>수주품목의 해당 단위공정 계획 잔량 = 수주수량 − 취소 안 된 계획수량 합.</summary>
    private static async Task<PlanItem> LoadPlanItemAsync(MySqlConnection conn, MySqlTransaction tx, long itemId, long unitProcessId)
    {
        var item = await conn.QuerySingleOrDefaultAsync<PlanItemRow>(
            """
            SELECT part_id, order_qty, status, heat_process_version_id AS route_id FROM sales_order_item
             WHERE sales_order_item_id = @itemId FOR UPDATE
            """, new { itemId }, tx)
            ?? throw new NotFoundException("sales_order_item", itemId);
        if (item.Status is "COMPLETED" or "CANCELLED")
            throw new BusinessRuleException("ORDER_ITEM_CLOSED", "완료되었거나 취소된 수주는 계획할 수 없습니다.");
        if (item.RouteId is { } routeId && await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM heat_process_operation WHERE heat_process_version_id = @routeId AND unit_process_id = @unitProcessId",
                new { routeId, unitProcessId }, tx) == 0)
            throw new BusinessRuleException("NOT_IN_ROUTE", "이 수주의 공정 경로에 없는 단위공정입니다.");

        var planned = await conn.ExecuteScalarAsync<decimal>(
            """
            SELECT COALESCE(SUM(psi.planned_qty), 0)
              FROM production_schedule_item psi
              JOIN production_schedule ps ON ps.production_schedule_id = psi.production_schedule_id
             WHERE psi.sales_order_item_id = @itemId AND ps.unit_process_id = @unitProcessId
               AND ps.is_deleted = 0 AND ps.status <> 'CANCELLED'
            """, new { itemId, unitProcessId }, tx);
        var remaining = item.OrderQty - planned;
        if (remaining <= 0)
            throw new BusinessRuleException("NOTHING_TO_PLAN", "이 단위공정에 계획할 잔량이 없습니다.");
        return new PlanItem(item.PartId, remaining);
    }

    /// <summary>
    /// ①~③ 으로 찾으면 그 값. 없으면 ④ 사용자 입력(선택 시 설비유형 기준시간으로 등록) 또는 ⑤ 설정 기본값.
    /// 둘 다 없으면 422 DURATION_REQUIRED — 화면이 입력창을 띄운다 (구 F_DummyTime).
    /// </summary>
    private async Task<(decimal Minutes, string Source)> DecideDurationAsync(
        MySqlConnection conn, MySqlTransaction tx, ResolvedRunningTime resolved, decimal? input, bool saveAsDefault, bool useDefault,
        long unitProcessId, long? equipmentTypeId, long equipmentId)
    {
        if (resolved.Minutes is { } found)
            return (found, resolved.Source!);

        if (input is { } minutes)
        {
            if (minutes <= 0)
                throw new RequestValidationException("durationMin", "작업시간은 0보다 커야 합니다.");
            if (saveAsDefault)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO process_default_time (equipment_type_id, equipment_id, unit_process_id, running_time_min, remark)
                    VALUES (@equipmentTypeId, @equipmentId, @unitProcessId, @minutes, '생산계획에서 입력')
                    """,
                    // 설비유형이 없으면 설비에 등록 (CHECK: 둘 중 하나 필수)
                    new { equipmentTypeId, equipmentId = equipmentTypeId is null ? equipmentId : (long?)null, unitProcessId, minutes }, tx);
            }
            return (minutes, RunningTimeSource.UserInput);
        }

        if (useDefault)
            return (settings.GetDecimal(SettingKeys.ScheduleDefaultRunningTimeMin), RunningTimeSource.Setting);

        throw new DurationRequiredException(settings.GetDecimal(SettingKeys.ScheduleDefaultRunningTimeMin));
    }

    private static int PositionOf(List<long> order, long? beforeBlockId)
    {
        if (beforeBlockId is null)
            return order.Count;
        var index = order.IndexOf(beforeBlockId.Value);
        return index >= 0
            ? index
            : throw new BusinessRuleException("INVALID_POSITION", "기준 계획이 이 설비의 이동 가능한 계획이 아닙니다.");
    }

    private DateTime Now()
    {
        var now = time.GetLocalNow().DateTime;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);   // 분 단위 (DB DATETIME·재계산 흔들림 방지)
    }

    private Task PublishAsync(IEnumerable<long> equipmentIds, CancellationToken ct) =>
        events.PublishAsync(RealtimeEvents.ScheduleChanged, new { equipmentIds = equipmentIds.Distinct().ToArray() }, ct);
}

/// <summary>작업시간을 찾지 못함 — 화면에서 입력받아 다시 요청 (extensions.defaultMin = 설정 기본값).</summary>
public sealed class DurationRequiredException(decimal defaultMinutes)
    : AppException(StatusCodes.Status422UnprocessableEntity, "DURATION_REQUIRED",
        "작업표준·직전 작업·기준시간에서 작업시간을 찾지 못했습니다. 작업시간을 입력하세요.")
{
    public override IReadOnlyDictionary<string, object?> Extra => new Dictionary<string, object?> { ["defaultMin"] = defaultMinutes };
}
