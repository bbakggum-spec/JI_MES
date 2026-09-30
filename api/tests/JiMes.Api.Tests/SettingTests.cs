using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Realtime;
using JiMes.Api.Infrastructure.Settings;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace JiMes.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class SettingTests(ApiFixture fx)
{
    private const string Key = "schedule.refresh_interval_sec";   // INT, 10~3600

    private SettingsCache Cache => fx.Factory.Services.GetRequiredService<SettingsCache>();

    [Fact]
    public async Task List_returns_effective_values()
    {
        var client = await fx.LoginAdminAsync();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/settings");
        var vat = list.EnumerateArray().Single(s => s.GetProperty("settingKey").GetString() == "sales.vat_rate");
        Assert.Equal("0.10", vat.GetProperty("effectiveValue").GetString());
        Assert.Equal("DECIMAL", vat.GetProperty("valueType").GetString());
    }

    [Fact]
    public async Task Out_of_range_value_is_rejected()
    {
        var client = await fx.LoginAdminAsync();
        var res = await client.PutAsJsonAsync($"/api/settings/{Key}", new { value = "5" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Update_saves_audits_refreshes_cache_and_notifies_then_reset_restores_default()
    {
        var client = await fx.LoginAdminAsync();
        await using var hub = await ConnectHubAsync();
        var notified = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>(RealtimeEvents.SettingChanged, p => notified.TrySetResult(p.GetProperty("key").GetString()!));

        var res = await client.PutAsJsonAsync($"/api/settings/{Key}", new { value = " 30 ", reason = "테스트" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(30, Cache.GetInt(Key));
        Assert.Equal(Key, await notified.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        await using (var conn = await fx.OpenAsync())
        {
            var audit = await conn.QuerySingleAsync<(string Before, string After, string Reason)>(
                """
                SELECT a.before_json, a.after_json, a.reason FROM audit_log a
                  JOIN system_setting s ON s.system_setting_id = a.record_id
                 WHERE a.table_name = 'system_setting' AND s.setting_key = @Key
                 ORDER BY a.audit_log_id DESC LIMIT 1
                """, new { Key });
            Assert.Contains("\"setting_value\":null", audit.Before);
            Assert.Contains("\"setting_value\":\"30\"", audit.After);
            Assert.Equal("테스트", audit.Reason);
        }

        var reset = await client.PostAsJsonAsync($"/api/settings/{Key}/reset", new { });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(60, Cache.GetInt(Key));
    }

    [Fact]
    public async Task Invalid_stored_value_falls_back_to_default()
    {
        const string key = "downtime.default_duration_min";   // INT 1~1440, 기본 60
        await using (var conn = await fx.OpenAsync())
            await conn.ExecuteAsync("UPDATE system_setting SET setting_value = 'abc' WHERE setting_key = @key", new { key });
        try
        {
            await Cache.ReloadAsync();
            Assert.Equal(60, Cache.GetInt(key));
            Assert.True(Cache.Get(key).IsFallback);
        }
        finally
        {
            await using var conn = await fx.OpenAsync();
            await conn.ExecuteAsync("UPDATE system_setting SET setting_value = NULL WHERE setting_key = @key", new { key });
            await Cache.ReloadAsync();
        }
    }

    [Fact]
    public async Task Hub_requires_login()
    {
        var server = fx.Factory.Server;
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, EventsHub.Route.TrimStart('/')), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => hub.StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    /// <summary>관리자 쿠키로 허브에 접속 (TestServer 는 WebSocket 대신 LongPolling).</summary>
    private async Task<HubConnection> ConnectHubAsync()
    {
        var raw = fx.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await raw.PostAsJsonAsync("/api/auth/login",
            new { loginId = ApiFixture.AdminLoginId, password = ApiFixture.AdminPassword });
        login.EnsureSuccessStatusCode();
        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var server = fx.Factory.Server;
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, EventsHub.Route.TrimStart('/')), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.Headers["Cookie"] = cookie;
            })
            .Build();
        await hub.StartAsync();
        return hub;
    }
}
