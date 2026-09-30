using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace JiMes.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture fx)
{
    [Fact]
    public async Task Health_is_anonymous()
    {
        var res = await fx.Factory.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/settings")]
    [InlineData("/api/common-codes")]
    [InlineData("/api/audit-logs")]
    public async Task Protected_endpoints_return_401_without_login(string url)
    {
        var res = await fx.Factory.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_admin_is_created_once_with_audit()
    {
        await using var conn = await fx.OpenAsync();
        Assert.Equal(1, await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM app_user WHERE login_id = @id", new { id = ApiFixture.AdminLoginId }));
        Assert.Equal(1, await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_log WHERE table_name = 'app_user' AND action_type = 'CREATE' AND app_user_id IS NULL"));
    }

    [Fact]
    public async Task Wrong_password_is_rejected_with_code()
    {
        var res = await fx.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { loginId = ApiFixture.AdminLoginId, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_CREDENTIALS", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_login_returns_menu_tree_and_permissions()
    {
        var client = await fx.LoginAdminAsync();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.Equal(ApiFixture.AdminLoginId, me.GetProperty("loginId").GetString());
        Assert.Contains("ADMIN", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        var system = me.GetProperty("menus").EnumerateArray().Single(m => m.GetProperty("menuKey").GetString() == "system");
        Assert.Contains(MenuKeys.SystemSetting,
            system.GetProperty("children").EnumerateArray().Select(c => c.GetProperty("menuKey").GetString()));
        Assert.True(me.GetProperty("permissions").GetProperty(MenuKeys.SystemSetting).GetProperty("update").GetBoolean());
    }

    [Fact]
    public async Task Menu_tree_contains_only_readable_menus_and_their_parents()
    {
        await fx.CreateUserAsync("menu_reader", "menu-reader-pw", (MenuKeys.SystemAudit, PermissionAction.Read));
        var client = await fx.LoginAsync("menu_reader", "menu-reader-pw");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        var top = me.GetProperty("menus").EnumerateArray().ToList();
        Assert.Equal("system", Assert.Single(top).GetProperty("menuKey").GetString());
        var child = Assert.Single(top[0].GetProperty("children").EnumerateArray().ToList());
        Assert.Equal(MenuKeys.SystemAudit, child.GetProperty("menuKey").GetString());
    }

    [Fact]
    public async Task Roles_are_unioned_across_multiple_roles()
    {
        var userId = await fx.CreateUserAsync("multi_role", "multi-role-pw", (MenuKeys.SystemAudit, PermissionAction.Read));
        await using (var conn = await fx.OpenAsync())
        {
            // 두 번째 역할(공통코드 수정)을 추가 — 공용 PC 에서 여러 부서를 가진 사용자 (B8)
            var roleId = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO role (role_code, role_name) VALUES ('T_multi_2', 'T_multi_2'); SELECT LAST_INSERT_ID();");
            await conn.ExecuteAsync(
                """
                INSERT INTO role_menu (role_id, menu_id, can_read, can_update)
                SELECT @roleId, menu_id, 1, 1 FROM menu WHERE menu_key = @key;
                INSERT INTO app_user_role (app_user_id, role_id) VALUES (@userId, @roleId);
                """, new { roleId, userId, key = MenuKeys.SystemCode });
        }

        var access = await fx.Factory.Services.GetRequiredService<PermissionService>().GetAsync(userId);
        Assert.True(access.Has(MenuKeys.SystemAudit, PermissionAction.Read));
        Assert.True(access.Has(MenuKeys.SystemCode, PermissionAction.Read | PermissionAction.Update));
        Assert.False(access.Has(MenuKeys.SystemCode, PermissionAction.Delete));
        Assert.False(access.Has(MenuKeys.SystemSetting, PermissionAction.Read));
    }

    [Fact]
    public async Task Missing_permission_returns_403()
    {
        await fx.CreateUserAsync("no_perm", "no-perm-pw");
        var client = await fx.LoginAsync("no_perm", "no-perm-pw");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/common-codes")).StatusCode);   // 로그인만 필요

        // 웹 동작용 설정은 권한 없이 읽지만, 공개 목록의 키만 내려간다
        var clientSettings = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/client-settings");
        Assert.Equal(SettingKeys.ClientVisible.Order(), clientSettings!.Keys.Order());
    }

    [Fact]
    public async Task Unknown_api_path_is_404_not_spa_fallback()
    {
        var client = await fx.LoginAdminAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/no-such-endpoint")).StatusCode);
    }

    [Fact]
    public async Task Read_only_permission_cannot_update()
    {
        await fx.CreateUserAsync("setting_reader", "setting-reader-pw", (MenuKeys.SystemSetting, PermissionAction.Read));
        var client = await fx.LoginAsync("setting_reader", "setting-reader-pw");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/settings")).StatusCode);
        var res = await client.PutAsJsonAsync("/api/settings/schedule.refresh_interval_sec", new { value = "30" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_loses_existing_session()
    {
        var userId = await fx.CreateUserAsync("to_deactivate", "to-deactivate-pw");
        var client = await fx.LoginAsync("to_deactivate", "to-deactivate-pw");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        await using (var conn = await fx.OpenAsync())
            await conn.ExecuteAsync("UPDATE app_user SET is_active = 0 WHERE app_user_id = @userId", new { userId });
        fx.Factory.Services.GetRequiredService<PermissionService>().InvalidateAll();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        var relogin = await fx.Factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { loginId = "to_deactivate", password = "to-deactivate-pw" });
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.StatusCode);
    }

    [Fact]
    public async Task Change_password_enforces_policy_and_audits()
    {
        var userId = await fx.CreateUserAsync("pw_changer", "pw-changer-old");
        var client = await fx.LoginAsync("pw_changer", "pw-changer-old");

        var tooShort = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "pw-changer-old", newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var wrongCurrent = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "not-the-password", newPassword = "pw-changer-new" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "pw-changer-old", newPassword = "pw-changer-new" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        await fx.LoginAsync("pw_changer", "pw-changer-new");

        await using var conn = await fx.OpenAsync();
        var after = await conn.ExecuteScalarAsync<string>(
            "SELECT after_json FROM audit_log WHERE table_name = 'app_user' AND record_id = @userId AND app_user_id = @userId",
            new { userId });
        Assert.DoesNotContain("hash", after);
    }

    [Fact]
    public async Task Logout_ends_session()
    {
        var client = await fx.LoginAdminAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
