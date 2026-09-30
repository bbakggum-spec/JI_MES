using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Realtime;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;

namespace JiMes.Api.Features.Settings;

public sealed record SettingDto(
    string SettingKey, string Category, string SettingName, string ValueType,
    string? SettingValue, string DefaultValue, string EffectiveValue, bool IsFallback,
    decimal? MinValue, decimal? MaxValue, string? UnitLabel, string? Description,
    bool IsEditable, bool RequiresRestart, int SortOrder, DateTime UpdatedAt, long? UpdatedBy);

public sealed record UpdateSettingRequest(string? Value, string? Reason);
public sealed record ResetSettingRequest(string? Reason);

/// <summary>관리자 설정 화면 API (설계 §15.4). 저장 → audit_log → 캐시 갱신 → SignalR 알림.</summary>
public static class SettingEndpoints
{
    public static void MapSettingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");

        group.MapGet("/", (SettingsCache cache) => Results.Ok(cache.All
                .OrderBy(s => s.Row.Category).ThenBy(s => s.Row.SortOrder).ThenBy(s => s.Row.SettingKey)
                .Select(ToDto)))
            .RequirePermission(MenuKeys.SystemSetting, PermissionAction.Read);

        group.MapPut("/{key}", (string key, UpdateSettingRequest request, SettingChangeService service, CancellationToken ct) =>
                service.ChangeAsync(key, request.Value ?? "", request.Reason, ct))
            .RequirePermission(MenuKeys.SystemSetting, PermissionAction.Update);

        group.MapPost("/{key}/reset", (string key, ResetSettingRequest? request, SettingChangeService service, CancellationToken ct) =>
                service.ChangeAsync(key, null, request?.Reason, ct))
            .RequirePermission(MenuKeys.SystemSetting, PermissionAction.Update);
    }

    internal static SettingDto ToDto(CachedSetting s) => new(
        s.Row.SettingKey, s.Row.Category, s.Row.SettingName, s.Row.ValueType,
        s.Row.SettingValue, s.Row.DefaultValue, s.EffectiveValue, s.IsFallback,
        s.Row.MinValue, s.Row.MaxValue, s.Row.UnitLabel, s.Row.Description,
        s.Row.IsEditable, s.Row.RequiresRestart, s.Row.SortOrder, s.Row.UpdatedAt, s.Row.UpdatedBy);
}

public sealed class SettingChangeService(
    IDbConnectionFactory db, SettingsCache cache, AuditWriter audit, EventPublisher events, ICurrentUser currentUser)
{
    /// <param name="value">null 이면 기본값으로 되돌림 (setting_value = NULL).</param>
    public async Task<IResult> ChangeAsync(string key, string? value, string? reason, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<SystemSettingRow>(
            SettingsCache.SelectSql + " WHERE setting_key = @key FOR UPDATE", new { key }, tx)
            ?? throw new BusinessRuleException("SETTING_NOT_FOUND", $"설정 '{key}' 가 없습니다.");
        if (!row.IsEditable)
            throw new BusinessRuleException("SETTING_NOT_EDITABLE", $"설정 '{row.SettingName}' 은 수정할 수 없습니다.");

        string? normalized = null;
        if (value is not null)
        {
            normalized = SettingValue.Normalize(row.ValueType, value, row.MinValue, row.MaxValue, out var error);
            if (normalized is null)
                throw new RequestValidationException("value", error!);
        }

        await conn.ExecuteAsync(
            "UPDATE system_setting SET setting_value = @normalized, updated_by = @userId WHERE system_setting_id = @id",
            new { normalized, userId = currentUser.UserId, id = row.SystemSettingId }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, "system_setting", row.SystemSettingId,
            new { setting_key = key, setting_value = row.SettingValue },
            new { setting_key = key, setting_value = normalized },
            reason);
        await tx.CommitAsync(ct);

        await cache.ReloadAsync(ct);
        await events.PublishAsync(RealtimeEvents.SettingChanged, new { key }, ct);
        return Results.Ok(SettingEndpoints.ToDto(cache.Get(key)));
    }
}
