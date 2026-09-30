using Dapper;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.Printing;

public sealed class PurposeDto
{
    public long PrintPurposeId { get; init; }
    public string PurposeCode { get; init; } = "";
    public string PurposeName { get; init; } = "";
    public string DataSourceCode { get; init; } = "";
    public string DataSourceName { get; init; } = "";
    public bool IsSystem { get; init; }
    public bool DataSourceImplemented { get; set; }
    public List<PrintFieldRow> Fields { get; set; } = [];
}

public sealed class TemplateDto
{
    public long PrintTemplateId { get; init; }
    public string PrintTemplateName { get; init; } = "";
    public string PurposeCode { get; init; } = "";
    public string TemplateKind { get; init; } = "";
    public string? RendererKey { get; init; }
    public string OutputFormat { get; init; } = "";
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public long? CurrentVersionId { get; init; }
    public int? CurrentVersionNo { get; init; }
    public DateTime? UploadedAt { get; init; }
    public string? FileName { get; init; }
    public string? UnknownPlaceholdersJson { get; init; }
    public long PartLinkCount { get; init; }
}

public sealed class VersionDto
{
    public long PrintTemplateVersionId { get; init; }
    public int VersionNo { get; init; }
    public string? FileName { get; init; }
    public int? FileSize { get; init; }
    public bool IsCurrent { get; init; }
    public string? ChangeNote { get; init; }
    public DateTime UploadedAt { get; init; }
    public string? UploadedByName { get; init; }
    public string? PlaceholdersJson { get; init; }
    public string? UnknownPlaceholdersJson { get; init; }
    public string? LayoutOptionsJson { get; init; }
}

public sealed class PrintLogDto
{
    public long PrintLogId { get; init; }
    public string PurposeName { get; init; } = "";
    public string PrintTemplateName { get; init; } = "";
    public int VersionNo { get; init; }
    public string SourceTable { get; init; } = "";
    public long SourceId { get; init; }
    public string OutputFormat { get; init; } = "";
    public string? OutputFileName { get; init; }
    public bool HasContent { get; init; }
    public long? ReprintOfId { get; init; }
    public DateTime PrintedAt { get; init; }
    public string? PrintedByName { get; init; }
}

/// <summary>
/// 출력 양식 관리·발행 (§5.3, §15.2~15.3). 권한 = 메뉴 system.print.
/// 발행은 지금은 system.print 읽기 권한 — 6단계 업무 화면(검사·출하)이 생기면 해당 화면 권한으로 옮긴다.
/// </summary>
public static class PrintEndpoints
{
    public static void MapPrintEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/print").WithTags("Print");
        const string key = MenuKeys.SystemPrint;

        g.MapGet("/purposes", PurposesAsync).RequirePermission(key, PermissionAction.Read);
        g.MapGet("/purposes/{code}/sample-template", async (string code, TemplateAdminService s, CancellationToken ct) =>
                Results.File(await s.SampleTemplateAsync(code, ct), XlsxType, $"샘플양식_{code}.xlsx"))
            .RequirePermission(key, PermissionAction.Read);

        g.MapGet("/templates", TemplatesAsync).RequirePermission(key, PermissionAction.Read);
        g.MapPost("/templates", async (CreateTemplateRequest r, TemplateAdminService s, CancellationToken ct) =>
                Results.Ok(new { printTemplateId = await s.CreateAsync(r, ct) }))
            .RequirePermission(key, PermissionAction.Create);
        g.MapGet("/templates/{id:long}/versions", VersionsAsync).RequirePermission(key, PermissionAction.Read);
        g.MapPost("/templates/{id:long}/versions", async (long id, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? changeNote,
                TemplateAdminService s, CancellationToken ct) =>
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                return Results.Ok(await s.UploadExcelAsync(id, file.FileName, buffer.ToArray(), changeNote, ct));
            })
            .RequirePermission(key, PermissionAction.Update)
            // 쿠키가 SameSite=Strict 라 다른 사이트의 폼 전송에는 인증이 실리지 않는다 (CSRF 방지는 쿠키 정책으로)
            .DisableAntiforgery();
        g.MapPost("/templates/{id:long}/options", async (long id, FixedOptionsRequest r, TemplateAdminService s, CancellationToken ct) =>
                Results.Ok(await s.SaveFixedOptionsAsync(id, r, ct)))
            .RequirePermission(key, PermissionAction.Update);
        g.MapPut("/templates/{id:long}/default", async (long id, TemplateAdminService s, CancellationToken ct) =>
            { await s.SetDefaultAsync(id, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapGet("/versions/{id:long}/file", VersionFileAsync).RequirePermission(key, PermissionAction.Read);

        g.MapPost("/issue", async (IssueRequest r, PrintService s, CancellationToken ct) => FileOf(await s.IssueAsync(r, ct)))
            .RequirePermission(key, PermissionAction.Read);
        g.MapPost("/logs/{id:long}/reprint", async (long id, PrintService s, CancellationToken ct) => FileOf(await s.ReprintAsync(id, ct)))
            .RequirePermission(key, PermissionAction.Read);
        g.MapGet("/logs", LogsAsync).RequirePermission(key, PermissionAction.Read);
        g.MapGet("/logs/{id:long}/file", LogFileAsync).RequirePermission(key, PermissionAction.Read);
    }

    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    internal static IResult FileOf(PrintResult r)
    {
        // 발행 이력 id 를 헤더로 알려 화면이 재발행·이력 연결에 쓴다
        return new PrintFileResult(r);
    }

    private sealed class PrintFileResult(PrintResult r) : IResult
    {
        public Task ExecuteAsync(HttpContext http)
        {
            http.Response.Headers["X-Print-Log-Id"] = r.PrintLogId.ToString();
            return Results.File(r.Content, r.ContentType, r.FileName).ExecuteAsync(http);
        }
    }

    private static async Task<IResult> PurposesAsync(IDbConnectionFactory db, PrintService print, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var purposes = (await conn.QueryAsync<PurposeDto>(
            """
            SELECT p.print_purpose_id, p.purpose_code, p.purpose_name, ds.data_source_code, ds.data_source_name, p.is_system
              FROM print_purpose p JOIN print_data_source ds ON ds.print_data_source_id = p.print_data_source_id
             WHERE p.is_active = 1 ORDER BY p.sort_order, p.purpose_code
            """)).ToList();
        foreach (var p in purposes)
        {
            var dsId = await conn.ExecuteScalarAsync<long>("SELECT print_data_source_id FROM print_data_source WHERE data_source_code = @c", new { c = p.DataSourceCode });
            p.Fields = (await PrintService.FieldsAsync(conn, dsId, false)).Fields.ToList();
            try { print.ProviderFor(p.DataSourceCode); p.DataSourceImplemented = true; }
            catch (BusinessRuleException) { p.DataSourceImplemented = false; }
        }
        return Results.Ok(purposes);
    }

    private static async Task<IResult> TemplatesAsync(IDbConnectionFactory db, CancellationToken ct, string? purposeCode = null)
    {
        await using var conn = await db.OpenAsync(ct);
        return Results.Ok(await conn.QueryAsync<TemplateDto>(
            """
            SELECT t.print_template_id, t.print_template_name, p.purpose_code, t.template_kind, t.renderer_key, t.output_format,
                   t.is_default, t.is_active, v.print_template_version_id AS current_version_id, v.version_no AS current_version_no,
                   v.uploaded_at, v.file_name, v.unknown_placeholders_json,
                   (SELECT COUNT(*) FROM part_print_template ppt WHERE ppt.print_template_id = t.print_template_id) AS part_link_count
              FROM print_template t
              JOIN print_purpose p ON p.print_purpose_id = t.print_purpose_id
              LEFT JOIN print_template_version v ON v.print_template_id = t.print_template_id AND v.is_current = 1
             WHERE (@purposeCode IS NULL OR p.purpose_code = @purposeCode)
             ORDER BY p.sort_order, t.is_default DESC, t.print_template_name
            """, new { purposeCode }));
    }

    private static async Task<IResult> VersionsAsync(long id, IDbConnectionFactory db, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return Results.Ok(await conn.QueryAsync<VersionDto>(
            """
            SELECT v.print_template_version_id, v.version_no, v.file_name, v.file_size, v.is_current, v.change_note, v.uploaded_at,
                   u.user_name AS uploaded_by_name, v.placeholders_json, v.unknown_placeholders_json, v.layout_options_json
              FROM print_template_version v LEFT JOIN app_user u ON u.app_user_id = v.uploaded_by
             WHERE v.print_template_id = @id ORDER BY v.version_no DESC
            """, new { id }));
    }

    private static async Task<IResult> VersionFileAsync(long id, IDbConnectionFactory db, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var v = await conn.QuerySingleOrDefaultAsync<(string? FileName, byte[]? Content, string? Options)>(
            "SELECT file_name, file_content, layout_options_json FROM print_template_version WHERE print_template_version_id = @id", new { id });
        if (v.Content is { } content)
            return Results.File(content, XlsxType, v.FileName ?? "template.xlsx");
        if (v.Options is { } options)
            return Results.Text(options, "application/json");
        throw new NotFoundException("print_template_version", id);
    }

    private static async Task<IResult> LogsAsync(IDbConnectionFactory db, CancellationToken ct, string? sourceTable = null, long? sourceId = null)
    {
        await using var conn = await db.OpenAsync(ct);
        return Results.Ok(await conn.QueryAsync<PrintLogDto>(
            """
            SELECT l.print_log_id, p.purpose_name, t.print_template_name, v.version_no, l.source_table, l.source_id, l.output_format,
                   l.output_file_name, l.output_content IS NOT NULL AS has_content, l.reprint_of_id, l.printed_at, u.user_name AS printed_by_name
              FROM print_log l
              JOIN print_purpose p           ON p.print_purpose_id = l.print_purpose_id
              JOIN print_template_version v  ON v.print_template_version_id = l.print_template_version_id
              JOIN print_template t          ON t.print_template_id = v.print_template_id
              LEFT JOIN app_user u           ON u.app_user_id = l.printed_by
             WHERE (@sourceTable IS NULL OR l.source_table = @sourceTable) AND (@sourceId IS NULL OR l.source_id = @sourceId)
             ORDER BY l.print_log_id DESC LIMIT 200
            """, new { sourceTable, sourceId }));
    }

    private static async Task<IResult> LogFileAsync(long id, IDbConnectionFactory db, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var log = await conn.QuerySingleOrDefaultAsync<(string? FileName, string Format, byte[]? Content)>(
            "SELECT output_file_name, output_format, output_content FROM print_log WHERE print_log_id = @id", new { id });
        if (log.Content is null)
            throw new BusinessRuleException("OUTPUT_NOT_KEPT", "발행본을 보관하지 않는 용도입니다. 재발행하세요.");
        return Results.File(log.Content, log.Format == "PDF" ? "application/pdf" : XlsxType, log.FileName);
    }
}
