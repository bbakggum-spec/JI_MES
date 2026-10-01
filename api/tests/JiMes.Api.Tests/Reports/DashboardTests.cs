using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Tests.Reports;

/// <summary>대시보드 KPI — 패널 권한, 추이 계열 권한, 오늘 입고·출하·부적합 반영 (설계 §24.3)</summary>
[Collection(ApiCollection.Name)]
public sealed class DashboardTests(ApiFixture fx)
{
    [Fact]
    public async Task Admin_sees_all_panels_and_today_is_counted()
    {
        var client = await fx.LoginAdminAsync();
        var before = await TodayAsync(client);

        var tag = Guid.NewGuid().ToString("N")[..8];
        await using (var c = await fx.OpenAsync())
        {
            var customer = await c.ExecuteScalarAsync<long>("INSERT INTO customer (customer_code, customer_name) VALUES (@code, 'x'); SELECT LAST_INSERT_ID();", new { code = $"C-{tag}" });
            var part = await c.ExecuteScalarAsync<long>("INSERT INTO part (part_code, part_name) VALUES (@code, 'p'); SELECT LAST_INSERT_ID();", new { code = $"P-{tag}" });
            var order = await c.ExecuteScalarAsync<long>("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@no, CURDATE(), @customer); SELECT LAST_INSERT_ID();",
                new { no = $"SO-{tag}", customer });
            await c.ExecuteAsync(
                "INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, order_qty, unit_price) VALUES (@no, @order, 1, @part, 10, 1000)",
                new { no = $"I-{tag}", order, part });
        }
        var after = await TodayAsync(client);
        Assert.Equal(before.IntakeAmount + 10000m, after.IntakeAmount);
        Assert.Equal(before.IntakeCount + 1, after.IntakeCount);

        var sales = await client.GetFromJsonAsync<JsonElement>("/api/dashboard/sales");
        Assert.True(sales.GetProperty("intake").GetProperty("todayAmount").GetDecimal() >= 10000m);
        Assert.NotEqual(JsonValueKind.Null, sales.GetProperty("shipment").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard/equipment")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard/quality")).StatusCode);
        Assert.Equal(7, (await client.GetFromJsonAsync<JsonElement>("/api/dashboard/trend?days=7")).GetArrayLength());
    }

    [Fact]
    public async Task Panels_and_trend_series_follow_permissions()
    {
        var login = $"dash_{Guid.NewGuid():N}"[..20];
        await fx.CreateUserAsync(login, "dash-pw-123", (MenuKeys.ProductionWork, PermissionAction.Read));
        var client = await fx.LoginAsync(login, "dash-pw-123");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard/equipment")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard/quality")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard/sales")).StatusCode);
        var trend = await client.GetFromJsonAsync<JsonElement>("/api/dashboard/trend?days=3");
        Assert.All(trend.EnumerateArray(), p =>
        {
            Assert.Equal(JsonValueKind.Null, p.GetProperty("intakeAmount").ValueKind);   // 금액 계열은 권한 없으면 비움
            Assert.Equal(JsonValueKind.Null, p.GetProperty("defectCount").ValueKind);
        });
    }

    private static async Task<(decimal IntakeAmount, long IntakeCount)> TodayAsync(HttpClient client)
    {
        var trend = await client.GetFromJsonAsync<JsonElement>("/api/dashboard/trend?days=1");
        var today = trend[0];
        return (today.GetProperty("intakeAmount").GetDecimal(), today.GetProperty("intakeCount").GetInt64());
    }
}
