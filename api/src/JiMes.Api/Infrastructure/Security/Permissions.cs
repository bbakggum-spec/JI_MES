using Dapper;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace JiMes.Api.Infrastructure.Security;

/// <summary>API 권한 키 = menu.menu_key. DDL 초기 데이터(§9.8)에 있어야 한다 (테스트로 확인).</summary>
public static class MenuKeys
{
    public const string SalesOrder = "sales.order";
    public const string ProductionSchedule = "production.schedule";
    public const string ProductionWork = "production.work";
    public const string MasterPart = "master.part";
    public const string MasterHeatProcess = "master.heat_process";
    public const string MasterStepTemplate = "master.step_template";
    public const string MasterStandard = "master.standard";
    public const string MasterInspectionStandard = "master.inspection_standard";

    public const string SystemUser = "system.user";
    public const string SystemRole = "system.role";
    public const string SystemSetting = "system.setting";
    public const string SystemCode = "system.code";
    public const string SystemAudit = "system.audit";
    public const string SystemPrint = "system.print";
}

/// <summary>role_menu 의 can_read / can_create / can_update / can_delete.</summary>
[Flags]
public enum PermissionAction
{
    None = 0,
    Read = 1,
    Create = 2,
    Update = 4,
    Delete = 8,
}

/// <summary>사용자의 유효 권한 = 활성 역할들의 메뉴 권한 합집합 (설계 §17 권한, B8).</summary>
public sealed record UserAccess(
    long UserId,
    bool IsActive,
    IReadOnlyList<string> RoleCodes,
    IReadOnlyDictionary<string, PermissionAction> Menus)
{
    public static UserAccess Missing(long userId) => new(userId, false, [], new Dictionary<string, PermissionAction>());

    public bool Has(string menuKey, PermissionAction action) =>
        IsActive && Menus.TryGetValue(menuKey, out var granted) && (granted & action) == action;
}

public sealed class PermissionService(IDbConnectionFactory db, IMemoryCache cache, SettingsCache settings)
{
    private sealed class MenuGrantRow
    {
        public string MenuKey { get; init; } = "";
        public long CanRead { get; init; }
        public long CanCreate { get; init; }
        public long CanUpdate { get; init; }
        public long CanDelete { get; init; }
    }

    private CancellationTokenSource _reset = new();

    public async Task<UserAccess> GetAsync(long userId, CancellationToken ct = default)
    {
        var key = (nameof(PermissionService), userId);
        if (cache.TryGetValue(key, out UserAccess? cached) && cached is not null)
            return cached;

        var access = await LoadAsync(userId, ct);
        var seconds = settings.GetInt(SettingKeys.AuthPermissionCacheSec);
        if (seconds > 0)
        {
            cache.Set(key, access, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromSeconds(seconds))
                .AddExpirationToken(new CancellationChangeToken(_reset.Token)));
        }
        return access;
    }

    /// <summary>역할·메뉴 권한·사용자 활성 여부를 바꾼 API 가 호출한다.</summary>
    public void InvalidateAll() => Interlocked.Exchange(ref _reset, new CancellationTokenSource()).Cancel();

    private async Task<UserAccess> LoadAsync(long userId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var isActive = await conn.ExecuteScalarAsync<bool?>(
            "SELECT is_active FROM app_user WHERE app_user_id = @userId", new { userId });
        if (isActive is null)
            return UserAccess.Missing(userId);

        var roles = await conn.QueryAsync<string>(
            """
            SELECT r.role_code
              FROM app_user_role ur
              JOIN role r ON r.role_id = ur.role_id AND r.is_active = 1
             WHERE ur.app_user_id = @userId
             ORDER BY r.role_code
            """, new { userId });

        var grants = await conn.QueryAsync<MenuGrantRow>(
            """
            SELECT m.menu_key,
                   CAST(MAX(rm.can_read)   AS SIGNED) AS can_read,
                   CAST(MAX(rm.can_create) AS SIGNED) AS can_create,
                   CAST(MAX(rm.can_update) AS SIGNED) AS can_update,
                   CAST(MAX(rm.can_delete) AS SIGNED) AS can_delete
              FROM app_user_role ur
              JOIN role r       ON r.role_id = ur.role_id AND r.is_active = 1
              JOIN role_menu rm ON rm.role_id = r.role_id
              JOIN menu m       ON m.menu_id = rm.menu_id AND m.is_active = 1
             WHERE ur.app_user_id = @userId
             GROUP BY m.menu_key
            """, new { userId });

        var menus = grants.ToDictionary(
            g => g.MenuKey,
            g => (g.CanRead > 0 ? PermissionAction.Read : 0)
               | (g.CanCreate > 0 ? PermissionAction.Create : 0)
               | (g.CanUpdate > 0 ? PermissionAction.Update : 0)
               | (g.CanDelete > 0 ? PermissionAction.Delete : 0));

        return new UserAccess(userId, isActive.Value, roles.ToList(), menus);
    }
}
