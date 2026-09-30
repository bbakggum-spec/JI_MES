using Dapper;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.Master.Parts;

/// <summary>품목 API (설계 §22.4) — 권한 master.part</summary>
public static class PartEndpoints
{
    public static void MapPartEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/parts").WithTags("Parts");
        const string key = MenuKeys.MasterPart;

        g.MapGet("/", async (PartService s, CancellationToken ct, string? search, long? customerId, bool includeInactive = false, int page = 1, int pageSize = 50) =>
            {
                var (items, total) = await s.ListAsync(search, customerId, includeInactive, page, pageSize, ct);
                return Results.Ok(new { items, total });
            })
            .RequirePermission(key, PermissionAction.Read);
        g.MapGet("/lookups", LookupsAsync).RequirePermission(key, PermissionAction.Read);
        // 다른 화면(작업표준·검사기준·수주) 선택 목록 — id·표시명만
        g.MapGet("/options", async (IDbConnectionFactory db, CancellationToken ct, string? search = null) =>
            {
                await using var conn = await db.OpenAsync(ct);
                return Results.Ok(await conn.QueryAsync<LookupOption>(
                    """
                    SELECT part_id AS value, CONCAT(part_name, ' (', part_code, IF(part_number IS NULL, '', CONCAT(' / ', part_number)), ')') AS label,
                           is_active AS active, NULL AS `group`
                      FROM part
                     WHERE @search IS NULL OR part_code LIKE @like OR part_name LIKE @like OR part_number LIKE @like
                     ORDER BY part_name LIMIT 200
                    """, new { search = string.IsNullOrWhiteSpace(search) ? null : search, like = $"%{search?.Trim()}%" }));
            })
            .RequireLogin();
        g.MapGet("/{id:long}", async (long id, PartService s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)))
            .RequirePermission(key, PermissionAction.Read);
        g.MapPost("/", async (PartSaveRequest r, PartService s, CancellationToken ct) => Results.Ok(new { partId = await s.SaveAsync(null, r, ct) }))
            .RequirePermission(key, PermissionAction.Create);
        g.MapPut("/{id:long}", async (long id, PartSaveRequest r, PartService s, CancellationToken ct) =>
            { await s.SaveAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapPut("/{id:long}/print-templates", async (long id, PartPrintTemplateInput[] links, PartService s, CancellationToken ct) =>
            { await s.SavePrintTemplatesAsync(id, links, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapGet("/{id:long}/history", async (long id, PartService s, CancellationToken ct) => Results.Ok(await s.HistoryAsync(id, ct)))
            .RequirePermission(key, PermissionAction.Read);

        g.MapPost("/{id:long}/attachments", async (long id, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string kind,
                [Microsoft.AspNetCore.Mvc.FromForm] string? caption, PartService s, CancellationToken ct) =>
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                return Results.Ok(new { attachmentId = await s.AddAttachmentAsync(id, kind, file.FileName, file.ContentType, buffer.ToArray(), caption, ct) });
            })
            .RequirePermission(key, PermissionAction.Update)
            .DisableAntiforgery();   // SameSite=Strict 쿠키로 CSRF 방지
        g.MapGet("/{id:long}/attachments/{attachmentId:long}", async (long id, long attachmentId, PartService s, CancellationToken ct) =>
            {
                var (content, fileName, contentType) = await s.GetAttachmentAsync(id, attachmentId, ct);
                return Results.File(content, contentType, fileName, enableRangeProcessing: true);
            })
            .RequirePermission(key, PermissionAction.Read);
        g.MapDelete("/{id:long}/attachments/{attachmentId:long}", async (long id, long attachmentId, PartService s, CancellationToken ct) =>
            { await s.DeleteAttachmentAsync(id, attachmentId, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
    }

    /// <summary>품목 편집 화면 선택 목록 — 거래처, 공정(열처리), 품목에 연결할 수 있는 EXCEL 양식</summary>
    private static async Task<IResult> LookupsAsync(IDbConnectionFactory db, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var customers = await conn.QueryAsync<LookupOption>(
            "SELECT customer_id AS value, customer_name AS label, is_active AS active, NULL AS `group` FROM customer ORDER BY customer_name");
        var heatProcesses = await conn.QueryAsync<LookupOption>(
            "SELECT heat_process_id AS value, heat_process_name AS label, is_active AS active, NULL AS `group` FROM heat_process ORDER BY heat_process_name");
        var templates = await conn.QueryAsync<LookupOption>(
            """
            SELECT t.print_template_id AS value, t.print_template_name AS label, t.is_active AS active, p.purpose_name AS `group`
              FROM print_template t JOIN print_purpose p ON p.print_purpose_id = t.print_purpose_id
              JOIN print_data_source ds ON ds.print_data_source_id = p.print_data_source_id
             WHERE t.template_kind = 'EXCEL' AND ds.data_source_code = 'INSPECTION_TARGET'
             ORDER BY p.sort_order, t.print_template_name
            """);
        return Results.Ok(new { customers, heatProcesses, templates });
    }
}
