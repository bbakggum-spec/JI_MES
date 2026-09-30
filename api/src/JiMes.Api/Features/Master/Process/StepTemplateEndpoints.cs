using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Features.Master.Process;

public sealed class StepTemplateDto
{
    public long StepTemplateId { get; init; }
    public string StepTemplateCode { get; init; } = "";
    public string StepTemplateName { get; init; } = "";
    public long UnitProcessId { get; init; }
    public string? UnitProcessName { get; init; }
    public long? EquipmentTypeId { get; init; }
    public string? EquipmentTypeName { get; init; }
    public long? EquipmentId { get; init; }
    public string? EquipmentName { get; init; }
    public bool IsActive { get; init; }
    public long StepCount { get; init; }
    public long ConditionCount { get; init; }
    public long UsageCount { get; init; }
}

public sealed class StepItemDto
{
    public long StepTemplateItemId { get; init; }
    public int SequenceNo { get; init; }
    public string StepName { get; init; } = "";
}

public sealed class TemplateConditionDto
{
    public long ConditionItemId { get; init; }
    public string ConditionItemName { get; init; } = "";
    public string? UnitCode { get; init; }
    public string ValueType { get; init; } = "NUMBER";
    public int SequenceNo { get; init; }
}

public sealed class ConditionItemRowDto
{
    public long ConditionItemId { get; init; }
    public string ConditionItemCode { get; init; } = "";
    public string ConditionItemName { get; init; } = "";
    public string? UnitCode { get; init; }
    public string ValueType { get; init; } = "NUMBER";
    public bool IsActive { get; init; }
}

public sealed record StepTemplateDetail(StepTemplateDto Header, IReadOnlyList<StepItemDto> Steps, IReadOnlyList<TemplateConditionDto> Conditions);

/// <param name="Steps">스텝(열) 이름 — 순서 = 열 순서</param>
/// <param name="ConditionItemIds">관리항목(행) — 순서 = 행 순서</param>
public sealed record StepTemplateSaveRequest(
    string? StepTemplateCode, string? StepTemplateName, long UnitProcessId, long? EquipmentTypeId, long? EquipmentId, bool IsActive,
    string[]? Steps, long[]? ConditionItemIds);

/// <summary>
/// 단계 템플릿 (구 t_standardtemplate row/column, F_StandardTemplateAdd) — 설비(유형) × 단위공정의 스텝(열)과 관리항목(행).
/// 작업표준 입력표를 처음 채우는 초기값일 뿐이다 (표준은 스텝·항목을 Version 마다 따로 저장, §22.5).
/// 그래서 템플릿을 고쳐도 기존 작업표준은 바뀌지 않고, 스텝·항목은 자유롭게 바꿀 수 있다.
/// </summary>
public static class StepTemplateEndpoints
{
    public static void MapStepTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/step-templates").WithTags("StepTemplate");
        const string key = MenuKeys.MasterStepTemplate;
        g.MapGet("/", ListAsync).RequirePermission(key, PermissionAction.Read);
        // 작업표준 화면이 템플릿을 고를 때도 쓴다
        g.MapGet("/{id:long}", async (long id, IDbConnectionFactory db, CancellationToken ct) =>
            {
                await using var conn = await db.OpenAsync(ct);
                return Results.Ok(await DetailAsync(conn, null, id) ?? throw new NotFoundException("step_template", id));
            })
            .RequireLogin();
        // 입력표 행 후보 (이름·단위·값 형식) — 템플릿·작업표준 화면의 "관리항목 추가"
        g.MapGet("/condition-items", async (IDbConnectionFactory db, CancellationToken ct) =>
            {
                await using var conn = await db.OpenAsync(ct);
                return Results.Ok(await conn.QueryAsync<ConditionItemRowDto>(
                    """
                    SELECT condition_item_id, condition_item_code, condition_item_name, unit_code, value_type, is_active
                      FROM condition_item ORDER BY sort_order, condition_item_code
                    """));
            })
            .RequireLogin();
        g.MapPost("/",(StepTemplateSaveRequest r, IDbConnectionFactory db, AuditWriter audit, CancellationToken ct) => SaveAsync(null, r, db, audit, ct))
            .RequirePermission(key, PermissionAction.Create);
        g.MapPut("/{id:long}", (long id, StepTemplateSaveRequest r, IDbConnectionFactory db, AuditWriter audit, CancellationToken ct) => SaveAsync(id, r, db, audit, ct))
            .RequirePermission(key, PermissionAction.Update);
    }

    private const string ListSql =
        """
        SELECT t.step_template_id, t.step_template_code, t.step_template_name, t.unit_process_id, u.unit_process_name,
               t.equipment_type_id, et.equipment_type_name, t.equipment_id, e.equipment_name, t.is_active,
               (SELECT COUNT(*) FROM step_template_item i WHERE i.step_template_id = t.step_template_id) AS step_count,
               (SELECT COUNT(*) FROM step_template_condition c WHERE c.step_template_id = t.step_template_id) AS condition_count,
               (SELECT COUNT(DISTINCT sv.standard_id) FROM standard_version sv WHERE sv.step_template_id = t.step_template_id AND sv.is_current = 1) AS usage_count
          FROM step_template t
          JOIN unit_process u ON u.unit_process_id = t.unit_process_id
          LEFT JOIN equipment_type et ON et.equipment_type_id = t.equipment_type_id
          LEFT JOIN equipment e ON e.equipment_id = t.equipment_id
        """;

    /// <param name="unitProcessId">작업표준 화면: 같은 단위공정의 템플릿만</param>
    private static async Task<IResult> ListAsync(IDbConnectionFactory db, CancellationToken ct, long? unitProcessId = null, bool includeInactive = false)
    {
        await using var conn = await db.OpenAsync(ct);
        return Results.Ok(await conn.QueryAsync<StepTemplateDto>(
            ListSql + """
             WHERE (@includeInactive OR t.is_active = 1) AND (@unitProcessId IS NULL OR t.unit_process_id = @unitProcessId)
             ORDER BY u.sort_order, et.equipment_type_name, t.step_template_name
            """, new { includeInactive, unitProcessId }));
    }

    internal static async Task<StepTemplateDetail?> DetailAsync(MySqlConnection conn, MySqlTransaction? tx, long id)
    {
        var header = await conn.QuerySingleOrDefaultAsync<StepTemplateDto>(ListSql + " WHERE t.step_template_id = @id", new { id }, tx);
        if (header is null) return null;
        var steps = await conn.QueryAsync<StepItemDto>(
            """
            SELECT i.step_template_item_id, i.sequence_no, i.step_name
              FROM step_template_item i WHERE i.step_template_id = @id ORDER BY i.sequence_no
            """, new { id }, tx);
        var conditions = await conn.QueryAsync<TemplateConditionDto>(
            """
            SELECT c.condition_item_id, ci.condition_item_name, ci.unit_code, ci.value_type, c.sequence_no
              FROM step_template_condition c JOIN condition_item ci ON ci.condition_item_id = c.condition_item_id
             WHERE c.step_template_id = @id ORDER BY c.sequence_no
            """, new { id }, tx);
        return new StepTemplateDetail(header, steps.ToList(), conditions.ToList());
    }

    private static async Task<IResult> SaveAsync(long? id, StepTemplateSaveRequest r, IDbConnectionFactory db, AuditWriter audit, CancellationToken ct)
    {
        var code = HeatProcessEndpoints.Required(r.StepTemplateCode, "stepTemplateCode", 50);
        var name = HeatProcessEndpoints.Required(r.StepTemplateName, "stepTemplateName", 100);
        var steps = (r.Steps ?? []).Select(s => s?.Trim() ?? "").ToList();
        if (steps.Any(s => s.Length is 0 or > 100))
            throw new RequestValidationException("steps", "스텝 이름은 1~100자여야 합니다.");
        var conditionIds = (r.ConditionItemIds ?? []).Distinct().ToArray();
        if (steps.Count == 0 && conditionIds.Length == 0)
            throw new RequestValidationException("steps", "스텝이나 관리항목을 1개 이상 넣으세요.");

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM step_template WHERE step_template_code = @code AND step_template_id <> @id",
                new { code, id = id ?? 0 }, tx) > 0)
            throw new RequestValidationException("stepTemplateCode", "이미 사용 중인 템플릿 코드입니다.");
        if (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM unit_process WHERE unit_process_id = @UnitProcessId", r, tx) == 0)
            throw new RequestValidationException("unitProcessId", "단위공정을 선택하세요.");
        if (conditionIds.Length > 0 && await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM condition_item WHERE condition_item_id IN @conditionIds", new { conditionIds }, tx) != conditionIds.Length)
            throw new RequestValidationException("conditionItemIds", "없는 조건 항목이 있습니다.");

        StepTemplateDetail? before = null;
        long templateId;
        var args = new { code, name, r.UnitProcessId, r.EquipmentTypeId, r.EquipmentId, r.IsActive, id = id ?? 0 };
        if (id is null)
        {
            templateId = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO step_template (step_template_code, step_template_name, unit_process_id, equipment_type_id, equipment_id, is_active)
                VALUES (@code, @name, @UnitProcessId, @EquipmentTypeId, @EquipmentId, @IsActive); SELECT LAST_INSERT_ID();
                """, args, tx);
        }
        else
        {
            templateId = id.Value;
            before = await DetailAsync(conn, tx, templateId) ?? throw new NotFoundException("step_template", templateId);
            await conn.ExecuteAsync(
                """
                UPDATE step_template SET step_template_code = @code, step_template_name = @name, unit_process_id = @UnitProcessId,
                       equipment_type_id = @EquipmentTypeId, equipment_id = @EquipmentId, is_active = @IsActive
                 WHERE step_template_id = @id
                """, args, tx);
        }

        // 초기값이라 참조가 없다 → 통째로 다시 쓴다
        await conn.ExecuteAsync("DELETE FROM step_template_item WHERE step_template_id = @templateId", new { templateId }, tx);
        for (var i = 0; i < steps.Count; i++)
            await conn.ExecuteAsync("INSERT INTO step_template_item (step_template_id, sequence_no, step_name) VALUES (@templateId, @seq, @name)",
                new { templateId, seq = i + 1, name = steps[i] }, tx);

        await conn.ExecuteAsync("DELETE FROM step_template_condition WHERE step_template_id = @templateId", new { templateId }, tx);
        for (var i = 0; i < conditionIds.Length; i++)
            await conn.ExecuteAsync("INSERT INTO step_template_condition (step_template_id, sequence_no, condition_item_id) VALUES (@templateId, @seq, @cid)",
                new { templateId, seq = i + 1, cid = conditionIds[i] }, tx);

        var after = await DetailAsync(conn, tx, templateId);
        object Snap(StepTemplateDetail d) => new
        {
            d.Header.StepTemplateCode, d.Header.StepTemplateName, d.Header.UnitProcessId, d.Header.EquipmentTypeId, d.Header.EquipmentId, d.Header.IsActive,
            steps = d.Steps.Select(s => s.StepName), conditions = d.Conditions.Select(c => c.ConditionItemId),
        };
        await audit.WriteAsync(conn, tx, before is null ? AuditAction.Create : AuditAction.Update, "step_template", templateId,
            before is null ? null : Snap(before), Snap(after!));
        await tx.CommitAsync(ct);
        return Results.Ok(new { stepTemplateId = templateId });
    }
}
