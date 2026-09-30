using System.Net.Http.Json;
using Dapper;
using JiMes.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace JiMes.Api.Tests;

/// <summary>
/// 개발 DB 인스턴스(3307)에 테스트 전용 DB 를 DDL 단일 원본으로 새로 만들고 API 를 띄운다.
/// 개발용 bbakggum_v2 와 운영 DB 는 건드리지 않는다.
/// 접속 서버는 환경변수 JIMES_TEST_SERVER 로 바꿀 수 있다.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string DatabaseName = "bbakggum_v2_test";
    public const string AdminLoginId = "admin";
    public const string AdminPassword = "test-admin-pw";

    internal static readonly string ServerConnection =
        Environment.GetEnvironmentVariable("JIMES_TEST_SERVER")
        ?? "Server=127.0.0.1;Port=3307;User ID=root;Password=;SslMode=None";

    public string ConnectionString { get; } = $"{ServerConnection};Database={DatabaseName}";

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await RecreateDatabaseAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Main", ConnectionString);
            b.UseSetting("Bootstrap:AdminLoginId", AdminLoginId);
            b.UseSetting("Bootstrap:AdminPassword", AdminPassword);
            b.UseSetting("Scheduling:AutoRecalculate", "false");   // 테스트는 재계산 시점을 직접 통제
        });
        _ = Factory.Server;   // 시작 (캐시 적재 + 최초 관리자 생성)
    }

    public async Task DisposeAsync() => await Factory.DisposeAsync();

    public async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    public async Task<HttpClient> LoginAsync(string loginId, string password)
    {
        var client = Factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { loginId, password });
        res.EnsureSuccessStatusCode();
        return client;
    }

    public Task<HttpClient> LoginAdminAsync() => LoginAsync(AdminLoginId, AdminPassword);

    /// <summary>전용 역할(T_{loginId})과 함께 사용자를 만든다. 권한은 (menuKey, 동작) 목록.</summary>
    public async Task<long> CreateUserAsync(string loginId, string password, params (string MenuKey, PermissionAction Action)[] grants)
    {
        var hash = Factory.Services.GetRequiredService<PasswordService>().Hash(password);
        await using var conn = await OpenAsync();
        var roleId = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO role (role_code, role_name) VALUES (@code, @code); SELECT LAST_INSERT_ID();",
            new { code = $"T_{loginId}" });
        var userId = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO app_user (login_id, password_hash, user_name) VALUES (@loginId, @hash, @loginId); SELECT LAST_INSERT_ID();",
            new { loginId, hash });
        await conn.ExecuteAsync("INSERT INTO app_user_role (app_user_id, role_id) VALUES (@userId, @roleId)", new { userId, roleId });
        foreach (var (menuKey, action) in grants)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO role_menu (role_id, menu_id, can_read, can_create, can_update, can_delete)
                SELECT @roleId, menu_id, @r, @c, @u, @d FROM menu WHERE menu_key = @menuKey
                """,
                new
                {
                    roleId, menuKey,
                    r = action.HasFlag(PermissionAction.Read), c = action.HasFlag(PermissionAction.Create),
                    u = action.HasFlag(PermissionAction.Update), d = action.HasFlag(PermissionAction.Delete),
                });
        }
        return userId;
    }

    private async Task RecreateDatabaseAsync()
    {
        var ddl = await File.ReadAllTextAsync(FindRepoFile("db", "bbakggum_v2_DDL_V3.sql"));
        ddl = ddl.Replace("`bbakggum_v2`", $"`{DatabaseName}`", StringComparison.Ordinal);

        await using var conn = new MySqlConnection($"{ServerConnection};AllowUserVariables=true");
        try { await conn.OpenAsync(); }
        catch (MySqlException ex)
        {
            throw new InvalidOperationException(
                "테스트 DB 서버에 접속할 수 없습니다. 'db\\dev\\dev-db.ps1 start' 로 개발 DB 를 먼저 시작하세요.", ex);
        }
        await conn.ExecuteAsync($"DROP DATABASE IF EXISTS `{DatabaseName}`");
        await conn.ExecuteAsync(ddl, commandTimeout: 120);

        // 테스트는 같은 IP 로 로그인을 반복하므로 시도 제한을 넉넉히
        await conn.ExecuteAsync(
            $"UPDATE `{DatabaseName}`.system_setting SET setting_value = '1000' WHERE setting_key = 'auth.login_max_attempts_per_min'");
    }

    internal static string FindRepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException($"저장소에서 {Path.Combine(parts)} 를 찾지 못했습니다.");
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
