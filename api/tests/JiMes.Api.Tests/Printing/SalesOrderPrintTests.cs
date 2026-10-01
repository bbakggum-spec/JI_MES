using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Tests.Printing;

/// <summary>수주 출력 — 공정이동표·제품표시 라벨 (설계 §29.1). 여러 건 = 한 PDF, 화면 권한으로 발행</summary>
[Collection(ApiCollection.Name)]
public sealed class SalesOrderPrintTests(ApiFixture fx)
{
    private static int PageCount(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?![s\w])").Count;

    private async Task<long[]> OrderItemsAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var c = await fx.OpenAsync();
        var customer = await c.ExecuteScalarAsync<long>("INSERT INTO customer (customer_code, customer_name) VALUES (@code, '한독기어'); SELECT LAST_INSERT_ID();", new { code = $"C-{tag}" });
        var part = await c.ExecuteScalarAsync<long>("INSERT INTO part (part_code, part_name, model) VALUES (@code, '헬리컬 기어', 'HD-01'); SELECT LAST_INSERT_ID();", new { code = $"P-{tag}" });
        var order = await c.ExecuteScalarAsync<long>("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@no, CURDATE(), @customer); SELECT LAST_INSERT_ID();",
            new { no = $"SO-{tag}", customer });
        var ids = new List<long>();
        for (var i = 1; i <= 2; i++)
            ids.Add(await c.ExecuteScalarAsync<long>(
                """
                INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, order_qty, order_weight, unit_weight, priority, is_separately_managed)
                VALUES (@no, @order, @i, @part, 400, 120.5, 0.301, 3, 1); SELECT LAST_INSERT_ID();
                """, new { no = $"I-{tag}-{i}", order, i, part }));
        return [.. ids];
    }

    [Theory]
    [InlineData("PROCESS_SHEET")]
    [InlineData("PRODUCT_LABEL")]
    public async Task Two_order_items_print_as_one_pdf(string purpose)
    {
        var client = await fx.LoginAdminAsync();
        var ids = await OrderItemsAsync();

        var choices = await client.GetFromJsonAsync<JsonElement>($"/api/print/choices?purposeCode={purpose}");
        Assert.True(choices[0].GetProperty("isDefault").GetBoolean());

        var res = await client.PostAsJsonAsync("/api/print/documents", new { purposeCode = purpose, sourceIds = ids });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", res.Content.Headers.ContentType?.MediaType);
        var pdf = await res.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(2, PageCount(pdf));

        // 발행 이력은 건마다
        await using var c = await fx.OpenAsync();
        Assert.Equal(2, await c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM print_log WHERE source_table = 'sales_order_item' AND source_id IN @ids", new { ids }));
    }

    [Fact]
    public async Task Issue_follows_the_screen_read_permission()
    {
        var ids = await OrderItemsAsync();
        var login = $"pr_{Guid.NewGuid():N}"[..20];
        await fx.CreateUserAsync(login, "pr-pw-12345", (MenuKeys.SalesOrder, PermissionAction.Read));
        var client = await fx.LoginAsync(login, "pr-pw-12345");
        Assert.True((await client.PostAsJsonAsync("/api/print/documents", new { purposeCode = "PRODUCT_LABEL", sourceIds = ids })).IsSuccessStatusCode);
        // 수주 권한만 있는 사용자는 출하 전표·검사 성적서 발행 불가
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/print/choices?purposeCode=SHIPMENT_SLIP")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/print/documents", new { purposeCode = "SHIPMENT_SLIP", sourceIds = new[] { 1 } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/print/documents", new { purposeCode = "PRODUCT_LABEL", sourceIds = Array.Empty<long>() })).StatusCode);
    }
}
