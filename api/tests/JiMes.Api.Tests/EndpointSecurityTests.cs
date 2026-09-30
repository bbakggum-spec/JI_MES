using System.Reflection;
using Dapper;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace JiMes.Api.Tests;

/// <summary>fail-closed 확인: 권한 선언 누락, 코드 상수와 DDL 초기 데이터 불일치를 잡는다.</summary>
[Collection(ApiCollection.Name)]
public sealed class EndpointSecurityTests(ApiFixture fx)
{
    [Fact]
    public void Every_api_and_hub_endpoint_declares_its_access()
    {
        var endpoints = fx.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } p && (p.StartsWith("/api") || p.StartsWith("/hubs")))
            .ToList();
        Assert.NotEmpty(endpoints);

        var undeclared = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null
                     && e.Metadata.GetMetadata<PermissionMetadata>() is null
                     && e.Metadata.GetMetadata<LoginOnlyMetadata>() is null)
            .Select(e => e.DisplayName)
            .ToList();
        Assert.Empty(undeclared);
    }

    [Fact]
    public async Task Menu_keys_used_in_code_exist_in_menu_table()
    {
        await using var conn = await fx.OpenAsync();
        var keys = (await conn.QueryAsync<string>("SELECT menu_key FROM menu")).ToHashSet();
        Assert.All(ConstantsOf(typeof(MenuKeys)), k => Assert.Contains(k, keys));
    }

    [Fact]
    public async Task Setting_keys_used_in_code_exist_in_system_setting()
    {
        await using var conn = await fx.OpenAsync();
        var keys = (await conn.QueryAsync<string>("SELECT setting_key FROM system_setting")).ToHashSet();
        Assert.All(ConstantsOf(typeof(SettingKeys)), k => Assert.Contains(k, keys));
        Assert.All(SettingKeys.ClientVisible, k => Assert.Contains(k, keys));
    }

    [Fact]
    public async Task Admin_role_has_full_permission_on_every_menu()
    {
        await using var conn = await fx.OpenAsync();
        var missing = await conn.ExecuteScalarAsync<long>(
            """
            SELECT COUNT(*) FROM menu m
             WHERE NOT EXISTS (SELECT 1 FROM role_menu rm JOIN role r ON r.role_id = rm.role_id
                                WHERE r.role_code = 'ADMIN' AND rm.menu_id = m.menu_id
                                  AND rm.can_read = 1 AND rm.can_create = 1 AND rm.can_update = 1 AND rm.can_delete = 1)
            """);
        Assert.Equal(0, missing);
    }

    private static IEnumerable<string> ConstantsOf(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);
}
