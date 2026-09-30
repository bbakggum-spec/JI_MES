using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Codes;
using Microsoft.Extensions.DependencyInjection;

namespace JiMes.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class CommonCodeTests(ApiFixture fx)
{
    private CommonCodeCache Cache => fx.Factory.Services.GetRequiredService<CommonCodeCache>();

    private async Task<long> CodeIdAsync(string group, string code)
    {
        await using var conn = await fx.OpenAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            SELECT c.common_code_id FROM common_code c JOIN common_code_group g ON g.common_code_group_id = c.common_code_group_id
             WHERE g.group_code = @group AND c.code = @code
            """, new { group, code });
    }

    [Fact]
    public async Task List_is_grouped_and_includes_attributes()
    {
        var client = await fx.LoginAdminAsync();
        var groups = await client.GetFromJsonAsync<JsonElement>("/api/common-codes");
        var priority = groups.EnumerateArray().Single(g => g.GetProperty("groupCode").GetString() == "PRIORITY");
        var urgent = priority.GetProperty("codes").EnumerateArray().Single(c => c.GetProperty("code").GetString() == "3");
        Assert.Contains("#E53935", urgent.GetProperty("attrJson").GetString());
        Assert.True(urgent.GetProperty("isSystem").GetBoolean());
    }

    [Fact]
    public async Task System_code_display_name_can_change_and_cache_follows()
    {
        var id = await CodeIdAsync("DECISION", "CONDITIONAL");
        var client = await fx.LoginAdminAsync();
        try
        {
            var res = await client.PutAsJsonAsync($"/api/common-codes/{id}",
                new { codeName = "특채", sortOrder = 3, isActive = true, reason = "표시명 변경" });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("특채", Cache.GetName("DECISION", "CONDITIONAL"));

            await using var conn = await fx.OpenAsync();
            Assert.Equal(1, await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_log WHERE table_name = 'common_code' AND record_id = @id AND JSON_VALUE(after_json, '$.code_name') = '특채'",
                new { id }));
        }
        finally
        {
            await client.PutAsJsonAsync($"/api/common-codes/{id}", new { codeName = "조건부합격", sortOrder = 3, isActive = true });
        }
    }

    [Fact]
    public async Task System_code_cannot_be_deactivated()
    {
        var id = await CodeIdAsync("DECISION", "PASS");
        var client = await fx.LoginAdminAsync();
        var res = await client.PutAsJsonAsync($"/api/common-codes/{id}", new { codeName = "합격", sortOrder = 1, isActive = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SYSTEM_CODE_LOCKED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Invalid_attr_json_is_rejected_and_unknown_id_is_404()
    {
        var client = await fx.LoginAdminAsync();
        var id = await CodeIdAsync("PRIORITY", "3");
        var bad = await client.PutAsJsonAsync($"/api/common-codes/{id}", new { codeName = "긴급", sortOrder = 4, isActive = true, attrJson = "{color" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var missing = await client.PutAsJsonAsync("/api/common-codes/999999", new { codeName = "x", sortOrder = 1, isActive = true });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
