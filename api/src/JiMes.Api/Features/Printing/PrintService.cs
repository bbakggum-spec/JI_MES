using System.Security.Cryptography;
using Dapper;
using JiMes.Api.Features.Printing.Fixed;
using JiMes.Api.Features.Printing.Providers;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Printing;

public sealed record IssueRequest(string PurposeCode, long SourceId, long? PrintTemplateId);

public sealed record PrintResult(long PrintLogId, string FileName, string ContentType, byte[] Content);

/// <summary>발행 화면의 양식 선택 콤보 항목 (사용 중 + 현재 버전 있는 양식만)</summary>
public sealed class TemplateChoice
{
    public long PrintTemplateId { get; init; }
    public string PrintTemplateName { get; init; } = "";
    public string TemplateKind { get; init; } = "";
    public bool IsDefault { get; init; }
}

/// <summary>
/// 출력 발행의 유일한 경로 (§15.2 P1 — 구 네 갈래 엔진을 EXCEL / FIXED 두 방식으로).
/// 양식 선택 → 데이터 공급원 → 렌더링(EXCEL 채우기 → PDF, FIXED 렌더러) → print_log(버전·치환값 Snapshot·발행본) → 원본 반영.
/// </summary>
public sealed class PrintService(
    IDbConnectionFactory db, IEnumerable<IPrintDataProvider> providers, IEnumerable<IFixedRenderer> renderers,
    PdfConverter pdf, SettingsCache settings, ICurrentUser currentUser, TimeProvider time)
{
    internal sealed class PurposeRow
    {
        public long PrintPurposeId { get; init; }
        public string PurposeCode { get; init; } = "";
        public string PurposeName { get; init; } = "";
        public long PrintDataSourceId { get; init; }
        public string DataSourceCode { get; init; } = "";
    }

    internal sealed class TemplateVersionRow
    {
        public long PrintTemplateId { get; init; }
        public long PrintTemplateVersionId { get; init; }
        public long PrintPurposeId { get; init; }
        public string TemplateKind { get; init; } = "";
        public string? RendererKey { get; init; }
        public string OutputFormat { get; init; } = "PDF";
        public byte[]? FileContent { get; init; }
        public string? LayoutOptionsJson { get; init; }
    }

    private const string VersionColumns =
        """
        t.print_template_id, v.print_template_version_id, t.print_purpose_id, t.template_kind, t.renderer_key, t.output_format,
        v.file_content, v.layout_options_json
        """;

    /// <summary>용도의 발행 가능 양식 (기본 양식 먼저) — 발행 화면에서 기본 외 양식(예: 업체 전용 엑셀 양식)으로 바꿔 출력</summary>
    public async Task<IEnumerable<TemplateChoice>> ChoicesAsync(string purposeCode, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.QueryAsync<TemplateChoice>(
            """
            SELECT t.print_template_id, t.print_template_name, t.template_kind, t.is_default
              FROM print_template t
              JOIN print_purpose p ON p.print_purpose_id = t.print_purpose_id
             WHERE p.purpose_code = @purposeCode AND t.is_active = 1
               AND EXISTS (SELECT 1 FROM print_template_version v WHERE v.print_template_id = t.print_template_id AND v.is_current = 1)
             ORDER BY t.is_default DESC, t.print_template_name
            """, new { purposeCode });
    }

    public async Task<PrintResult> IssueAsync(IssueRequest request, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var purpose = await PurposeAsync(conn, request.PurposeCode);
        var provider = ProviderFor(purpose.DataSourceCode);
        var fields = await FieldsAsync(conn, purpose.PrintDataSourceId, provider.AllowLegacyGridKeys);
        var issuedAt = Now();
        var source = await provider.LoadAsync(conn, request.SourceId, fields, issuedAt, ct);
        var template = await ResolveTemplateAsync(conn, purpose, source.PartId, source.CustomerId, request.PrintTemplateId);
        return await RenderAndLogAsync(conn, purpose, provider, template, fields, source.Data, request.SourceId, source.FileNameHint,
            issuedAt, reprintOf: null, ct);
    }

    /// <summary>재발행 — 당시 양식 버전 + 당시 치환값으로 같은 내용 (§15.2 P8). 이미지는 원본 첨부를 다시 읽는다.</summary>
    public async Task<PrintResult> ReprintAsync(long printLogId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var log = await conn.QuerySingleOrDefaultAsync<(long VersionId, string PurposeCode, long SourceId, string? Snapshot, string? FileName)>(
            """
            SELECT CAST(l.print_template_version_id AS SIGNED), p.purpose_code, CAST(l.source_id AS SIGNED), l.data_snapshot_json, l.output_file_name
              FROM print_log l JOIN print_purpose p ON p.print_purpose_id = l.print_purpose_id
             WHERE l.print_log_id = @printLogId
            """, new { printLogId });
        if (log.PurposeCode is null)
            throw new NotFoundException("print_log", printLogId);
        if (log.Snapshot is null)
            throw new BusinessRuleException("SNAPSHOT_MISSING", "발행 당시 값이 없어 같은 내용으로 재발행할 수 없습니다.");

        var purpose = await PurposeAsync(conn, log.PurposeCode);
        var provider = ProviderFor(purpose.DataSourceCode);
        var fields = await FieldsAsync(conn, purpose.PrintDataSourceId, provider.AllowLegacyGridKeys);
        var issuedAt = Now();
        var live = await provider.LoadAsync(conn, log.SourceId, fields, issuedAt, ct);
        var data = PrintData.FromSnapshotJson(log.Snapshot);
        foreach (var (key, image) in live.Data.Images)
            data.Images[key] = image;

        var template = await conn.QuerySingleAsync<TemplateVersionRow>(
            $"""
            SELECT {VersionColumns} FROM print_template_version v JOIN print_template t ON t.print_template_id = v.print_template_id
             WHERE v.print_template_version_id = @VersionId
            """, new { log.VersionId });
        return await RenderAndLogAsync(conn, purpose, provider, template, fields, data, log.SourceId, live.FileNameHint,
            issuedAt, reprintOf: printLogId, ct);
    }

    /// <summary>
    /// 양식 선택 (§5.3.1, §15.2 P9 — 단일 조회 함수): 지정 양식 → 품목+업체 기본 → 품목 공통 기본 → 용도 기본.
    /// </summary>
    internal static async Task<TemplateVersionRow> ResolveTemplateAsync(
        MySqlConnection conn, PurposeRow purpose, long? partId, long? customerId, long? explicitTemplateId)
    {
        long? templateId = explicitTemplateId;
        if (templateId is null && partId is not null)
        {
            templateId = await conn.ExecuteScalarAsync<long?>(
                """
                SELECT ppt.print_template_id FROM part_print_template ppt
                  JOIN print_template t ON t.print_template_id = ppt.print_template_id AND t.is_active = 1
                 WHERE ppt.part_id = @partId AND ppt.print_purpose_id = @PrintPurposeId AND ppt.is_default = 1
                   AND (ppt.customer_id = @customerId OR ppt.customer_id IS NULL)
                 ORDER BY ppt.customer_id IS NULL
                 LIMIT 1
                """, new { partId, customerId, purpose.PrintPurposeId });
        }
        templateId ??= await conn.ExecuteScalarAsync<long?>(
            "SELECT print_template_id FROM print_template WHERE print_purpose_id = @PrintPurposeId AND is_default = 1 AND is_active = 1",
            new { purpose.PrintPurposeId });
        if (templateId is null)
            throw new BusinessRuleException("TEMPLATE_NOT_FOUND",
                $"'{purpose.PurposeName}' 에 쓸 양식이 없습니다. 품목별 양식 또는 용도 기본 양식을 지정하세요.");

        var row = await conn.QuerySingleOrDefaultAsync<TemplateVersionRow>(
            $"""
            SELECT {VersionColumns} FROM print_template t
              JOIN print_template_version v ON v.print_template_id = t.print_template_id AND v.is_current = 1
             WHERE t.print_template_id = @templateId AND t.is_active = 1
            """, new { templateId }) ?? throw new BusinessRuleException("TEMPLATE_VERSION_MISSING", "양식에 현재 버전이 없습니다. 양식 파일을 등록하세요.");
        if (row.PrintPurposeId != purpose.PrintPurposeId)
            throw new BusinessRuleException("TEMPLATE_PURPOSE_MISMATCH", "다른 용도의 양식입니다.");
        return row;
    }

    private async Task<PrintResult> RenderAndLogAsync(
        MySqlConnection conn, PurposeRow purpose, IPrintDataProvider provider, TemplateVersionRow template, FieldDictionary fields,
        PrintData data, long sourceId, string hint, DateTime issuedAt, long? reprintOf, CancellationToken ct)
    {
        byte[] content;
        string format;
        if (template.TemplateKind == "FIXED")
        {
            var renderer = renderers.FirstOrDefault(r => r.RendererKey == template.RendererKey)
                ?? throw new BusinessRuleException("RENDERER_NOT_FOUND", $"고정 양식 렌더러 '{template.RendererKey}' 가 없습니다.");
            content = renderer.Render(data, template.LayoutOptionsJson);
            format = "PDF";
        }
        else
        {
            var xlsx = ExcelTemplateRenderer.Render(template.FileContent!, data, fields);
            format = template.OutputFormat;
            content = format == "PDF" ? await pdf.ConvertXlsxAsync(xlsx, ct) : xlsx;
        }

        var fileName = $"{purpose.PurposeName}_{hint}.{format.ToLowerInvariant()}";
        var keep = settings.GetJson<string[]>(SettingKeys.PrintKeepIssuedOutput).Contains(purpose.PurposeCode);

        await using var tx = await conn.BeginTransactionAsync(ct);
        var logId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO print_log (print_template_version_id, print_purpose_id, source_table, source_id, output_format, output_file_name,
                                   output_file_hash, output_content, data_snapshot_json, reprint_of_id, printed_at, printed_by)
            VALUES (@PrintTemplateVersionId, @PrintPurposeId, @sourceTable, @sourceId, @format, @fileName,
                    @hash, @stored, @snapshot, @reprintOf, @issuedAt, @userId);
            SELECT LAST_INSERT_ID();
            """,
            new
            {
                template.PrintTemplateVersionId, purpose.PrintPurposeId, sourceTable = provider.SourceTable, sourceId, format, fileName,
                hash = Convert.ToHexStringLower(SHA256.HashData(content)), stored = keep ? content : null,
                snapshot = data.ToSnapshotJson(), reprintOf, issuedAt, userId = currentUser.UserId,
            }, tx);
        await provider.OnIssuedAsync(conn, tx, sourceId, template.PrintTemplateId, issuedAt);
        await tx.CommitAsync(ct);

        return new PrintResult(logId, fileName,
            format == "PDF" ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", content);
    }

    internal static async Task<PurposeRow> PurposeAsync(MySqlConnection conn, string purposeCode) =>
        await conn.QuerySingleOrDefaultAsync<PurposeRow>(
            """
            SELECT p.print_purpose_id, p.purpose_code, p.purpose_name, p.print_data_source_id, ds.data_source_code
              FROM print_purpose p JOIN print_data_source ds ON ds.print_data_source_id = p.print_data_source_id
             WHERE p.purpose_code = @purposeCode AND p.is_active = 1
            """, new { purposeCode })
        ?? throw new BusinessRuleException("PURPOSE_NOT_FOUND", $"출력 용도 '{purposeCode}' 가 없습니다.");

    internal static async Task<FieldDictionary> FieldsAsync(MySqlConnection conn, long dataSourceId, bool allowLegacy) =>
        new((await conn.QueryAsync<PrintFieldRow>(
            """
            SELECT field_key, field_alias, field_type, field_group, format_pattern, description, sample_value
              FROM print_field WHERE print_data_source_id = @dataSourceId AND is_active = 1 ORDER BY sort_order, field_key
            """, new { dataSourceId })), allowLegacy);

    internal IPrintDataProvider ProviderFor(string dataSourceCode) =>
        providers.FirstOrDefault(p => p.DataSourceCode == dataSourceCode)
        ?? throw new BusinessRuleException("DATA_SOURCE_NOT_IMPLEMENTED", $"데이터 공급원 '{dataSourceCode}' 는 아직 개발되지 않았습니다.");

    private DateTime Now() => time.GetLocalNow().DateTime;
}
