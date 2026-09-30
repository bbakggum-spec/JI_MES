using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;

namespace JiMes.Api.Features.Printing;

public sealed record CreateTemplateRequest(string? PurposeCode, string? PrintTemplateName, string? OutputFormat);

public sealed record FixedOptionsRequest(string? LayoutOptionsJson, string? ChangeNote);

public sealed record VersionResult(long PrintTemplateVersionId, int VersionNo, IReadOnlyList<string> Placeholders, IReadOnlyList<string> Unknown);

/// <summary>
/// 양식 관리 (§5.3.1, §15.2 P3·P5·P10). 수정 = 새 버전 (현재 버전 1개), 파일은 DB 보관, 같은 파일 재등록은 해시로 막는다.
/// </summary>
public sealed class TemplateAdminService(
    IDbConnectionFactory db, SettingsCache settings, AuditWriter audit, ICurrentUser currentUser)
{
    private static readonly string[] ExcelExtensions = [".xlsx", ".xlsm"];

    public async Task<long> CreateAsync(CreateTemplateRequest r, CancellationToken ct)
    {
        var name = r.PrintTemplateName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            throw new RequestValidationException("printTemplateName", "양식 이름은 1~100자여야 합니다.");
        var format = r.OutputFormat ?? "PDF";
        if (format is not ("PDF" or "XLSX"))
            throw new RequestValidationException("outputFormat", "발행 형식은 PDF 또는 XLSX 입니다.");

        await using var conn = await db.OpenAsync(ct);
        var purpose = await PrintService.PurposeAsync(conn, r.PurposeCode ?? "");
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM print_template WHERE print_purpose_id = @PrintPurposeId AND print_template_name = @name",
                new { purpose.PrintPurposeId, name }, tx) > 0)
            throw new BusinessRuleException("DUPLICATE_TEMPLATE_NAME", "같은 용도에 같은 이름의 양식이 있습니다.");

        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO print_template (print_template_name, print_purpose_id, template_kind, output_format, created_by, updated_by)
            VALUES (@name, @PrintPurposeId, 'EXCEL', @format, @userId, @userId);
            SELECT LAST_INSERT_ID();
            """, new { name, purpose.PrintPurposeId, format, userId = currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, "print_template", id, null,
            new { print_template_name = name, purpose_code = purpose.PurposeCode, template_kind = "EXCEL", output_format = format });
        await tx.CommitAsync(ct);
        return id;
    }

    /// <summary>엑셀 양식 파일 등록 → 새 현재 버전. 사전에 없는 치환자는 경고로 돌려주고 저장한다.</summary>
    public async Task<VersionResult> UploadExcelAsync(long templateId, string fileName, byte[] content, string? changeNote, CancellationToken ct)
    {
        var maxBytes = settings.GetInt(SettingKeys.PrintMaxTemplateFileMb) * 1024L * 1024L;
        if (content.Length == 0 || content.Length > maxBytes)
            throw new RequestValidationException("file", $"양식 파일은 {settings.GetInt(SettingKeys.PrintMaxTemplateFileMb)}MB 이하여야 합니다.");
        if (!ExcelExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant()))
            throw new RequestValidationException("file", "엑셀 파일(.xlsx, .xlsm)만 등록할 수 있습니다.");

        IReadOnlyList<PlaceholderUse> uses;
        try
        {
            using var stream = new MemoryStream(content);
            using var workbook = new XLWorkbook(stream);
            uses = Placeholders.Scan(workbook);
        }
        catch (Exception ex) when (ex is not AppException)
        {
            throw new RequestValidationException("file", "엑셀 파일을 열 수 없습니다. 손상되었거나 암호가 걸린 파일인지 확인하세요.");
        }

        await using var conn = await db.OpenAsync(ct);
        var template = await TemplateAsync(conn, templateId);
        if (template.TemplateKind != "EXCEL")
            throw new BusinessRuleException("NOT_EXCEL_TEMPLATE", "고정 양식에는 파일을 등록할 수 없습니다. 레이아웃 옵션을 수정하세요.");

        var fields = await PrintService.FieldsAsync(conn, template.PrintDataSourceId, template.DataSourceCode == "INSPECTION_TARGET");
        var check = Placeholders.Check(uses, fields);
        if (check.Errors.Count > 0)
            throw new BusinessRuleException("TEMPLATE_SYNTAX", string.Join(" ", check.Errors));

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync("SELECT print_template_id FROM print_template WHERE print_template_id = @templateId FOR UPDATE", new { templateId }, tx);
        var duplicate = await conn.ExecuteScalarAsync<int?>(
            "SELECT version_no FROM print_template_version WHERE print_template_id = @templateId AND file_hash = @hash LIMIT 1",
            new { templateId, hash }, tx);
        if (duplicate is not null)
            throw new BusinessRuleException("DUPLICATE_TEMPLATE_FILE", $"같은 파일이 이미 버전 {duplicate} 으로 등록되어 있습니다.");

        var result = await InsertVersionAsync(conn, tx, templateId,
            new { fileName = Path.GetFileName(fileName), content, hash, size = content.Length, options = (string?)null,
                  placeholders = JsonSerializer.Serialize(check.Placeholders), unknown = check.Unknown.Count > 0 ? JsonSerializer.Serialize(check.Unknown) : null },
            changeNote);
        await tx.CommitAsync(ct);
        return result with { Placeholders = check.Placeholders, Unknown = check.Unknown };
    }

    /// <summary>고정 양식 레이아웃 옵션 → 새 현재 버전 (§15.3.1). 알 수 없는 키는 렌더러가 무시한다.</summary>
    public async Task<VersionResult> SaveFixedOptionsAsync(long templateId, FixedOptionsRequest r, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(r.LayoutOptionsJson ?? "");
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new RequestValidationException("layoutOptionsJson", "레이아웃 옵션은 JSON 객체여야 합니다.");
        }
        catch (JsonException)
        {
            throw new RequestValidationException("layoutOptionsJson", "올바른 JSON 이 아닙니다.");
        }

        await using var conn = await db.OpenAsync(ct);
        var template = await TemplateAsync(conn, templateId);
        if (template.TemplateKind != "FIXED")
            throw new BusinessRuleException("NOT_FIXED_TEMPLATE", "엑셀 양식은 파일로 수정합니다.");
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync("SELECT print_template_id FROM print_template WHERE print_template_id = @templateId FOR UPDATE", new { templateId }, tx);
        var result = await InsertVersionAsync(conn, tx, templateId,
            new { fileName = (string?)null, content = (byte[]?)null, hash = (string?)null, size = (int?)null, options = r.LayoutOptionsJson,
                  placeholders = (string?)null, unknown = (string?)null },
            r.ChangeNote);
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>용도 기본 양식 지정 (§5.3.1 양식 선택 3순위) — 용도당 1개</summary>
    public async Task SetDefaultAsync(long templateId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var template = await TemplateAsync(conn, templateId);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var previous = await conn.ExecuteScalarAsync<long?>(
            "SELECT print_template_id FROM print_template WHERE print_purpose_id = @PrintPurposeId AND is_default = 1 FOR UPDATE",
            new { template.PrintPurposeId }, tx);
        await conn.ExecuteAsync("UPDATE print_template SET is_default = 0 WHERE print_purpose_id = @PrintPurposeId AND is_default = 1",
            new { template.PrintPurposeId }, tx);
        await conn.ExecuteAsync("UPDATE print_template SET is_default = 1, updated_by = @userId WHERE print_template_id = @templateId",
            new { templateId, userId = currentUser.UserId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, "print_template", templateId,
            new { default_template_id = previous }, new { default_template_id = templateId }, "용도 기본 양식 지정");
        await tx.CommitAsync(ct);
    }

    /// <summary>용도별 샘플 양식 — 사용할 수 있는 치환자를 모두 넣은 엑셀 (§15.2 P10)</summary>
    public async Task<byte[]> SampleTemplateAsync(string purposeCode, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var purpose = await PrintService.PurposeAsync(conn, purposeCode);
        var fields = await PrintService.FieldsAsync(conn, purpose.PrintDataSourceId, false);

        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("양식 예시");
        ws.Cell(1, 1).Value = $"{purpose.PurposeName} — 샘플 양식 (셀 위치·서식은 자유롭게 바꾸고 치환자만 유지)";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        var row = 3;
        foreach (var f in fields.Fields.Where(f => f.FieldType is not "LIST" && !f.FieldKey.Contains('.')))
        {
            ws.Cell(row, 1).Value = f.FieldAlias ?? f.FieldKey;
            ws.Cell(row, 2).Value = $"{{{{{f.FieldKey}}}}}";
            ws.Cell(row, 3).Value = f.Description ?? "";
            if (f.FieldType == "IMAGE")
            {
                ws.Range(row, 2, row + 3, 3).Merge();
                row += 3;
            }
            row++;
        }
        foreach (var list in fields.Fields.Where(f => f.FieldType == "LIST"))
        {
            row++;
            var items = fields.Fields.Where(f => f.FieldKey.StartsWith(list.FieldKey + ".", StringComparison.Ordinal)).ToList();
            ws.Cell(row, 1).Value = $"{list.FieldAlias ?? list.FieldKey} (반복행)";
            ws.Cell(row, 1).Style.Font.SetBold();
            row++;
            for (var i = 0; i < items.Count; i++)
            {
                var shortKey = items[i].FieldKey[(list.FieldKey.Length + 1)..];
                ws.Cell(row, i + 1).Value = shortKey;
                ws.Cell(row, i + 1).Style.Fill.SetBackgroundColor(XLColor.LightGray);
                var body = $"{{{{{shortKey}}}}}";
                if (i == 0) body = $"{{{{#{list.FieldKey}}}}}" + body;
                if (i == items.Count - 1) body += $"{{{{/{list.FieldKey}}}}}";
                ws.Cell(row + 1, i + 1).Value = body;
            }
            row += 3;
        }
        ws.Columns().AdjustToContents();

        var dict = workbook.AddWorksheet("치환자 목록");
        dict.Cell(1, 1).InsertTable(fields.Fields.Select(f => new
        {
            치환자 = $"{{{{{f.FieldKey}}}}}", 별칭 = f.FieldAlias, 유형 = f.FieldType, 분류 = f.FieldGroup, 형식 = f.FormatPattern, 설명 = f.Description, 예 = f.SampleValue,
        }));
        dict.Columns().AdjustToContents();

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    internal sealed class TemplateRow
    {
        public long PrintTemplateId { get; init; }
        public long PrintPurposeId { get; init; }
        public string TemplateKind { get; init; } = "";
        public long PrintDataSourceId { get; init; }
        public string DataSourceCode { get; init; } = "";
    }

    private static async Task<TemplateRow> TemplateAsync(MySqlConnector.MySqlConnection conn, long templateId) =>
        await conn.QuerySingleOrDefaultAsync<TemplateRow>(
            """
            SELECT t.print_template_id, t.print_purpose_id, t.template_kind, p.print_data_source_id, ds.data_source_code
              FROM print_template t
              JOIN print_purpose p      ON p.print_purpose_id = t.print_purpose_id
              JOIN print_data_source ds ON ds.print_data_source_id = p.print_data_source_id
             WHERE t.print_template_id = @templateId
            """, new { templateId }) ?? throw new NotFoundException("print_template", templateId);

    private async Task<VersionResult> InsertVersionAsync(
        MySqlConnector.MySqlConnection conn, MySqlConnector.MySqlTransaction tx, long templateId, object body, string? changeNote)
    {
        var versionNo = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(version_no), 0) + 1 FROM print_template_version WHERE print_template_id = @templateId", new { templateId }, tx);
        await conn.ExecuteAsync("UPDATE print_template_version SET is_current = 0 WHERE print_template_id = @templateId AND is_current = 1",
            new { templateId }, tx);

        var args = new DynamicParameters(body);
        args.AddDynamicParams(new { templateId, versionNo, changeNote = changeNote?.Trim(), userId = currentUser.UserId });
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO print_template_version (print_template_id, version_no, file_name, file_content, file_hash, file_size,
                                                layout_options_json, placeholders_json, unknown_placeholders_json, is_current, change_note, uploaded_by)
            VALUES (@templateId, @versionNo, @fileName, @content, @hash, @size, @options, @placeholders, @unknown, 1, @changeNote, @userId);
            SELECT LAST_INSERT_ID();
            """, args, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, "print_template_version", id, null,
            new { print_template_id = templateId, version_no = versionNo, change_note = changeNote }, changeNote);
        return new VersionResult(id, versionNo, [], []);
    }
}
