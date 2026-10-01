using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace JiMes.Api.Tests.Equipment;

/// <summary>설비 보전·측정기구 교정 (설계 §28.3~28.4)</summary>
[Collection(ApiCollection.Name)]
public sealed class MaintenanceTests(ApiFixture fx)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<long> EquipmentAsync()
    {
        await using var c = await fx.OpenAsync();
        var code = $"MT-{Tag()}";
        return await c.ExecuteScalarAsync<long>("INSERT INTO equipment (equipment_code, equipment_name) VALUES (@code, @code); SELECT LAST_INSERT_ID();", new { code });
    }

    private static MultipartFormDataContent File(string kind, string name = "a.txt") =>
        new() { { new ByteArrayContent("hello"u8.ToArray()), "file", name }, { new StringContent(kind), "kind" } };

    [Fact]
    public async Task Maintenance_lifecycle_due_state_and_attachments()
    {
        var client = await fx.LoginAdminAsync();
        var equipmentId = await EquipmentAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var bad = await client.PostAsJsonAsync("/api/maintenances", new { equipmentId, maintenanceType = "없는구분", maintenanceDate = today });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var res = await client.PostAsJsonAsync("/api/maintenances", new
        {
            equipmentId, maintenanceType = "REPAIR", maintenanceDate = today.AddDays(-30), status = "COMPLETED",
            repairPart = "버너", cost = 50000, nextDueDate = today.AddDays(3),
        });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/maintenances/{id}");
        var m = detail.GetProperty("maintenance");
        // 완료 + 완료 시각 없음 + 지난 날짜 → 완료 시각 = 보전일
        Assert.StartsWith(today.AddDays(-30).ToString("yyyy-MM-dd"), m.GetProperty("completedAt").GetString());
        Assert.Equal(0, m.GetProperty("rowVersion").GetInt32());

        // 다음 점검 3일 뒤 (임박 기준 14일) → 임박
        var due = await client.GetFromJsonAsync<JsonElement>("/api/maintenances/due");
        var mine = due.EnumerateArray().Single(d => d.GetProperty("equipmentId").GetInt64() == equipmentId);
        Assert.Equal("DUE_SOON", mine.GetProperty("dueState").GetString());

        // 낙관적 잠금
        var update = new { rowVersion = 0, equipmentId, maintenanceType = "REPAIR", maintenanceDate = today.AddDays(-30), status = "COMPLETED", nextDueDate = today.AddDays(-1) };
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/maintenances/{id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/maintenances/{id}", update)).StatusCode);
        due = await client.GetFromJsonAsync<JsonElement>("/api/maintenances/due");
        Assert.Equal("OVERDUE", due.EnumerateArray().Single(d => d.GetProperty("equipmentId").GetInt64() == equipmentId).GetProperty("dueState").GetString());

        // 첨부: 보전 사진·기타 허용, 다른 소유 종류(품목 도면) 거부
        Assert.True((await client.PostAsync($"/api/maintenances/{id}/attachments", File("ETC"))).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/maintenances/{id}/attachments", File("PART_DRAWING"))).StatusCode);
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>($"/api/maintenances/{id}")).GetProperty("attachments").GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/maintenances/{id}?rowVersion=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/maintenances/{id}")).StatusCode);
    }

    [Fact]
    public async Task Calibration_updates_instrument_last_and_next_dates()
    {
        var client = await fx.LoginAdminAsync();
        long instrumentId;
        await using (var c = await fx.OpenAsync())
            instrumentId = await c.ExecuteScalarAsync<long>(
                "INSERT INTO instrument (instrument_code, instrument_name, calibration_cycle_day) VALUES (@code, '경도계', 365); SELECT LAST_INSERT_ID();",
                new { code = $"IN-{Tag()}" });

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/calibrations", new { instrumentId, calibrationDate = "2025-01-10", result = "NA" })).StatusCode);

        // 다음 교정일 비움 → 교정일 + 주기 365일
        var r1 = await client.PostAsJsonAsync("/api/calibrations", new { instrumentId, calibrationDate = "2025-01-10", result = "PASS", agencyName = "KTL" });
        Assert.True(r1.IsSuccessStatusCode, await r1.Content.ReadAsStringAsync());
        var latest = (await r1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        // 더 오래된 교정은 최근 교정일을 바꾸지 않음
        Assert.True((await client.PostAsJsonAsync("/api/calibrations", new { instrumentId, calibrationDate = "2024-01-05", result = "PASS", nextCalibrationDate = "2025-01-05" })).IsSuccessStatusCode);

        async Task<JsonElement> Instrument() =>
            (await client.GetFromJsonAsync<JsonElement>("/api/calibrations/instruments")).EnumerateArray().Single(i => i.GetProperty("instrumentId").GetInt64() == instrumentId);
        var inst = await Instrument();
        Assert.Equal("2025-01-10T00:00:00", inst.GetProperty("lastCalibratedDate").GetString());
        Assert.Equal("2026-01-10T00:00:00", inst.GetProperty("nextCalibrationDate").GetString());
        Assert.Equal(2, inst.GetProperty("calibrationCount").GetInt64());
        Assert.Equal("OVERDUE", inst.GetProperty("dueState").GetString());   // 2026-01-10 은 지남 (오늘 2026-10 기준)

        // 최근 교정 삭제 → 이전 교정으로 되돌림
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/calibrations/{latest}")).StatusCode);
        inst = await Instrument();
        Assert.Equal("2024-01-05T00:00:00", inst.GetProperty("lastCalibratedDate").GetString());
        Assert.Equal("2025-01-05T00:00:00", inst.GetProperty("nextCalibrationDate").GetString());
        Assert.Single((await client.GetFromJsonAsync<JsonElement>($"/api/calibrations?instrumentId={instrumentId}")).EnumerateArray());
    }
}
