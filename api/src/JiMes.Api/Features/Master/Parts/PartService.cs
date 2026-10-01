using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Files;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Features.Master.Parts;

/// <summary>
/// 품목 (구 F_PartForm / F_PartDetailForm). 품목·거래처별 품번·적용 공정은 한 번에 저장하고,
/// 변경마다 part_history(전·후 JSON)와 audit_log 를 남긴다 (설계 §1.3 — 품목은 Version 없이 History + 거래 Snapshot).
/// 도면·이미지는 공통 첨부(attachment)로 DB 에 보관 (구 PC 폴더 PartDrawingFolder·PartImageFolder 폐지).
/// </summary>
public sealed class PartService(
    IDbConnectionFactory db, CommonCodeCache codes, AuditWriter audit, ICurrentUser currentUser, AttachmentStore attachments)
{
    public const string AttachmentOwner = "part";

    private const string PartSelect =
        """
        SELECT p.part_id, p.part_code, p.part_name, p.part_number, p.specification, p.model, p.material, p.unit_weight, p.unit_code,
               p.price_basis, p.unit_price, p.drawing_no, p.hardness, p.core_hardness, p.effective_hardening_depth, p.grade, p.texture,
               p.remark, p.is_active, p.updated_at,
               (SELECT GROUP_CONCAT(c.customer_name ORDER BY pc.is_primary DESC, c.customer_name SEPARATOR ', ')
                  FROM part_customer pc JOIN customer c ON c.customer_id = pc.customer_id
                 WHERE pc.part_id = p.part_id AND pc.is_active = 1) AS customer_names,
               (SELECT h.heat_process_name FROM part_heat_process ph JOIN heat_process h ON h.heat_process_id = ph.heat_process_id
                 WHERE ph.part_id = p.part_id AND ph.is_default = 1 AND ph.is_active = 1 LIMIT 1) AS default_heat_process_name,
               (SELECT COUNT(*) FROM attachment a WHERE a.owner_table = 'part' AND a.owner_id = p.part_id) AS attachment_count
          FROM part p
        """;

    public async Task<(IReadOnlyList<PartDto> Items, long Total)> ListAsync(
        string? search, long? customerId, bool includeInactive, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);
        const string where =
            """
             WHERE (@includeInactive OR p.is_active = 1)
               AND (@search IS NULL OR p.part_code LIKE @like OR p.part_name LIKE @like OR p.part_number LIKE @like OR p.drawing_no LIKE @like
                    OR EXISTS (SELECT 1 FROM part_customer pc WHERE pc.part_id = p.part_id AND pc.customer_part_code LIKE @like))
               AND (@customerId IS NULL OR EXISTS (SELECT 1 FROM part_customer pc WHERE pc.part_id = p.part_id AND pc.customer_id = @customerId))
            """;
        var args = new
        {
            includeInactive, customerId, search = string.IsNullOrWhiteSpace(search) ? null : search,
            like = $"%{search?.Trim()}%", pageSize, offset = (page - 1) * pageSize,
        };
        await using var conn = await db.OpenAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM part p" + where, args);
        var items = await conn.QueryAsync<PartDto>(PartSelect + where + " ORDER BY p.part_name, p.part_code LIMIT @pageSize OFFSET @offset", args);
        return (items.ToList(), total);
    }

    public async Task<PartDetail> GetAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await DetailAsync(conn, null, id) ?? throw new NotFoundException("part", id);
    }

    public async Task<long> SaveAsync(long? id, PartSaveRequest r, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        string? Text(string field, string? value, int max, bool required = false)
        {
            var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (v is null && required) errors[field] = ["필수입니다."];
            else if (v is not null && v.Length > max) errors[field] = [$"{max}자 이하여야 합니다."];
            return v;
        }
        var code = Text("partCode", r.PartCode, 50, required: true);
        var name = Text("partName", r.PartName, 100, required: true);
        var number = Text("partNumber", r.PartNumber, 100);
        var basis = r.PriceBasis ?? "EA";
        if (codes.GetGroup("PRICE_BASIS").Codes.All(c => c.Code != basis || !c.IsActive))
            errors["priceBasis"] = ["허용되지 않는 단가 구분입니다."];
        if (r.UnitPrice is < 0) errors["unitPrice"] = ["0 이상이어야 합니다."];
        if (r.UnitWeight is < 0) errors["unitWeight"] = ["0 이상이어야 합니다."];
        var customers = (r.Customers ?? []).DistinctBy(c => c.CustomerId).ToList();
        var processes = (r.HeatProcesses ?? []).DistinctBy(h => h.HeatProcessId).ToList();
        if (customers.Count(c => c.IsPrimary) > 1) errors["customers"] = ["주 거래처는 1개만 지정합니다."];
        if (processes.Count(h => h.IsDefault) > 1) errors["heatProcesses"] = ["기본 공정은 1개만 지정합니다."];
        var values = new
        {
            code, name, number, spec = Text("specification", r.Specification, 100), model = Text("model", r.Model, 100),
            material = Text("material", r.Material, 100), r.UnitWeight, unitCode = Text("unitCode", r.UnitCode, 20), basis, r.UnitPrice,
            drawingNo = Text("drawingNo", r.DrawingNo, 100), hardness = Text("hardness", r.Hardness, 100),
            core = Text("coreHardness", r.CoreHardness, 100), depth = Text("effectiveHardeningDepth", r.EffectiveHardeningDepth, 100),
            grade = Text("grade", r.Grade, 50), texture = Text("texture", r.Texture, 100), remark = r.Remark?.Trim(), r.IsActive,
            userId = currentUser.UserId, id = id ?? 0,
        };
        if (errors.Count > 0) throw new RequestValidationException(errors);

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM part WHERE part_code = @code AND part_id <> @id", values, tx) > 0)
            throw new RequestValidationException("partCode", "이미 사용 중인 품목 코드입니다.");
        if (number is not null && !r.AllowDuplicatePartNumber)
        {
            var dup = await conn.QueryFirstOrDefaultAsync<string?>(
                "SELECT CONCAT(part_name, ' (', part_code, ')') FROM part WHERE part_number = @number AND part_id <> @id LIMIT 1", values, tx);
            if (dup is not null)
                throw new BusinessRuleException("DUPLICATE_PART_NUMBER", $"같은 품번을 쓰는 품목이 있습니다: {dup}. 그래도 저장하려면 확인하세요.");
        }
        await EnsureExistsAsync(conn, tx, "customer", customers.Select(c => c.CustomerId), "customers");
        await EnsureExistsAsync(conn, tx, "heat_process", processes.Select(h => h.HeatProcessId), "heatProcesses");

        PartDetail? before = null;
        long partId;
        if (id is null)
        {
            partId = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO part (part_code, part_name, part_number, specification, model, material, unit_weight, unit_code, price_basis,
                                  unit_price, drawing_no, hardness, core_hardness, effective_hardening_depth, grade, texture, remark, is_active,
                                  created_by, updated_by)
                VALUES (@code, @name, @number, @spec, @model, @material, @UnitWeight, @unitCode, @basis, @UnitPrice, @drawingNo, @hardness,
                        @core, @depth, @grade, @texture, @remark, @IsActive, @userId, @userId);
                SELECT LAST_INSERT_ID();
                """, values, tx);
        }
        else
        {
            partId = id.Value;
            before = await DetailAsync(conn, tx, partId, forUpdate: true) ?? throw new NotFoundException("part", partId);
            await conn.ExecuteAsync(
                """
                UPDATE part SET part_code = @code, part_name = @name, part_number = @number, specification = @spec, model = @model,
                       material = @material, unit_weight = @UnitWeight, unit_code = @unitCode, price_basis = @basis, unit_price = @UnitPrice,
                       drawing_no = @drawingNo, hardness = @hardness, core_hardness = @core, effective_hardening_depth = @depth,
                       grade = @grade, texture = @texture, remark = @remark, is_active = @IsActive, updated_by = @userId
                 WHERE part_id = @id
                """, values, tx);
        }

        // 거래처·공정: 목록에서 빠진 행은 사용 중지 (수주·검사가 참조할 수 있어 지우지 않음)
        await conn.ExecuteAsync("UPDATE part_customer SET is_active = 0, is_primary = 0 WHERE part_id = @partId", new { partId }, tx);
        foreach (var c in customers)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO part_customer (part_id, customer_id, customer_part_code, is_customer_lot_required, is_primary, is_active)
                VALUES (@partId, @CustomerId, @code, @IsCustomerLotRequired, @IsPrimary, 1)
                ON DUPLICATE KEY UPDATE customer_part_code = VALUES(customer_part_code), is_customer_lot_required = VALUES(is_customer_lot_required),
                                        is_primary = VALUES(is_primary), is_active = 1
                """, new { partId, c.CustomerId, code = string.IsNullOrWhiteSpace(c.CustomerPartCode) ? null : c.CustomerPartCode.Trim(), c.IsCustomerLotRequired, c.IsPrimary }, tx);
        }
        await conn.ExecuteAsync("UPDATE part_heat_process SET is_active = 0, is_default = 0 WHERE part_id = @partId", new { partId }, tx);
        foreach (var h in processes)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO part_heat_process (part_id, heat_process_id, is_default, is_active) VALUES (@partId, @HeatProcessId, @IsDefault, 1)
                ON DUPLICATE KEY UPDATE is_default = VALUES(is_default), is_active = 1
                """, new { partId, h.HeatProcessId, h.IsDefault }, tx);
        }

        var after = await DetailAsync(conn, tx, partId);
        var snapshotBefore = before is null ? null : Snapshot(before);
        var snapshotAfter = Snapshot(after!);
        await conn.ExecuteAsync(
            """
            INSERT INTO part_history (part_id, changed_by, change_type, old_data_json, new_data_json)
            VALUES (@partId, @userId, @type, @old, @new)
            """,
            new
            {
                partId, userId = currentUser.UserId, type = before is null ? "CREATE" : "UPDATE",
                old = snapshotBefore is null ? null : System.Text.Json.JsonSerializer.Serialize(snapshotBefore, AuditWriter.JsonOptions),
                @new = System.Text.Json.JsonSerializer.Serialize(snapshotAfter, AuditWriter.JsonOptions),
            }, tx);
        await audit.WriteAsync(conn, tx, before is null ? AuditAction.Create : AuditAction.Update, "part", partId, snapshotBefore, snapshotAfter, r.Reason);
        await tx.CommitAsync(ct);
        return partId;
    }

    /// <summary>품목(+업체)·용도별 기본 양식 연결 (§5.3.1 양식 선택 1·2순위). 목록 전체를 바꾼다.</summary>
    public async Task SavePrintTemplatesAsync(long partId, PartPrintTemplateInput[] links, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM part WHERE part_id = @partId FOR UPDATE", new { partId }, tx) == 0)
            throw new NotFoundException("part", partId);

        var rows = new List<(long TemplateId, long? CustomerId, long PurposeId, bool IsDefault)>();
        foreach (var l in links.DistinctBy(l => (l.PrintTemplateId, l.CustomerId)))
        {
            var purposeId = await conn.ExecuteScalarAsync<long?>(
                "SELECT print_purpose_id FROM print_template WHERE print_template_id = @PrintTemplateId AND is_active = 1", l, tx)
                ?? throw new RequestValidationException("printTemplates", $"없는 양식입니다 (#{l.PrintTemplateId}).");
            rows.Add((l.PrintTemplateId, l.CustomerId, purposeId, l.IsDefault));
        }
        if (rows.Where(x => x.IsDefault).GroupBy(x => (x.CustomerId, x.PurposeId)).Any(g => g.Count() > 1))
            throw new RequestValidationException("printTemplates", "같은 거래처·용도의 기본 양식은 1개입니다.");

        var before = await conn.QueryAsync<PartPrintTemplateDto>(PrintTemplateSelect, new { partId }, tx);
        await conn.ExecuteAsync("DELETE FROM part_print_template WHERE part_id = @partId", new { partId }, tx);
        foreach (var x in rows)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO part_print_template (part_id, customer_id, print_purpose_id, print_template_id, is_default)
                VALUES (@partId, @CustomerId, @PurposeId, @TemplateId, @IsDefault)
                """, new { partId, x.CustomerId, x.PurposeId, x.TemplateId, x.IsDefault }, tx);
        }
        await audit.WriteAsync(conn, tx, AuditAction.Update, "part", partId, new { print_templates = before },
            new { print_templates = rows.Select(x => new { x.TemplateId, x.CustomerId, x.PurposeId, x.IsDefault }) }, "성적서 양식 연결");
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<PartHistoryDto>> HistoryAsync(long partId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return (await conn.QueryAsync<PartHistoryDto>(
            """
            SELECT h.part_history_id, h.changed_at, u.user_name AS changed_by_name, h.change_type, h.old_data_json, h.new_data_json
              FROM part_history h LEFT JOIN app_user u ON u.app_user_id = h.changed_by
             WHERE h.part_id = @partId ORDER BY h.part_history_id DESC
            """, new { partId })).ToList();
    }

    // ───────────────────────── 첨부 ─────────────────────────

    public Task<long> AddAttachmentAsync(long partId, string kind, string fileName, string? contentType, byte[] content, string? caption, CancellationToken ct) =>
        attachments.AddAsync(AttachmentOwner, partId, kind, fileName, contentType, content, caption, ct);

    public Task<(byte[] Content, string FileName, string ContentType)> GetAttachmentAsync(long partId, long attachmentId, CancellationToken ct) =>
        attachments.GetAsync(AttachmentOwner, partId, attachmentId, ct);

    public Task DeleteAttachmentAsync(long partId, long attachmentId, CancellationToken ct) =>
        attachments.DeleteAsync(AttachmentOwner, partId, attachmentId, ct);

    // ───────────────────────── 내부 ─────────────────────────

    private const string PrintTemplateSelect =
        """
        SELECT ppt.print_template_id, t.print_template_name, pu.purpose_code, pu.purpose_name, ppt.customer_id, c.customer_name, ppt.is_default
          FROM part_print_template ppt
          JOIN print_template t  ON t.print_template_id = ppt.print_template_id
          JOIN print_purpose pu  ON pu.print_purpose_id = ppt.print_purpose_id
          LEFT JOIN customer c   ON c.customer_id = ppt.customer_id
         WHERE ppt.part_id = @partId
         ORDER BY pu.sort_order, c.customer_name
        """;

    private static async Task<PartDetail?> DetailAsync(MySqlConnection conn, MySqlTransaction? tx, long partId, bool forUpdate = false)
    {
        var part = await conn.QuerySingleOrDefaultAsync<PartDto>(PartSelect + $" WHERE p.part_id = @partId{(forUpdate ? " FOR UPDATE" : "")}", new { partId }, tx);
        if (part is null) return null;
        var customers = await conn.QueryAsync<PartCustomerDto>(
            """
            SELECT pc.customer_id, c.customer_name, pc.customer_part_code, pc.is_customer_lot_required, pc.is_primary, pc.is_active
              FROM part_customer pc JOIN customer c ON c.customer_id = pc.customer_id
             WHERE pc.part_id = @partId AND pc.is_active = 1 ORDER BY pc.is_primary DESC, c.customer_name
            """, new { partId }, tx);
        var processes = await conn.QueryAsync<PartHeatProcessDto>(
            """
            SELECT ph.heat_process_id, h.heat_process_name, ph.is_default, ph.is_active
              FROM part_heat_process ph JOIN heat_process h ON h.heat_process_id = ph.heat_process_id
             WHERE ph.part_id = @partId AND ph.is_active = 1 ORDER BY ph.is_default DESC, h.heat_process_name
            """, new { partId }, tx);
        var templates = await conn.QueryAsync<PartPrintTemplateDto>(PrintTemplateSelect, new { partId }, tx);
        var files = await AttachmentStore.ListAsync(conn, tx, AttachmentOwner, partId);
        return new PartDetail(part, customers.ToList(), processes.ToList(), templates.ToList(), files);
    }

    /// <summary>이력·감사용 — 계산 컬럼(갱신 시각, 첨부 수 등)은 뺀다</summary>
    private static object Snapshot(PartDetail d) => new
    {
        d.Part.PartCode, d.Part.PartName, d.Part.PartNumber, d.Part.Specification, d.Part.Model, d.Part.Material, d.Part.UnitWeight,
        d.Part.UnitCode, d.Part.PriceBasis, d.Part.UnitPrice, d.Part.DrawingNo, d.Part.Hardness, d.Part.CoreHardness,
        d.Part.EffectiveHardeningDepth, d.Part.Grade, d.Part.Texture, d.Part.Remark, d.Part.IsActive,
        Customers = d.Customers.Select(c => new { c.CustomerId, c.CustomerPartCode, c.IsCustomerLotRequired, c.IsPrimary }),
        HeatProcesses = d.HeatProcesses.Select(h => new { h.HeatProcessId, h.IsDefault }),
    };

    private static async Task EnsureExistsAsync(MySqlConnection conn, MySqlTransaction tx, string table, IEnumerable<long> ids, string field)
    {
        var list = ids.Distinct().ToArray();
        if (list.Length > 0 && await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE {table}_id IN @list", new { list }, tx) != list.Length)
            throw new RequestValidationException(field, "없는 항목이 있습니다.");
    }
}
