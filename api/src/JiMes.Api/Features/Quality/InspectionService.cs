using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Numbering;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Quality;

/// <summary>
/// 검사 (구 F_InspectionAddForm, 설계 §5.1·§17, legacy_forms/F_InspectionAddForm.md).
/// 검사 1회 = LOT 입력 → 그 LOT 투입 행 중 대상 N개 선택. 측정·판정은 검사 공통 (I2), 성적서는 대상별 1장.
/// 저장(미확정, 여러 번 수정) → 확정(불합격이면 대상마다 부적합 = 대상 수량 전체, I4) → 확정 후 수정은 재검사(새 번호, I3).
/// </summary>
public sealed class InspectionService(
    IDbConnectionFactory db, AuditWriter audit, ICurrentUser currentUser, SettingsCache settings, CommonCodeCache codes, TimeProvider time)
{
    private const string Table = "inspection";
    private static readonly string[] Types = ["INCOMING", "PROCESS", "OUTGOING"];

    private const string InspectionSql =
        """
        SELECT i.inspection_id, i.inspection_no, i.inspection_type, i.inspection_date, i.production_work_id, w.lot_no,
               i.inspection_standard_version_id, i.reinspection_of_id, ro.inspection_no AS reinspection_of_no,
               i.inspector_employee_id, e.employee_name AS inspector_name, i.status, i.decision, i.completed_at, i.remark,
               (SELECT GROUP_CONCAT(DISTINCT t.part_name_snapshot ORDER BY t.sub_no SEPARATOR ', ') FROM inspection_target t WHERE t.inspection_id = i.inspection_id) AS target_summary,
               (SELECT COUNT(*) FROM inspection_target t WHERE t.inspection_id = i.inspection_id) AS target_count,
               i.row_version
          FROM inspection i
          JOIN production_work w ON w.production_work_id = i.production_work_id
          LEFT JOIN inspection ro ON ro.inspection_id = i.reinspection_of_id
          LEFT JOIN employee e ON e.employee_id = i.inspector_employee_id
        """;

    // ───────────────────────── 조회 ─────────────────────────

    public async Task<IEnumerable<InspectionDto>> ListAsync(DateOnly? from, DateOnly? to, string? type, string? search, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        await using var conn = await db.OpenAsync(ct);
        return await conn.QueryAsync<InspectionDto>(
            InspectionSql + """
             WHERE i.is_deleted = 0 AND i.inspection_date BETWEEN @from AND @to AND (@type IS NULL OR i.inspection_type = @type)
               AND (@search IS NULL OR i.inspection_no LIKE @like OR w.lot_no LIKE @like
                    OR EXISTS (SELECT 1 FROM inspection_target t JOIN sales_order_item soi ON soi.sales_order_item_id = t.sales_order_item_id
                                WHERE t.inspection_id = i.inspection_id AND (t.part_name_snapshot LIKE @like OR soi.order_item_no LIKE @like OR t.customer_name_snapshot LIKE @like)))
             ORDER BY i.inspection_date DESC, i.inspection_no DESC
             LIMIT 1000
            """,
            new
            {
                from = (from ?? today.AddDays(-settings.GetInt(SettingKeys.SalesOrderListDefaultDays))).ToDateTime(TimeOnly.MinValue),
                to = (to ?? today).ToDateTime(TimeOnly.MinValue), type = string.IsNullOrEmpty(type) ? null : type,
                search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), like = $"%{search?.Trim()}%",
            });
    }

    public async Task<object> DetailAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await DetailCoreAsync(conn, null, id);
    }

    private static async Task<object> DetailCoreAsync(MySqlConnection conn, MySqlTransaction? tx, long id)
    {
        var inspection = await conn.QuerySingleOrDefaultAsync<InspectionDto>(InspectionSql + " WHERE i.inspection_id = @id AND i.is_deleted = 0", new { id }, tx)
            ?? throw new NotFoundException(Table, id);
        var targets = await conn.QueryAsync<InspectionTargetDto>(
            """
            SELECT t.inspection_target_id, t.sub_no, t.production_work_input_id, t.sales_order_item_id, soi.order_item_no, soi.part_id, t.customer_id,
                   t.customer_name_snapshot AS customer_name, t.customer_lot_snapshot AS customer_lot, t.part_name_snapshot AS part_name,
                   t.part_number_snapshot AS part_number, t.inspection_qty, t.submit_lot_no_snapshot AS submit_lot_no, t.report_issued_at, t.report_issue_count
              FROM inspection_target t JOIN sales_order_item soi ON soi.sales_order_item_id = t.sales_order_item_id
             WHERE t.inspection_id = @id ORDER BY t.sub_no
            """, new { id }, tx);
        var items = (await conn.QueryAsync<InspectionItemDto>(
            """
            SELECT inspection_item_id, inspection_criteria_id, sequence_no, item_type, item_name, location, result, decision, remark
              FROM inspection_item WHERE inspection_id = @id ORDER BY sequence_no
            """, new { id }, tx)).ToList();
        var samples = await conn.QueryAsync<MeasurementRow>(
            """
            SELECT m.inspection_item_id, m.sample_no, m.measured_value, m.measured_text
              FROM inspection_measurement m JOIN inspection_item ii ON ii.inspection_item_id = m.inspection_item_id
             WHERE ii.inspection_id = @id ORDER BY m.sample_no
            """, new { id }, tx);
        var byItem = samples.ToLookup(s => s.InspectionItemId);
        foreach (var item in items)
        {
            var list = byItem[item.InspectionItemId].ToList();
            var count = list.Count == 0 ? 0 : list.Max(s => s.SampleNo);
            item.Values = Enumerable.Range(1, count)
                .Select(n => list.FirstOrDefault(s => s.SampleNo == n) is { } s ? s.MeasuredText ?? s.MeasuredValue?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) : null)
                .ToList();
        }
        var criteria = inspection.InspectionStandardVersionId is { } v ? await CriteriaAsync(conn, tx, v) : [];
        return new { inspection, targets, items, criteria };
    }

    public async Task<IReadOnlyList<CriteriaDto>> CriteriaAsync(long versionId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await CriteriaAsync(conn, null, versionId);
    }

    private static async Task<IReadOnlyList<CriteriaDto>> CriteriaAsync(MySqlConnection conn, MySqlTransaction? tx, long versionId)
    {
        var criteria = (await conn.QueryAsync<CriteriaDto>(
            """
            SELECT inspection_criteria_id, sequence_no, item_type, item_name, location, specification_value, scale, range_type, lower_limit, upper_limit,
                   sample_count, test_count
              FROM inspection_criteria WHERE inspection_standard_version_id = @versionId ORDER BY sequence_no
            """, new { versionId }, tx)).ToList();
        var points = (await conn.QueryAsync<(long CriteriaId, int PointNo, string? Label)>(
            """
            SELECT CAST(p.inspection_criteria_id AS SIGNED), p.point_no, p.point_label
              FROM inspection_criteria_point p JOIN inspection_criteria c ON c.inspection_criteria_id = p.inspection_criteria_id
             WHERE c.inspection_standard_version_id = @versionId ORDER BY p.point_no
            """, new { versionId }, tx)).ToLookup(p => p.CriteriaId);
        foreach (var c in criteria) c.Points = points[c.InspectionCriteriaId].Select(p => p.Label).ToList();
        return criteria;
    }

    /// <summary>LOT 입력 (구 GetWorkSubsByLotNo) — 어떤 LOT 이든 (보통 주 LOT, I5). 투입 행 = 검사 대상 후보 + 품목 검사기준</summary>
    public async Task<object> LotAsync(string lotNo, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var work = await conn.QuerySingleOrDefaultAsync<LotRow>(
            """
            SELECT production_work_id, lot_no, status, unit_process_name_snapshot AS unit_process_name, equipment_name_snapshot AS equipment_name, submit_lot_no, work_date
              FROM production_work WHERE lot_no = @lotNo AND is_deleted = 0 AND status <> 'CANCELLED'
            """, new { lotNo = lotNo.Trim() })
            ?? throw new BusinessRuleException("LOT_NOT_FOUND", $"작업 LOT '{lotNo}' 이 없습니다.");
        var candidates = await conn.QueryAsync<TargetCandidateDto>(
            """
            SELECT pwi.production_work_input_id, pwi.sales_order_item_id, soi.order_item_no, soi.part_id, so.customer_id,
                   COALESCE(pwi.customer_name_snapshot, c.customer_name) AS customer_name, pwi.customer_lot_snapshot AS customer_lot,
                   pwi.part_name_snapshot AS part_name, pwi.part_number_snapshot AS part_number, pwi.input_qty, COALESCE(q.good_qty, pwi.input_qty) AS good_qty,
                   mw.lot_no AS main_lot_no,
                   (SELECT COUNT(*) FROM inspection_target t JOIN inspection i ON i.inspection_id = t.inspection_id
                     WHERE t.production_work_input_id = pwi.production_work_input_id AND i.is_deleted = 0 AND i.status <> 'CANCELLED') AS inspection_count,
                   (SELECT v.inspection_standard_version_id FROM inspection_standard s
                      JOIN inspection_standard_version v ON v.inspection_standard_id = s.inspection_standard_id AND v.is_current = 1
                     WHERE s.part_id = soi.part_id AND s.is_active = 1 AND (s.customer_id IS NULL OR s.customer_id = so.customer_id)
                     ORDER BY s.customer_id IS NULL LIMIT 1) AS inspection_standard_version_id
              FROM production_work_input pwi
              JOIN sales_order_item soi ON soi.sales_order_item_id = pwi.sales_order_item_id
              JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
              JOIN customer c ON c.customer_id = so.customer_id
              LEFT JOIN production_work mw ON mw.production_work_id = pwi.main_work_id
              LEFT JOIN vw_production_work_input_qty q ON q.production_work_input_id = pwi.production_work_input_id
             WHERE pwi.production_work_id = @ProductionWorkId AND pwi.status <> 'CANCELLED'
             ORDER BY pwi.production_work_input_id
            """, work);
        return new { work, candidates };
    }

    private sealed class LotRow
    {
        public long ProductionWorkId { get; init; }
        public string LotNo { get; init; } = "";
        public string Status { get; init; } = "";
        public string? UnitProcessName { get; init; }
        public string? EquipmentName { get; init; }
        public string? SubmitLotNo { get; init; }
        public DateTime WorkDate { get; init; }
    }

    // ───────────────────────── 저장 ─────────────────────────

    public async Task<object> CreateAsync(InspectionCreateRequest r, CancellationToken ct)
    {
        var type = r.InspectionType ?? "OUTGOING";
        if (!Types.Contains(type)) throw new RequestValidationException("inspectionType", "검사구분이 올바르지 않습니다.");
        var inputIds = Validate(r.InputIds, r.InspectorEmployeeId, r.Items);

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var numbers = await DocumentNumbers.LockAsync(conn, tx, "inspection");
        var no = await numbers.NextAsync("inspection", "inspection_no", settings.GetString(SettingKeys.InspectionNumberFormat), r.InspectionDate,
            new Dictionary<string, string> { ["TYPE"] = PrefixOf(type) });
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO inspection (inspection_no, inspection_type, inspection_date, production_work_id, inspection_standard_version_id,
                   inspector_employee_id, status, remark, created_by, updated_by)
            VALUES (@no, @type, @date, @ProductionWorkId, @InspectionStandardVersionId, @InspectorEmployeeId, 'IN_PROGRESS', @remark, @UserId, @UserId);
            SELECT LAST_INSERT_ID();
            """,
            new { no, type, date = r.InspectionDate.ToDateTime(TimeOnly.MinValue), r.ProductionWorkId, r.InspectionStandardVersionId, r.InspectorEmployeeId,
                  remark = Trim(r.Remark), currentUser.UserId }, tx);
        await SaveTargetsAsync(conn, tx, id, r.ProductionWorkId, inputIds);
        var decision = await SaveItemsAsync(conn, tx, id, r.InspectionStandardVersionId, r.Items ?? []);
        await conn.ExecuteAsync("UPDATE inspection SET decision = @decision WHERE inspection_id = @id", new { id, decision }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, Table, id, null, new { inspection_no = no, type, r.ProductionWorkId, inputIds, decision });
        await tx.CommitAsync(ct);
        return new { inspectionId = id, inspectionNo = no, decision };
    }

    public async Task<object> UpdateAsync(long id, InspectionUpdateRequest r, CancellationToken ct)
    {
        var inputIds = Validate(r.InputIds, r.InspectorEmployeeId, r.Items);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id, r.RowVersion);
        if (before.Status is not ("WAITING" or "IN_PROGRESS"))
            throw new BusinessRuleException("INSPECTION_NOT_EDITABLE", "확정·취소된 검사는 고칠 수 없습니다. 확정 후 수정은 재검사로 하세요.");
        await conn.ExecuteAsync(
            """
            UPDATE inspection SET inspection_date = @date, inspection_standard_version_id = @InspectionStandardVersionId,
                   inspector_employee_id = @InspectorEmployeeId, remark = @remark, updated_by = @UserId, row_version = row_version + 1
             WHERE inspection_id = @id
            """, new { id, date = r.InspectionDate.ToDateTime(TimeOnly.MinValue), r.InspectionStandardVersionId, r.InspectorEmployeeId, remark = Trim(r.Remark), currentUser.UserId }, tx);
        await SaveTargetsAsync(conn, tx, id, before.ProductionWorkId, inputIds);
        await conn.ExecuteAsync(
            "DELETE m FROM inspection_measurement m JOIN inspection_item ii ON ii.inspection_item_id = m.inspection_item_id WHERE ii.inspection_id = @id", new { id }, tx);
        await conn.ExecuteAsync("DELETE FROM inspection_item WHERE inspection_id = @id", new { id }, tx);
        var decision = await SaveItemsAsync(conn, tx, id, r.InspectionStandardVersionId, r.Items ?? []);
        await conn.ExecuteAsync("UPDATE inspection SET decision = @decision WHERE inspection_id = @id", new { id, decision }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id, new { before.Decision }, new { inputIds, decision, items = r.Items?.Length });
        await tx.CommitAsync(ct);
        return new { inspectionId = id, decision };
    }

    /// <summary>확정 — 불합격이면 대상마다 부적합 등록 (수량 = 대상 수량 전체, 발견 공정 = 대상 투입 행, 주 LOT 병기)</summary>
    public async Task<object> CompleteAsync(long id, InspectionVersionRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id, r.RowVersion);
        if (before.Status is not ("WAITING" or "IN_PROGRESS"))
            throw new BusinessRuleException("INSPECTION_NOT_EDITABLE", "이미 확정되었거나 취소된 검사입니다.");
        if (before.Decision is null)
            throw new BusinessRuleException("DECISION_REQUIRED", "판정된 항목이 없습니다. 측정값을 넣거나 항목 판정을 고른 뒤 확정하세요.");
        if (before.TargetCount == 0) throw new BusinessRuleException("NO_TARGET", "검사 대상이 없습니다.");

        var now = time.GetLocalNow().DateTime;
        await conn.ExecuteAsync(
            "UPDATE inspection SET status = 'COMPLETED', completed_at = @now, updated_by = @UserId, row_version = row_version + 1 WHERE inspection_id = @id",
            new { id, now, currentUser.UserId }, tx);
        var defects = 0;
        if (before.Decision == InspectionJudge.Fail)
            defects = await conn.ExecuteAsync(
                """
                INSERT INTO defect_occurrence (sales_order_item_id, production_work_id, production_work_input_id, inspection_target_id, main_work_id,
                       defect_date, defect_qty, defect_weight, status, created_by, updated_by)
                SELECT t.sales_order_item_id, pwi.production_work_id, t.production_work_input_id, t.inspection_target_id, pwi.main_work_id,
                       i.inspection_date, COALESCE(t.inspection_qty, pwi.input_qty), t.inspection_weight, 'OPEN', @UserId, @UserId
                  FROM inspection_target t
                  JOIN inspection i ON i.inspection_id = t.inspection_id
                  JOIN production_work_input pwi ON pwi.production_work_input_id = t.production_work_input_id
                 WHERE t.inspection_id = @id
                """, new { id, currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id, new { status = before.Status },
            new { status = "COMPLETED", before.Decision, defects }, r.Reason);
        await tx.CommitAsync(ct);
        return new { decision = before.Decision, defects };
    }

    /// <summary>재검사 (확정 후 수정, 구 BtnReInspectionSave) — 새 번호로 대상·측정값을 복사해 미확정 검사를 만든다. 원 검사는 그대로</summary>
    public async Task<object> ReinspectAsync(long id, InspectionVersionRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var origin = await LockAsync(conn, tx, id, r.RowVersion);
        if (origin.Status != "COMPLETED") throw new BusinessRuleException("INSPECTION_NOT_COMPLETED", "확정된 검사만 재검사할 수 있습니다. 미확정 검사는 그대로 고치세요.");
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        await using var numbers = await DocumentNumbers.LockAsync(conn, tx, "inspection");
        var no = await numbers.NextAsync("inspection", "inspection_no", settings.GetString(SettingKeys.InspectionNumberFormat), today,
            new Dictionary<string, string> { ["TYPE"] = PrefixOf(origin.InspectionType) });
        var newId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO inspection (inspection_no, inspection_type, inspection_date, production_work_id, inspection_standard_version_id, reinspection_of_id,
                   inspector_employee_id, status, decision, remark, created_by, updated_by)
            SELECT @no, inspection_type, @today, production_work_id, inspection_standard_version_id, inspection_id,
                   inspector_employee_id, 'IN_PROGRESS', decision, @reason, @UserId, @UserId
              FROM inspection WHERE inspection_id = @id;
            SELECT LAST_INSERT_ID();
            """, new { no, today = today.ToDateTime(TimeOnly.MinValue), id, reason = Trim(r.Reason), currentUser.UserId }, tx);
        await conn.ExecuteAsync(
            """
            INSERT INTO inspection_target (inspection_id, sub_no, production_work_input_id, sales_order_item_id, customer_id, print_template_id, inspection_qty,
                   inspection_weight, submit_lot_no_snapshot, customer_name_snapshot, customer_lot_snapshot, part_name_snapshot, part_number_snapshot,
                   specification_snapshot, model_snapshot, material_snapshot)
            SELECT @newId, sub_no, production_work_input_id, sales_order_item_id, customer_id, print_template_id, inspection_qty,
                   inspection_weight, submit_lot_no_snapshot, customer_name_snapshot, customer_lot_snapshot, part_name_snapshot, part_number_snapshot,
                   specification_snapshot, model_snapshot, material_snapshot
              FROM inspection_target WHERE inspection_id = @id
            """, new { id, newId }, tx);
        foreach (var itemId in await conn.QueryAsync<long>("SELECT CAST(inspection_item_id AS SIGNED) FROM inspection_item WHERE inspection_id = @id ORDER BY sequence_no", new { id }, tx))
        {
            var copy = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO inspection_item (inspection_id, inspection_criteria_id, sequence_no, item_type, item_name, location, result, decision, remark)
                SELECT @newId, inspection_criteria_id, sequence_no, item_type, item_name, location, result, decision, remark FROM inspection_item WHERE inspection_item_id = @itemId;
                SELECT LAST_INSERT_ID();
                """, new { newId, itemId }, tx);
            await conn.ExecuteAsync(
                """
                INSERT INTO inspection_measurement (inspection_item_id, sample_no, measured_value, measured_text, unit_code, result)
                SELECT @copy, sample_no, measured_value, measured_text, unit_code, result FROM inspection_measurement WHERE inspection_item_id = @itemId
                """, new { copy, itemId }, tx);
        }
        await audit.WriteAsync(conn, tx, AuditAction.Create, Table, newId, null, new { inspection_no = no, reinspection_of = origin.InspectionNo }, r.Reason);
        await tx.CommitAsync(ct);
        return new { inspectionId = newId, inspectionNo = no };
    }

    public async Task CancelAsync(long id, InspectionVersionRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id, r.RowVersion);
        if (before.Status is not ("WAITING" or "IN_PROGRESS"))
            throw new BusinessRuleException("INSPECTION_NOT_EDITABLE", "확정된 검사는 취소할 수 없습니다 (부적합 처리·재검사로).");
        await conn.ExecuteAsync("UPDATE inspection SET status = 'CANCELLED', updated_by = @UserId, row_version = row_version + 1 WHERE inspection_id = @id",
            new { id, currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.StatusChange, Table, id, new { status = before.Status }, new { status = "CANCELLED" }, r.Reason);
        await tx.CommitAsync(ct);
    }

    /// <summary>성적서 발행 대상 확인 — 이 검사의 대상인지</summary>
    public async Task EnsureTargetAsync(long targetId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        if (await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM inspection_target t JOIN inspection i ON i.inspection_id = t.inspection_id WHERE t.inspection_target_id = @targetId AND i.is_deleted = 0 AND i.status <> 'CANCELLED'",
                new { targetId }) == 0)
            throw new NotFoundException("inspection_target", targetId);
    }

    // ───────────────────────── 공통 ─────────────────────────

    private static long[] Validate(long[]? inputIds, long? inspectorId, InspectionItemInput[]? items)
    {
        var errors = new Dictionary<string, string[]>();
        var ids = (inputIds ?? []).Distinct().ToArray();
        if (ids.Length == 0) errors["inputIds"] = ["검사 대상을 1개 이상 고르세요."];
        if (inspectorId is null) errors["inspectorEmployeeId"] = ["검사자를 고르세요."];
        foreach (var (item, i) in (items ?? []).Select((x, i) => (x, i)))
        {
            if (string.IsNullOrWhiteSpace(item.ItemName)) errors[$"items[{i}]"] = ["항목명이 필요합니다."];
            else if (item.Values?.Length > 50) errors[$"items[{i}]"] = ["측정값은 50개까지입니다."];
            else if (item.Decision is not (null or "" or InspectionJudge.Pass or InspectionJudge.Fail or InspectionJudge.NotApplicable))
                errors[$"items[{i}]"] = ["판정 값이 올바르지 않습니다."];
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return ids;
    }

    /// <summary>대상 = LOT 의 투입 행. 바뀐 것만 넣고 빼서 기존 대상(성적서 발행 이력 연결)을 유지한다</summary>
    private static async Task SaveTargetsAsync(MySqlConnection conn, MySqlTransaction tx, long id, long workId, long[] inputIds)
    {
        var valid = (await conn.QueryAsync<long>(
            "SELECT CAST(production_work_input_id AS SIGNED) FROM production_work_input WHERE production_work_id = @workId AND status <> 'CANCELLED' AND production_work_input_id IN @inputIds",
            new { workId, inputIds }, tx)).ToHashSet();
        if (inputIds.Any(x => !valid.Contains(x))) throw new RequestValidationException("inputIds", "이 LOT 의 투입 행이 아닌 대상이 있습니다.");

        var existing = (await conn.QueryAsync<(long TargetId, long InputId)>(
            "SELECT CAST(inspection_target_id AS SIGNED), CAST(production_work_input_id AS SIGNED) FROM inspection_target WHERE inspection_id = @id", new { id }, tx)).ToList();
        foreach (var gone in existing.Where(e => !inputIds.Contains(e.InputId)))
            await conn.ExecuteAsync("DELETE FROM inspection_target WHERE inspection_target_id = @TargetId", new { gone.TargetId }, tx);
        // sub_no 재부여: 임시로 비킨 뒤 1..N
        await conn.ExecuteAsync("UPDATE inspection_target SET sub_no = sub_no + 100000 WHERE inspection_id = @id", new { id }, tx);
        for (var i = 0; i < inputIds.Length; i++)
        {
            if (existing.Any(e => e.InputId == inputIds[i]))
            {
                await conn.ExecuteAsync("UPDATE inspection_target SET sub_no = @sub WHERE inspection_id = @id AND production_work_input_id = @inputId",
                    new { id, sub = i + 1, inputId = inputIds[i] }, tx);
                continue;
            }
            await conn.ExecuteAsync(
                """
                INSERT INTO inspection_target (inspection_id, sub_no, production_work_input_id, sales_order_item_id, customer_id, inspection_qty, inspection_weight,
                       submit_lot_no_snapshot, customer_name_snapshot, customer_lot_snapshot, part_name_snapshot, part_number_snapshot,
                       specification_snapshot, model_snapshot, material_snapshot)
                SELECT @id, @sub, pwi.production_work_input_id, pwi.sales_order_item_id, so.customer_id, pwi.input_qty, pwi.input_weight,
                       COALESCE(mw.submit_lot_no, w.submit_lot_no), COALESCE(pwi.customer_name_snapshot, c.customer_name), pwi.customer_lot_snapshot,
                       pwi.part_name_snapshot, pwi.part_number_snapshot, pwi.specification_snapshot, pwi.model_snapshot, pwi.material_snapshot
                  FROM production_work_input pwi
                  JOIN production_work w ON w.production_work_id = pwi.production_work_id
                  LEFT JOIN production_work mw ON mw.production_work_id = pwi.main_work_id
                  JOIN sales_order_item soi ON soi.sales_order_item_id = pwi.sales_order_item_id
                  JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
                  JOIN customer c ON c.customer_id = so.customer_id
                 WHERE pwi.production_work_input_id = @inputId
                """, new { id, sub = i + 1, inputId = inputIds[i] }, tx);
        }
    }

    /// <summary>항목·측정값 저장 + 판정 (기준 항목이면 기준 범위로 자동) — 종합 판정을 돌려준다</summary>
    private static async Task<string?> SaveItemsAsync(MySqlConnection conn, MySqlTransaction tx, long id, long? versionId, InspectionItemInput[] items)
    {
        var criteria = versionId is { } v ? (await CriteriaAsync(conn, tx, v)).ToDictionary(c => c.InspectionCriteriaId) : [];
        var decisions = new List<string?>();
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            CriteriaDto? c = null;
            if (item.InspectionCriteriaId is { } cid && !criteria.TryGetValue(cid, out c))
                throw new RequestValidationException($"items[{i}]", "검사기준에 없는 항목입니다.");
            var judged = InspectionJudge.Judge(c?.RangeType, c?.LowerLimit, c?.UpperLimit, item.Values ?? [], string.IsNullOrEmpty(item.Decision) ? null : item.Decision);
            decisions.Add(judged.Decision);
            var itemId = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO inspection_item (inspection_id, inspection_criteria_id, sequence_no, item_type, item_name, location, result, decision, remark)
                VALUES (@id, @criteriaId, @seq, @type, @name, @location, @result, @decision, @remark);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    id, criteriaId = item.InspectionCriteriaId, seq = i + 1, type = Trim(item.ItemType) ?? c?.ItemType, name = item.ItemName!.Trim(),
                    location = Trim(item.Location) ?? c?.Location, result = judged.Result, decision = judged.Decision, remark = Trim(item.Remark),
                }, tx);
            foreach (var s in judged.Samples)
                await conn.ExecuteAsync(
                    """
                    INSERT INTO inspection_measurement (inspection_item_id, sample_no, measured_value, measured_text, result)
                    VALUES (@itemId, @SampleNo, @Value, @Text, @Result)
                    """, new { itemId, s.SampleNo, s.Value, s.Text, s.Result }, tx);
        }
        return InspectionJudge.Overall(decisions);
    }

    private static async Task<InspectionDto> LockAsync(MySqlConnection conn, MySqlTransaction tx, long id, int rowVersion)
    {
        var version = await conn.ExecuteScalarAsync<int?>("SELECT row_version FROM inspection WHERE inspection_id = @id AND is_deleted = 0 FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException(Table, id);
        if (version != rowVersion) throw new ConcurrencyConflictException(Table, id);
        return await conn.QuerySingleAsync<InspectionDto>(InspectionSql + " WHERE i.inspection_id = @id", new { id }, tx);
    }

    /// <summary>검사번호 {TYPE} = 공통코드 INSPECTION_TYPE 의 attr prefix (TI/TP/TO)</summary>
    private string PrefixOf(string type)
    {
        var attr = codes.GetGroup("INSPECTION_TYPE").Codes.FirstOrDefault(c => c.Code == type)?.AttrJson;
        return attr is null ? type : JsonDocument.Parse(attr).RootElement.TryGetProperty("prefix", out var p) ? p.GetString() ?? type : type;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
