using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace JiMes.Api.Tests.Master;

[Collection(ApiCollection.Name)]
public sealed class UserRoleTests(ApiFixture fx) : IAsyncLifetime
{
    private HttpClient _admin = null!;
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    public async Task InitializeAsync() => _admin = await fx.LoginAdminAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<long> CreateRoleAsync(string code)
    {
        var res = await _admin.PostAsJsonAsync("/api/roles", new { roleCode = code, roleName = code });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roleId").GetInt64();
    }

    private async Task<long> MenuIdAsync(string key)
    {
        await using var c = await fx.OpenAsync();
        return await c.ExecuteScalarAsync<long>("SELECT menu_id FROM menu WHERE menu_key = @key", new { key });
    }

    [Fact]
    public async Task New_user_with_role_gets_menu_permissions_immediately()
    {
        var roleId = await CreateRoleAsync($"R_{Tag()}".ToUpperInvariant());
        var login = $"u_{Tag()}";
        var create = await _admin.PostAsJsonAsync("/api/users", new { loginId = login, userName = "신규", password = "new-user-pw", roleIds = new[] { roleId } });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var user = await fx.LoginAsync(login, "new-user-pw");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/master/customer")).StatusCode);

        var grants = new[] { new { menuId = await MenuIdAsync("master.customer"), read = true, create = false, update = false, delete = false } };
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PutAsJsonAsync($"/api/roles/{roleId}/menus", new { grants })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/master/customer")).StatusCode);   // 캐시 무효화 → 바로 반영

        var menus = await _admin.GetFromJsonAsync<JsonElement>($"/api/roles/{roleId}/menus");
        var customer = menus.EnumerateArray().Single(m => m.GetProperty("menuKey").GetString() == "master.customer");
        Assert.True(customer.GetProperty("read").GetBoolean());
        Assert.False(customer.GetProperty("update").GetBoolean());
    }

    [Fact]
    public async Task Duplicate_login_and_weak_password_are_rejected()
    {
        var weak = await _admin.PostAsJsonAsync("/api/users", new { loginId = $"w_{Tag()}", userName = "약함", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var dup = await _admin.PostAsJsonAsync("/api/users", new { loginId = ApiFixture.AdminLoginId, userName = "중복", password = "long-enough-pw" });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_lock_self_out_and_admin_role_is_locked()
    {
        await using var c = await fx.OpenAsync();
        var adminId = await c.ExecuteScalarAsync<long>("SELECT app_user_id FROM app_user WHERE login_id = @id", new { id = ApiFixture.AdminLoginId });
        var adminRole = await c.ExecuteScalarAsync<long>("SELECT role_id FROM role WHERE role_code = 'ADMIN'");

        var deactivateSelf = await _admin.PutAsJsonAsync($"/api/users/{adminId}", new { userName = "관리자", isActive = false, roleIds = new[] { adminRole } });
        Assert.Equal("SELF_LOCKOUT", (await deactivateSelf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var dropRole = await _admin.PutAsJsonAsync($"/api/users/{adminId}", new { userName = "관리자", isActive = true, roleIds = Array.Empty<long>() });
        Assert.Equal("SELF_LOCKOUT", (await dropRole.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var lockedMenus = await _admin.PutAsJsonAsync($"/api/roles/{adminRole}/menus", new { grants = Array.Empty<object>() });
        Assert.Equal("ADMIN_ROLE_LOCKED", (await lockedMenus.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var roles = await _admin.GetFromJsonAsync<JsonElement>("/api/roles");
        Assert.True(roles.EnumerateArray().Single(r => r.GetProperty("roleCode").GetString() == "ADMIN").GetProperty("isLocked").GetBoolean());
    }

    [Fact]
    public async Task Reset_password_and_deactivate_other_user()
    {
        var login = $"x_{Tag()}";
        var userId = await fx.CreateUserAsync(login, "old-password-1");
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsJsonAsync($"/api/users/{userId}/reset-password", new { newPassword = "new-password-1" })).StatusCode);
        var user = await fx.LoginAsync(login, "new-password-1");

        var off = await _admin.PutAsJsonAsync($"/api/users/{userId}", new { userName = login, isActive = false, roleIds = await RolesOfAsync(userId) });
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/auth/me")).StatusCode);   // 기존 세션도 끊김
    }

    private async Task<long[]> RolesOfAsync(long userId)
    {
        await using var c = await fx.OpenAsync();
        return (await c.QueryAsync<long>("SELECT CAST(role_id AS SIGNED) FROM app_user_role WHERE app_user_id = @userId", new { userId })).ToArray();
    }
}
