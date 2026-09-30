using System.Globalization;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Master;

public sealed record MasterPage(IReadOnlyList<IDictionary<string, object?>> Items, long Total);

public sealed record MasterOption(long Value, string Label, bool Active);

/// <summary>
/// 단순 기준정보 공통 처리 — 정의(MasterCatalog)에서 SQL 을 만든다. 식별자는 정의의 상수만 쓰고(시작 시 검증) 값은 모두 파라미터.
/// 등록·수정·삭제는 audit_log 에 남긴다. is_active 가 있는 테이블은 삭제 대신 사용 중지.
/// </summary>
public sealed class MasterService(
    IDbConnectionFactory db, CommonCodeCache codes, SettingsCache settings, AuditWriter audit, ICurrentUser currentUser)
{
    // 한 번에 돌려주는 최대 행 수 (서버 보호용 기술 상한)
    private const int MaxPageSize = 500;

    public async Task<MasterPage> ListAsync(MasterEntity e, string? search, bool includeInactive, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var where = new List<string>();
        if (e.HasActive && !includeInactive) where.Add("t.is_active = 1");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchable = e.Fields.Where(f => f.Searchable).Select(f => $"t.{f.Name} LIKE @like").ToList();
            if (searchable.Count > 0) where.Add("(" + string.Join(" OR ", searchable) + ")");
        }
        var whereSql = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";
        var args = new { like = $"%{search?.Trim()}%", offset = (page - 1) * pageSize, pageSize };

        await using var conn = await db.OpenAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {e.Table} t{whereSql}", args);
        var rows = await conn.QueryAsync($"{SelectSql(e)}{whereSql} ORDER BY {Qualified(e.OrderBy)} LIMIT @pageSize OFFSET @offset", args);
        return new MasterPage(rows.Select(r => Normalize((IDictionary<string, object?>)r)).ToList(), total);
    }

    public async Task<IDictionary<string, object?>> GetAsync(MasterEntity e, long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await LoadAsync(conn, null, e, id) ?? throw new NotFoundException(e.Table, id);
    }

    public async Task<IReadOnlyList<MasterOption>> OptionsAsync(MasterEntity e, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var active = e.HasActive ? "t.is_active" : "1";
        var rows = await conn.QueryAsync<(long Value, string? Label, long Active)>(
            $"SELECT CAST(t.{e.PrimaryKey} AS SIGNED), CAST(t.{e.DisplayField} AS CHAR), CAST({active} AS SIGNED) FROM {e.Table} t ORDER BY {Qualified(e.OrderBy)}");
        return rows.Select(r => new MasterOption(r.Value, r.Label ?? r.Value.ToString(CultureInfo.InvariantCulture), r.Active != 0)).ToList();
    }

    public async Task<long> CreateAsync(MasterEntity e, Dictionary<string, JsonElement> body, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var values = await BindAsync(conn, e, body, id: null);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var columns = values.Keys.ToList();
        var args = new DynamicParameters(values);
        if (e.HasCreatedBy) { columns.Add("created_by"); args.Add("created_by", currentUser.UserId); }
        if (e.HasUpdatedBy) { columns.Add("updated_by"); args.Add("updated_by", currentUser.UserId); }

        var id = await Execute(() => conn.ExecuteScalarAsync<long>(
            $"INSERT INTO {e.Table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select(c => "@" + c))}); SELECT LAST_INSERT_ID();",
            args, tx));
        await audit.WriteAsync(conn, tx, AuditAction.Create, e.Table, id, null, await LoadAsync(conn, tx, e, id));
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task UpdateAsync(MasterEntity e, long id, Dictionary<string, JsonElement> body, string? reason, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var values = await BindAsync(conn, e, body, id);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LoadAsync(conn, tx, e, id, forUpdate: true) ?? throw new NotFoundException(e.Table, id);

        var sets = values.Keys.Select(c => $"{c} = @{c}").ToList();
        var args = new DynamicParameters(values);
        args.Add("__id", id);
        if (e.HasUpdatedBy) { sets.Add("updated_by = @updated_by"); args.Add("updated_by", currentUser.UserId); }
        await Execute(() => conn.ExecuteAsync($"UPDATE {e.Table} SET {string.Join(", ", sets)} WHERE {e.PrimaryKey} = @__id", args, tx));

        var after = await LoadAsync(conn, tx, e, id);
        var action = e.HasActive && !Equals(before["is_active"], after!["is_active"]) ? AuditAction.StatusChange : AuditAction.Update;
        await audit.WriteAsync(conn, tx, action, e.Table, id, before, after, reason);
        await tx.CommitAsync(ct);
    }

    /// <summary>사용 중지가 있는 테이블은 삭제하지 않는다 (참조·과거 데이터 보존). 참조 중이면 422 IN_USE.</summary>
    public async Task DeleteAsync(MasterEntity e, long id, string? reason, CancellationToken ct)
    {
        if (!e.AllowDelete)
            throw new BusinessRuleException("DELETE_NOT_ALLOWED", $"{e.Label} 은 삭제하지 않고 '사용' 을 해제합니다.");
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LoadAsync(conn, tx, e, id, forUpdate: true) ?? throw new NotFoundException(e.Table, id);
        await Execute(() => conn.ExecuteAsync($"DELETE FROM {e.Table} WHERE {e.PrimaryKey} = @id", new { id }, tx));
        await audit.WriteAsync(conn, tx, AuditAction.Delete, e.Table, id, before, null, reason);
        await tx.CommitAsync(ct);
    }

    public async Task<(byte[] Content, string FileName)?> GetImageAsync(MasterEntity e, long id, string imageName, CancellationToken ct)
    {
        var image = ImageOf(e, imageName);
        await using var conn = await db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(byte[]? Content, string? FileName)>(
            $"SELECT {image.Name}, {(image.FileNameColumn ?? "NULL")} FROM {e.Table} WHERE {e.PrimaryKey} = @id", new { id });
        return row.Content is { Length: > 0 } content ? (content, row.FileName ?? $"{image.Name}.png") : null;
    }

    /// <param name="content">null 이면 이미지 삭제</param>
    public async Task SetImageAsync(MasterEntity e, long id, string imageName, byte[]? content, string? fileName, CancellationToken ct)
    {
        var image = ImageOf(e, imageName);
        if (content is not null)
        {
            var maxBytes = settings.GetInt(SettingKeys.PrintMaxTemplateFileMb) * 1024L * 1024L;
            if (content.Length == 0 || content.Length > maxBytes)
                throw new RequestValidationException("file", $"이미지는 {settings.GetInt(SettingKeys.PrintMaxTemplateFileMb)}MB 이하여야 합니다.");
            if (!LooksLikeImage(content))
                throw new RequestValidationException("file", "PNG·JPG 이미지만 등록할 수 있습니다.");
        }

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        _ = await LoadAsync(conn, tx, e, id, forUpdate: true) ?? throw new NotFoundException(e.Table, id);
        var fileSet = image.FileNameColumn is null ? "" : $", {image.FileNameColumn} = @fileName";
        await conn.ExecuteAsync($"UPDATE {e.Table} SET {image.Name} = @content{fileSet} WHERE {e.PrimaryKey} = @id",
            new { content, fileName = content is null ? null : Path.GetFileName(fileName), id }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, e.Table, id, null,
            new Dictionary<string, object?> { [image.Name] = content is null ? "(삭제)" : $"(이미지 {content.Length} bytes)", ["file_name"] = fileName });
        await tx.CommitAsync(ct);
    }

    // ───────────────────────── 내부 ─────────────────────────

    private static string SelectSql(MasterEntity e)
    {
        var columns = new List<string> { $"t.{e.PrimaryKey} AS id" };
        columns.AddRange(e.Fields.Select(f => $"t.{f.Name}"));
        foreach (var f in e.Fields.Where(f => f.Type == MasterFieldType.Lookup))
        {
            var target = MasterCatalog.Get(f.Lookup!);
            columns.Add($"(SELECT CAST(x.{target.DisplayField} AS CHAR) FROM {target.Table} x WHERE x.{target.PrimaryKey} = t.{f.Name}) AS {f.Name}__label");
        }
        columns.AddRange(e.Images.Select(i => $"(t.{i.Name} IS NOT NULL) AS has_{i.Name}"));
        return $"SELECT {string.Join(", ", columns)} FROM {e.Table} t";
    }

    private static string Qualified(string orderBy) =>
        string.Join(", ", orderBy.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(o => "t." + o));

    private static async Task<IDictionary<string, object?>?> LoadAsync(
        MySqlConnection conn, MySqlTransaction? tx, MasterEntity e, long id, bool forUpdate = false)
    {
        var row = await conn.QuerySingleOrDefaultAsync(
            $"{SelectSql(e)} WHERE t.{e.PrimaryKey} = @id{(forUpdate ? " FOR UPDATE" : "")}", new { id }, tx);
        return row is null ? null : Normalize((IDictionary<string, object?>)row);
    }

    /// <summary>JSON 직렬화에 맞게 값 정리 (TIME → HH:mm, DATE → yyyy-MM-dd, TINYINT(1) → bool, 부호 없는 id → long)</summary>
    private static IDictionary<string, object?> Normalize(IDictionary<string, object?> row)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in row)
        {
            result[key] = value switch
            {
                TimeSpan t => t.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ulong u => (long)u,
                long l when key.StartsWith("has_", StringComparison.Ordinal) => l != 0,
                int i when key.StartsWith("has_", StringComparison.Ordinal) => i != 0,
                _ => value,
            };
        }
        return result;
    }

    /// <summary>요청 값 → 컬럼 값. 형식·필수·길이·범위·공통코드·참조·중복을 검증한다 (필드별 오류 모음 → 400).</summary>
    private async Task<Dictionary<string, object?>> BindAsync(MySqlConnection conn, MasterEntity e, Dictionary<string, JsonElement> body, long? id)
    {
        var errors = new Dictionary<string, string[]>();
        var values = new Dictionary<string, object?>();
        void Error(MasterField f, string message) => errors[f.Name] = [$"{f.Label}: {message}"];

        foreach (var f in e.Fields)
        {
            object? value;
            if (!body.TryGetValue(f.Name, out var json) || json.ValueKind is JsonValueKind.Undefined)
            {
                if (id is not null) continue;   // 수정: 보내지 않은 필드는 그대로
                value = f.Default;
            }
            else if (!TryConvert(f, json, out value, out var message))
            {
                Error(f, message!);
                continue;
            }

            if (value is null && f.Required) { Error(f, "필수입니다."); continue; }
            if (value is string s && f.MaxLength is { } max && s.Length > max) { Error(f, $"{max}자 이하여야 합니다."); continue; }
            if (value is long or decimal && (f.Min, f.Max) is var (min, maxValue))
            {
                var d = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (min is { } lo && d < lo) { Error(f, $"{lo} 이상이어야 합니다."); continue; }
                if (maxValue is { } hi && d > hi) { Error(f, $"{hi} 이하여야 합니다."); continue; }
            }
            if (value is string code && f.Type == MasterFieldType.Code
                && codes.GetGroup(f.CodeGroup!).Codes.All(c => c.Code != code || !c.IsActive))
            { Error(f, "허용되지 않는 값입니다."); continue; }
            if (value is long refId && f.Type == MasterFieldType.Lookup)
            {
                var target = MasterCatalog.Get(f.Lookup!);
                if (await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {target.Table} WHERE {target.PrimaryKey} = @refId", new { refId }) == 0)
                { Error(f, "없는 항목입니다."); continue; }
            }
            if (f.Unique && value is not null && await conn.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM {e.Table} WHERE {f.Name} = @value AND {e.PrimaryKey} <> @id", new { value, id = id ?? 0 }) > 0)
            { Error(f, "이미 사용 중입니다."); continue; }
            values[f.Name] = value;
        }

        if (errors.Count == 0 && e.RequireOneOf is { } oneOf && id is null && oneOf.All(n => values.GetValueOrDefault(n) is null))
            errors[oneOf[0]] = [$"{string.Join(" 또는 ", oneOf.Select(n => e.Fields.First(f => f.Name == n).Label))} 중 하나는 필수입니다."];
        if (errors.Count > 0)
            throw new RequestValidationException(errors);
        return values;
    }

    private static bool TryConvert(MasterField f, JsonElement json, out object? value, out string? error)
    {
        value = null;
        error = null;
        if (json.ValueKind == JsonValueKind.Null)
            return true;
        if (json.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(json.GetString()) && f.Type != MasterFieldType.Bool)
            return true;   // 빈 입력 = 값 없음
        try
        {
            switch (f.Type)
            {
                case MasterFieldType.Text or MasterFieldType.TextArea or MasterFieldType.Code:
                    value = json.GetString()!.Trim();
                    return true;
                case MasterFieldType.Integer or MasterFieldType.Lookup:
                    value = json.ValueKind == JsonValueKind.Number ? json.GetInt64() : long.Parse(json.GetString()!, CultureInfo.InvariantCulture);
                    return true;
                case MasterFieldType.Decimal:
                    value = json.ValueKind == JsonValueKind.Number ? json.GetDecimal() : decimal.Parse(json.GetString()!, CultureInfo.InvariantCulture);
                    return true;
                case MasterFieldType.Bool:
                    value = json.GetBoolean();
                    return true;
                case MasterFieldType.Date:
                    value = DateOnly.ParseExact(json.GetString()!.Trim()[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
                    return true;
                case MasterFieldType.Time:
                    value = TimeOnly.ParseExact(json.GetString()!.Trim()[..5], "HH:mm", CultureInfo.InvariantCulture).ToTimeSpan();
                    return true;
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException or ArgumentOutOfRangeException)
        {
        }
        error = f.Type switch
        {
            MasterFieldType.Date => "날짜(yyyy-MM-dd) 형식이 아닙니다.",
            MasterFieldType.Time => "시각(HH:mm) 형식이 아닙니다.",
            MasterFieldType.Bool => "예/아니오 값이 아닙니다.",
            _ => "형식이 올바르지 않습니다.",
        };
        return false;
    }

    /// <summary>DB 오류 중 사용자에게 설명할 수 있는 것 (참조 중 삭제, 중복)</summary>
    private static async Task<T> Execute<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (MySqlException ex) when (ex.ErrorCode is MySqlErrorCode.RowIsReferenced2 or MySqlErrorCode.RowIsReferenced)
        {
            throw new BusinessRuleException("IN_USE", "다른 자료에서 사용 중이라 삭제할 수 없습니다.");
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new BusinessRuleException("DUPLICATE", "이미 같은 값이 등록되어 있습니다.");
        }
        catch (MySqlException ex) when ((int)ex.ErrorCode is 4025 or 3819)   // MariaDB ER_CONSTRAINT_FAILED / MySQL ER_CHECK_CONSTRAINT_VIOLATED
        {
            throw new BusinessRuleException("CHECK_VIOLATION", "입력값이 규칙에 맞지 않습니다.");
        }
    }

    private static MasterImageField ImageOf(MasterEntity e, string name) =>
        e.Images.FirstOrDefault(i => i.Name == name) ?? throw new NotFoundException($"{e.Table}.{name}", 0);

    private static bool LooksLikeImage(byte[] b) =>
        (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)   // PNG
        || (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF);             // JPEG
}
