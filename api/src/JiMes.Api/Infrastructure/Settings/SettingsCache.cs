using System.Globalization;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Data;

namespace JiMes.Api.Infrastructure.Settings;

public sealed class SystemSettingRow
{
    public long SystemSettingId { get; init; }
    public string SettingKey { get; init; } = "";
    public string Category { get; init; } = "";
    public string SettingName { get; init; } = "";
    public string ValueType { get; init; } = "";
    public string? SettingValue { get; init; }
    public string DefaultValue { get; init; } = "";
    public decimal? MinValue { get; init; }
    public decimal? MaxValue { get; init; }
    public string? UnitLabel { get; init; }
    public string? Description { get; init; }
    public bool IsEditable { get; init; }
    public bool RequiresRestart { get; init; }
    public int SortOrder { get; init; }
    public DateTime UpdatedAt { get; init; }
    public long? UpdatedBy { get; init; }
}

public sealed record CachedSetting(SystemSettingRow Row, string EffectiveValue, bool IsFallback);

/// <summary>
/// system_setting 전체를 메모리에 둔다 (설계 §15.4). 앱 시작 시 적재, 변경 API 가 저장 후 <see cref="ReloadAsync"/>.
/// 저장값이 형식·범위를 벗어나면 default_value 로 동작하고 경고를 남긴다.
/// </summary>
public sealed class SettingsCache(IDbConnectionFactory db, ILogger<SettingsCache> logger)
{
    public const string SelectSql =
        """
        SELECT system_setting_id, setting_key, category, setting_name, value_type, setting_value, default_value,
               min_value, max_value, unit_label, description, is_editable, requires_restart, sort_order,
               updated_at, updated_by
          FROM system_setting
        """;

    private volatile IReadOnlyDictionary<string, CachedSetting>? _items;

    public IReadOnlyCollection<CachedSetting> All => Items.Values.ToList();

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<SystemSettingRow>(SelectSql + " ORDER BY category, sort_order, setting_key");
        _items = rows.ToDictionary(r => r.SettingKey, Resolve, StringComparer.Ordinal);
    }

    public CachedSetting Get(string key) =>
        Items.TryGetValue(key, out var s) ? s : throw new KeyNotFoundException($"system_setting 에 '{key}' 가 없습니다. DDL 초기 데이터를 확인하세요.");

    public string GetString(string key) => Get(key).EffectiveValue;
    public int GetInt(string key) => int.Parse(GetString(key), CultureInfo.InvariantCulture);
    public decimal GetDecimal(string key) => decimal.Parse(GetString(key), CultureInfo.InvariantCulture);
    public bool GetBool(string key) => GetString(key) == "true";
    public TimeOnly GetTime(string key) => TimeOnly.Parse(GetString(key), CultureInfo.InvariantCulture);
    public T GetJson<T>(string key) => JsonSerializer.Deserialize<T>(GetString(key))
        ?? throw new InvalidOperationException($"설정 '{key}' JSON 이 null 입니다.");

    private IReadOnlyDictionary<string, CachedSetting> Items =>
        _items ?? throw new InvalidOperationException("설정 캐시가 아직 적재되지 않았습니다.");

    private CachedSetting Resolve(SystemSettingRow row)
    {
        if (row.SettingValue is not null)
        {
            var value = SettingValue.Normalize(row.ValueType, row.SettingValue, row.MinValue, row.MaxValue, out var error);
            if (value is not null)
                return new CachedSetting(row, value, IsFallback: false);
            logger.LogWarning("설정 {Key} 값 '{Value}' 이 올바르지 않아 기본값으로 동작합니다: {Error}",
                row.SettingKey, row.SettingValue, error);
        }

        var fallback = SettingValue.Normalize(row.ValueType, row.DefaultValue, row.MinValue, row.MaxValue, out var defaultError);
        if (fallback is null)
        {
            logger.LogError("설정 {Key} 기본값 '{Value}' 도 올바르지 않습니다: {Error}", row.SettingKey, row.DefaultValue, defaultError);
            fallback = row.DefaultValue;
        }
        return new CachedSetting(row, fallback, IsFallback: row.SettingValue is not null);
    }
}
