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

/// <summary>화면 발행 — 여러 건이면 한 파일로 이어서 (공정이동표·라벨 여러 장 등)</summary>
public sealed record IssueManyRequest(string? PurposeCode, IReadOnlyList<long>? SourceIds, long? PrintTemplateId);

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
    PdfConverter pdf, SettingsCache settings, ICurrentUser currentUser, TimeProvider time, PermissionService permissions)
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

    public Task<PrintResult> IssueAsync(IssueRequest request, CancellationToken ct) =>
        IssueManyCoreAsync(request.PurposeCode, [request.SourceId], request.PrintTemplateId, checkMenu: false, ct);

    /// <summary>
    /// 업무 화면 발행 (`POST /api/print/documents`) — 권한 = 데이터 공급원의 화면 읽기 권한 (§12 ⑦).
    /// 여러 건은 한 파일: FIXED 는 페이지를 이어 붙이고, EXCEL 은 시트를 한 통합문서로 모은 뒤 변환한다. 발행 이력은 건마다.
    /// </summary>
    public Task<PrintResult> IssueManyAsync(IssueManyRequest request, CancellationToken ct)
    {
        var ids = request.SourceIds?.Distinct().ToList() ?? [];
        if (string.IsNullOrWhiteSpace(request.PurposeCode)) throw new RequestValidationException("purposeCode", "출력 용도가 없습니다.");
        if (ids.Count == 0) throw new RequestValidationException("sourceIds", "출력할 대상을 고르세요.");
        if (ids.Count > MaxBatch) throw new RequestValidationException("sourceIds", $"한 번에 {MaxBatch}건까지 출력할 수 있습니다.");
        return IssueManyCoreAsync(request.PurposeCode, ids, request.PrintTemplateId, checkMenu: true, ct);
    }

    private const int MaxBatch = 200;

    /// <summary>발행 화면의 양식 선택 — 용도의 데이터 공급원 화면 읽기 권한으로</summary>
    public async Task<IEnumerable<TemplateChoice>> ChoicesForScreenAsync(string purposeCode, CancellationToken ct)
    {
        await using (var conn = await db.OpenAsync(ct))
            await EnsureMenuAsync(ProviderFor((await PurposeAsync(conn, purposeCode)).DataSourceCode), ct);
        return await ChoicesAsync(purposeCode, ct);
    }

    private async Task<PrintResult> IssueManyCoreAsync(string purposeCode, IReadOnlyList<long> sourceIds, long? templateId, bool checkMenu, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var purpose = await PurposeAsync(conn, purposeCode);
        var provider = ProviderFor(purpose.DataSourceCode);
        if (checkMenu) await EnsureMenuAsync(provider, ct);
        var fields = await FieldsAsync(conn, purpose.PrintDataSourceId, provider.AllowLegacyGridKeys);
        var issuedAt = Now();
        var sources = new List<(long Id, PrintSource Source)>();
        foreach (var id in sourceIds)
            sources.Add((id, await provider.LoadAsync(conn, id, fields, issuedAt, ct)));
        // 양식: 지정 양식, 아니면 건마다 고른 양식이 모두 같아야 한 파일로 낼 수 있다
        TemplateVersionRow? template = null;
        foreach (var (_, s) in sources)
        {
            var t = await ResolveTemplateAsync(conn, purpose, s.PartId, s.CustomerId, templateId);
            if (template is not null && t.PrintTemplateVersionId != template.PrintTemplateVersionId)
                throw new BusinessRuleException("TEMPLATE_DIFFERS", "품목마다 연결된 양식이 달라 한 파일로 낼 수 없습니다. 양식을 골라 주세요.");
            template = t;
        }
        var hint = sources.Count == 1 ? sources[0].Source.FileNameHint : $"{sources[0].Source.FileNameHint}_외{sources.Count - 1}건";
        var (content, format) = await RenderAsync(template!, fields, sources.Select(s => s.Source.Data).ToList(), ct);
        return await LogAsync(conn, purpose, provider, template!, sources.Select(s => (s.Id, s.Source.Data)).ToList(), content, format, hint,
            issuedAt, reprintOf: null, ct);
    }

    private async Task EnsureMenuAsync(IPrintDataProvider provider, CancellationToken ct)
    {
        var access = await permissions.GetAsync(currentUser.UserId ?? 0, ct);
        if (!access.Has(provider.MenuKey, PermissionAction.Read))
            throw new ForbiddenException("이 출력물을 발행할 권한이 없습니다.");
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
        var (content, format) = await RenderAsync(template, fields, [data], ct);
        return await LogAsync(conn, purpose, provider, template, [(log.SourceId, data)], content, format, live.FileNameHint,
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

    private async Task<(byte[] Content, string Format)> RenderAsync(
        TemplateVersionRow template, FieldDictionary fields, IReadOnlyList<PrintData> items, CancellationToken ct)
    {
        if (template.TemplateKind == "FIXED")
        {
            var renderer = renderers.FirstOrDefault(r => r.RendererKey == template.RendererKey)
                ?? throw new BusinessRuleException("RENDERER_NOT_FOUND", $"고정 양식 렌더러 '{template.RendererKey}' 가 없습니다.");
            return (renderer.RenderMany(items, template.LayoutOptionsJson), "PDF");
        }
        var books = items.Select(data => ExcelTemplateRenderer.Render(template.FileContent!, data, fields)).ToList();
        var xlsx = books.Count == 1 ? books[0] : ExcelTemplateRenderer.Combine(books);
        var format = template.OutputFormat;
        return (format == "PDF" ? await pdf.ConvertXlsxAsync(xlsx, ct) : xlsx, format);
    }

    /// <summary>발행 이력 — 건마다 1행 (같은 파일이면 해시 같음). 발행본 보관은 1건 발행일 때만</summary>
    private async Task<PrintResult> LogAsync(
        MySqlConnection conn, PurposeRow purpose, IPrintDataProvider provider, TemplateVersionRow template,
        IReadOnlyList<(long SourceId, PrintData Data)> items, byte[] content, string format, string hint, DateTime issuedAt, long? reprintOf,
        CancellationToken ct)
    {
        var fileName = $"{purpose.PurposeName}_{hint}.{format.ToLowerInvariant()}";
        var keep = items.Count == 1 && settings.GetJson<string[]>(SettingKeys.PrintKeepIssuedOutput).Contains(purpose.PurposeCode);
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));

        await using var tx = await conn.BeginTransactionAsync(ct);
        long firstLogId = 0;
        foreach (var (sourceId, data) in items)
        {
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
                    hash, stored = keep ? content : null, snapshot = data.ToSnapshotJson(), reprintOf, issuedAt, userId = currentUser.UserId,
                }, tx);
            if (firstLogId == 0) firstLogId = logId;
            await provider.OnIssuedAsync(conn, tx, sourceId, template.PrintTemplateId, issuedAt);
        }
        await tx.CommitAsync(ct);

        return new PrintResult(firstLogId, fileName,
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
