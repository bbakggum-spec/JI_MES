using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Tests.Equipment;

/// <summary>설비 비가동 (설계 §28.1) — 진행 중 등록·종료, 시간 계산, 계획 비가동 검증, 사유 코드, 권한</summary>
[Collection(ApiCollection.Name)]
public sealed class DowntimeTests(ApiFixture fx)
{
    private async Task<long> EquipmentAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var c = await fx.OpenAsync();
        return await c.ExecuteScalarAsync<long>(
            "INSERT INTO equipment (equipment_code, equipment_name) VALUES (@code, @code); SELECT LAST_INSERT_ID();", new { code = $"DT-{tag}" });
    }

    [Fact]
    public async Task Breakdown_is_open_until_ended_and_duration_is_computed()
    {
        var client = await fx.LoginAdminAsync();
        var equipmentId = await EquipmentAsync();
        var start = new DateTime(2026, 3, 2, 22, 10, 30);

        var res = await client.PostAsJsonAsync("/api/downtimes", new { equipmentId, startedAt = start, isPlanned = false, remark = "버너" });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        // 진행 중은 기간 밖이어도 목록에 나옴
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/downtimes?from=2030-01-01&to=2030-01-31&equipmentId={equipmentId}");
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("endedAt").ValueKind);

        // 자정 넘어 종료 → 분 단위 (초 버림)
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync($"/api/downtimes/{id}/end", new { endedAt = new DateTime(2026, 3, 3, 1, 40, 59) })).StatusCode);
        list = await client.GetFromJsonAsync<JsonElement>($"/api/downtimes?from=2026-03-01&to=2026-03-31&equipmentId={equipmentId}");
        row = Assert.Single(list.EnumerateArray());
        Assert.Equal(210m, row.GetProperty("durationMin").GetDecimal());
        Assert.Equal("2026-03-02T00:00:00", row.GetProperty("downtimeDate").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/downtimes/{id}/end", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/downtimes/{id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/downtimes?from=2026-03-01&to=2026-03-31&equipmentId={equipmentId}")).EnumerateArray());
    }

    [Fact]
    public async Task Planned_downtime_needs_end_and_reason_must_be_a_code()
    {
        var client = await fx.LoginAdminAsync();
        var equipmentId = await EquipmentAsync();
        var start = new DateTime(2026, 3, 5, 8, 0, 0);

        var noEnd = await client.PostAsJsonAsync("/api/downtimes", new { equipmentId, startedAt = start, isPlanned = true });
        Assert.Equal(HttpStatusCode.BadRequest, noEnd.StatusCode);
        var badReason = await client.PostAsJsonAsync("/api/downtimes", new { equipmentId, startedAt = start, endedAt = start.AddHours(1), isPlanned = true, reasonCode = "없는사유" });
        Assert.Equal(HttpStatusCode.BadRequest, badReason.StatusCode);
        var reversed = await client.PostAsJsonAsync("/api/downtimes", new { equipmentId, startedAt = start, endedAt = start.AddHours(-1), isPlanned = true });
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/downtimes", new { equipmentId, startedAt = start, endedAt = start.AddHours(2), isPlanned = true });
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        var id = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var update = await client.PutAsJsonAsync($"/api/downtimes/{id}", new { equipmentId, startedAt = start, endedAt = start.AddMinutes(30), isPlanned = true });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var row = (await client.GetFromJsonAsync<JsonElement>($"/api/downtimes?from=2026-03-05&to=2026-03-05&equipmentId={equipmentId}&planned=true"))[0];
        Assert.Equal(30m, row.GetProperty("durationMin").GetDecimal());
    }

    [Fact]
    public async Task Requires_downtime_menu_permission()
    {
        var login = $"dt_{Guid.NewGuid():N}"[..20];
        await fx.CreateUserAsync(login, "dt-pw-12345", (MenuKeys.EquipmentDowntime, PermissionAction.Read));
        var client = await fx.LoginAsync(login, "dt-pw-12345");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/downtimes?from=2026-01-01&to=2026-01-02")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/downtimes", new { equipmentId = 1, startedAt = DateTime.Now, isPlanned = false })).StatusCode);
    }
}
