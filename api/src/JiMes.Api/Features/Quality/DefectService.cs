using Dapper;
using JiMes.Api.Features.Scheduling;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Quality;

public sealed class DefectDto
{
    public long DefectOccurrenceId { get; init; }
    public DateTime DefectDate { get; init; }
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public string? CustomerName { get; init; }
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
    public string? CustomerLot { get; init; }
    public long? ProductionWorkId { get; init; }
    public string? LotNo { get; init; }
    public string? UnitProcessName { get; init; }
    public string? MainLotNo { get; init; }
    public long? InspectionId { get; init; }
    public string? InspectionNo { get; init; }
    public decimal DefectQty { get; init; }
    public long? DefectReasonId { get; init; }
    public string? DefectReasonName { get; init; }
    public string? Remark { get; init; }
    public string Status { get; init; } = "";
    public string? Decision { get; init; }
    public string? DecisionRemark { get; init; }
    public long? DecidedEmployeeId { get; init; }
    public string? DecidedByName { get; init; }
    public DateTime? DecidedAt { get; init; }
    public string? CompletedByName { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? ReworkRemark { get; init; }
    public long? ReworkWorkId { get; init; }
    public string? ReworkLotNo { get; init; }
    public string? ReworkStatus { get; init; }
    public int RowVersion { get; init; }
}

public sealed record DefectDecideRequest(int RowVersion, string? Decision, long? EmployeeId, string? Remark);
public sealed record DefectCompleteRequest(int RowVersion, long? EmployeeId, string? Remark);
public sealed record DefectCancelRequest(int RowVersion, string? Reason);
public sealed record DefectReworkRequest(int RowVersion, long EquipmentId, long UnitProcessId);

/// <summary>
/// 부적합 처리 (구 F_Defect / F_DefectAdd, legacy_forms/기타_폼_요약.md D1~D3).
/// 상태: 미결정(OPEN) → 결정(DECIDED, 처리구분 = 공통코드 DEFECT_ACTION) → [재처리면] 재작업 LOT 투입(REWORKING) → 완료(COMPLETED).
/// 재작업 LOT 은 같은 LOT번호 규칙, origin_work_id = 최초 LOT, 재작업 LOT 완료 시 부적합도 완료.
/// </summary>
public sealed class DefectService(
    IDbConnectionFactory db, AuditWriter audit, ICurrentUser currentUser, SettingsCache settings, CommonCodeCache codes, TimeProvider time,
    SchedulingService scheduling)
{
    private const string Table = "defect_occurrence";
    public const string ReworkDecision = "REWORK";

    internal const string DefectSql =
        """
        SELECT d.defect_occurrence_id, d.defect_date, d.sales_order_item_id, soi.order_item_no, c.customer_name, soi.part_name_snapshot AS part_name,
               soi.part_number_snapshot AS part_number, soi.customer_lot, d.production_work_id, w.lot_no, w.unit_process_name_snapshot AS unit_process_name,
               mw.lot_no AS main_lot_no, i.inspection_id, i.inspection_no, d.defect_qty, d.defect_reason_id, r.defect_reason_name, d.remark,
               d.status, d.decision, d.decision_remark, d.decided_employee_id, de.employee_name AS decided_by_name, d.decided_at,
               ce.employee_name AS completed_by_name, d.completed_at, d.rework_remark,
               rw.production_work_id AS rework_work_id, rw.lot_no AS rework_lot_no, rw.status AS rework_status, d.row_version
          FROM defect_occurrence d
          JOIN sales_order_item soi ON soi.sales_order_item_id = d.sales_order_item_id
          JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
          JOIN customer c ON c.customer_id = so.customer_id
          LEFT JOIN production_work w ON w.production_work_id = d.production_work_id
          LEFT JOIN production_work mw ON mw.production_work_id = d.main_work_id
          LEFT JOIN defect_reason r ON r.defect_reason_id = d.defect_reason_id
          LEFT JOIN inspection_target t ON t.inspection_target_id = d.inspection_target_id
          LEFT JOIN inspection i ON i.inspection_id = t.inspection_id
          LEFT JOIN employee de ON de.employee_id = d.decided_employee_id
          LEFT JOIN employee ce ON ce.employee_id = d.completed_employee_id
          LEFT JOIN production_work_input ri ON ri.production_work_input_id = d.rework_input_id
          LEFT JOIN production_work rw ON rw.production_work_id = ri.production_work_id
        """;

    public async Task<IEnumerable<DefectDto>> ListAsync(DateOnly? from, DateOnly? to, string? status, string? decision, string? search, bool openOnly, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        await using var conn = await db.OpenAsync(ct);
        return await conn.QueryAsync<DefectDto>(
            DefectSql + """
             WHERE d.is_deleted = 0
               AND (@openOnly OR d.defect_date BETWEEN @from AND @to)
               AND (NOT @openOnly OR d.status IN ('OPEN','DECIDED','REWORKING'))
               AND (@status IS NULL OR d.status = @status) AND (@decision IS NULL OR d.decision = @decision)
               AND (@search IS NULL OR soi.order_item_no LIKE @like OR w.lot_no LIKE @like OR mw.lot_no LIKE @like OR soi.part_name_snapshot LIKE @like
                    OR c.customer_name LIKE @like OR i.inspection_no LIKE @like)
             ORDER BY d.defect_date DESC, d.defect_occurrence_id DESC
             LIMIT 1000
            """,
            new
            {
                openOnly, from = (from ?? today.AddDays(-settings.GetInt(SettingKeys.SalesOrderListDefaultDays))).ToDateTime(TimeOnly.MinValue),
                to = (to ?? today).ToDateTime(TimeOnly.MinValue), status = Empty(status), decision = Empty(decision),
                search = Empty(search?.Trim()), like = $"%{search?.Trim()}%",
            });
    }

    public async Task<DefectDto> GetAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await GetAsync(conn, null, id);
    }

    private static async Task<DefectDto> GetAsync(MySqlConnection conn, MySqlTransaction? tx, long id) =>
        await conn.QuerySingleOrDefaultAsync<DefectDto>(DefectSql + " WHERE d.defect_occurrence_id = @id AND d.is_deleted = 0", new { id }, tx)
        ?? throw new NotFoundException(Table, id);

    /// <summary>판정 (구 F_DefectAdd 판정 저장 → check_complete) — 처리구분·판정자·메모. 재작업 투입 전까지 다시 판정 가능</summary>
    public async Task DecideAsync(long id, DefectDecideRequest r, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.Decision is null || codes.GetGroup("DEFECT_ACTION").Codes.All(c => c.Code != r.Decision || !c.IsActive)) errors["decision"] = ["처리구분을 고르세요."];
        if (r.EmployeeId is null) errors["employeeId"] = ["판정자를 고르세요."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id, r.RowVersion);
        if (before.Status is not ("OPEN" or "DECIDED"))
            throw new BusinessRuleException("DEFECT_NOT_DECIDABLE", "재작업 중·완료·취소된 부적합은 다시 판정할 수 없습니다.");
        await conn.ExecuteAsync(
            """
            UPDATE defect_occurrence SET status = 'DECIDED', decision = @Decision, decision_remark = @remark, decided_employee_id = @EmployeeId,
                   decided_at = @now, updated_by = @UserId, row_version = row_version + 1
             WHERE defect_occurrence_id = @id
            """, new { id, r.Decision, remark = Trim(r.Remark), r.EmployeeId, now = time.GetLocalNow().DateTime, currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id, new { before.Status, before.Decision }, new { status = "DECIDED", r.Decision, r.Remark });
        await tx.CommitAsync(ct);
    }

    /// <summary>완료 (구 완료자·완료일·재작업 메모) — 결정된 부적합, 재작업 중이어도 수동 완료 가능</summary>
    public async Task CompleteAsync(long id, DefectCompleteRequest r, CancellationToken ct)
    {
        if (r.EmployeeId is null) throw new RequestValidationException("employeeId", "완료자를 고르세요.");
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id, r.RowVersion);
        if (before.Status is not ("DECIDED" or "REWORKING"))
            throw new BusinessRuleException("DEFECT_NOT_DECIDED", "판정한 부적합만 완료할 수 있습니다.");
        await conn.ExecuteAsync(
            """
            UPDATE defect_occurrence SET status = 'COMPLETED', completed_employee_id = @EmployeeId, completed_at = @now, rework_remark = COALESCE(@remark, rework_remark),
                   updated_by = @UserId, row_version = row_version + 1
             WHERE defect_occurrence_id = @id
            """, new { id, r.EmployeeId, now = time.GetLocalNow().DateTime, remark = Trim(r.Remark), currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id, new { before.Status }, new { status = "COMPLETED", r.Remark });
        await tx.CommitAsync(ct);
    }

    public async Task CancelAsync(long id, DefectCancelRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id, r.RowVersion);
        if (before.Status is not ("OPEN" or "DECIDED"))
            throw new BusinessRuleException("DEFECT_NOT_CANCELLABLE", "재작업 중·완료된 부적합은 취소할 수 없습니다.");
        await conn.ExecuteAsync("UPDATE defect_occurrence SET status = 'CANCELLED', updated_by = @UserId, row_version = row_version + 1 WHERE defect_occurrence_id = @id",
            new { id, currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id, new { before.Status }, new { status = "CANCELLED" }, r.Reason);
        await tx.CommitAsync(ct);
    }

    private sealed class SourceInputRow
    {
        public long SalesOrderItemId { get; init; }
        public long? HeatProcessVersionId { get; init; }
        public long? MainWorkId { get; init; }
        public long? SourceWorkId { get; init; }
        public bool SourceIsRework { get; init; }
        public long? SourceOriginWorkId { get; init; }
        public decimal DefectQty { get; init; }
        public string? CustomerName { get; init; }
        public string? CustomerLot { get; init; }
        public string? PartName { get; init; }
        public string? PartNumber { get; init; }
        public string? Specification { get; init; }
        public string? Model { get; init; }
        public string? Material { get; init; }
        public decimal? UnitPrice { get; init; }
    }

    /// <summary>
    /// 재작업 (구 재처리 대기 → 스케줄 배정 plan_complete) — 재처리로 판정된 부적합으로 재작업 LOT 을 만들고 부적합 수량을 투입한다.
    /// origin_work_id = 최초 LOT (재작업의 재작업도 최초), 재작업 단위공정이 수주 경로의 주공정이면 주 LOT = 자신, 아니면 원 주 LOT 유지 (§3.3).
    /// </summary>
    public async Task<ReleasedLot> ReworkAsync(long id, DefectReworkRequest r, CancellationToken ct)
    {
        // 먼저 상태 확인 (잠금은 LOT 생성 트랜잭션 안에서 다시)
        var current = await GetAsync(id, ct);
        if (current.RowVersion != r.RowVersion) throw new ConcurrencyConflictException(Table, id);
        if (current.Status != "DECIDED" || current.Decision != ReworkDecision)
            throw new BusinessRuleException("DEFECT_NOT_REWORK", "처리구분이 재처리로 결정된 부적합만 재작업 LOT 을 만들 수 있습니다.");

        return await scheduling.CreateAdHocAsync(r.EquipmentId, r.UnitProcessId, ct, isRework: true, afterCreate: async (conn, tx, workId) =>
        {
            var d = await LockAsync(conn, tx, id, r.RowVersion);
            if (d.Status != "DECIDED" || d.Decision != ReworkDecision)
                throw new BusinessRuleException("DEFECT_NOT_REWORK", "처리구분이 재처리로 결정된 부적합만 재작업 LOT 을 만들 수 있습니다.");
            var src = await conn.QuerySingleAsync<SourceInputRow>(
                """
                SELECT d.sales_order_item_id, soi.heat_process_version_id, d.main_work_id, d.production_work_id AS source_work_id,
                       COALESCE(w.is_rework, 0) AS source_is_rework, pwi.origin_work_id AS source_origin_work_id, d.defect_qty,
                       COALESCE(pwi.customer_name_snapshot, c.customer_name) AS customer_name, COALESCE(pwi.customer_lot_snapshot, soi.customer_lot) AS customer_lot,
                       soi.part_name_snapshot AS part_name, soi.part_number_snapshot AS part_number, soi.specification_snapshot AS specification,
                       soi.model_snapshot AS model, soi.material_snapshot AS material, soi.unit_price
                  FROM defect_occurrence d
                  JOIN sales_order_item soi ON soi.sales_order_item_id = d.sales_order_item_id
                  JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
                  JOIN customer c ON c.customer_id = so.customer_id
                  LEFT JOIN production_work w ON w.production_work_id = d.production_work_id
                  LEFT JOIN production_work_input pwi ON pwi.production_work_input_id = d.production_work_input_id
                 WHERE d.defect_occurrence_id = @id
                """, new { id }, tx);
            var isMainOp = src.HeatProcessVersionId is { } v && await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM heat_process_operation WHERE heat_process_version_id = @v AND unit_process_id = @UnitProcessId AND is_main_process = 1",
                new { v, r.UnitProcessId }, tx) > 0;
            var origin = src.SourceIsRework ? src.SourceOriginWorkId ?? src.SourceWorkId : src.SourceWorkId;
            var inputId = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO production_work_input (production_work_id, sales_order_item_id, main_work_id, origin_work_id, input_qty, unit_price_snapshot, scanned_at,
                       status, customer_name_snapshot, customer_lot_snapshot, part_name_snapshot, part_number_snapshot, specification_snapshot, model_snapshot,
                       material_snapshot, remark, created_by, updated_by)
                VALUES (@workId, @SalesOrderItemId, @mainWorkId, @origin, @DefectQty, @UnitPrice, @now,
                        'INPUT', @CustomerName, @CustomerLot, @PartName, @PartNumber, @Specification, @Model,
                        @Material, '재작업', @UserId, @UserId);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    workId, src.SalesOrderItemId, mainWorkId = isMainOp ? workId : src.MainWorkId, origin, src.DefectQty, src.UnitPrice, now = time.GetLocalNow().DateTime,
                    src.CustomerName, src.CustomerLot, src.PartName, src.PartNumber, src.Specification, src.Model, src.Material, currentUser.UserId,
                }, tx);
            if (isMainOp)
                await conn.ExecuteAsync("UPDATE production_work SET is_main_process = 1 WHERE production_work_id = @workId", new { workId }, tx);
            await conn.ExecuteAsync(
                "UPDATE defect_occurrence SET status = 'REWORKING', rework_input_id = @inputId, updated_by = @UserId, row_version = row_version + 1 WHERE defect_occurrence_id = @id",
                new { id, inputId, currentUser.UserId }, tx);
            await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id, new { d.Status }, new { status = "REWORKING", rework_work_id = workId, rework_input_id = inputId });
        });
    }

    private static async Task<DefectDto> LockAsync(MySqlConnection conn, MySqlTransaction tx, long id, int rowVersion)
    {
        var version = await conn.ExecuteScalarAsync<int?>("SELECT row_version FROM defect_occurrence WHERE defect_occurrence_id = @id AND is_deleted = 0 FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException(Table, id);
        if (version != rowVersion) throw new ConcurrencyConflictException(Table, id);
        return await GetAsync(conn, tx, id);
    }

    private static string? Empty(string? s) => string.IsNullOrEmpty(s) ? null : s;
    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
