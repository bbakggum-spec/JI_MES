using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;

namespace JiMes.Api.Tests.Equipment;

/// <summary>작업자 주·야 배치 보드 (설계 §28.5) — 배치·이동·주/보조·중복·복사·삭제</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkerAssignmentTests(ApiFixture fx)
{
    [Fact]
    public async Task Assign_move_toggle_copy_and_remove()
    {
        var client = await fx.LoginAdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        long day, night, eq1, eq2, emp;
        await using (var c = await fx.OpenAsync())
        {
            // 교대는 스케줄 작업일 시작에 영향 → 테스트 끝에 지운다
            day = await c.ExecuteScalarAsync<long>("INSERT INTO work_shift (work_shift_code, work_shift_name, start_time, end_time, sort_order) VALUES (@code, '주간', '08:00', '20:00', 900); SELECT LAST_INSERT_ID();", new { code = $"D{tag}" });
            night = await c.ExecuteScalarAsync<long>("INSERT INTO work_shift (work_shift_code, work_shift_name, start_time, end_time, is_next_day_end, sort_order) VALUES (@code, '야간', '20:00', '08:00', 1, 901); SELECT LAST_INSERT_ID();", new { code = $"N{tag}" });
            eq1 = await c.ExecuteScalarAsync<long>("INSERT INTO equipment (equipment_code, equipment_name) VALUES (@code, @code); SELECT LAST_INSERT_ID();", new { code = $"WA1-{tag}" });
            eq2 = await c.ExecuteScalarAsync<long>("INSERT INTO equipment (equipment_code, equipment_name) VALUES (@code, @code); SELECT LAST_INSERT_ID();", new { code = $"WA2-{tag}" });
            emp = await c.ExecuteScalarAsync<long>("INSERT INTO employee (employee_code, employee_name, is_assignment_target) VALUES (@code, '홍길동', 1); SELECT LAST_INSERT_ID();", new { code = $"E{tag}" });
        }
        try
        {
            const string date = "2026-11-02";
            var board = await client.GetFromJsonAsync<JsonElement>($"/api/worker-assignments/board?workDate={date}");
            Assert.Contains(board.GetProperty("shifts").EnumerateArray(), s => s.GetProperty("workShiftId").GetInt64() == night);
            Assert.Contains(board.GetProperty("employees").EnumerateArray(), e => e.GetProperty("employeeId").GetInt64() == emp);

            var res = await client.PostAsJsonAsync("/api/worker-assignments", new { workDate = date, workShiftId = day, equipmentId = eq1, employeeId = emp });
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
            var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
            // 같은 칸 같은 사람 = 중복, 같은 교대 다른 설비 = 허용 (여러 대 담당)
            Assert.Equal(HttpStatusCode.UnprocessableEntity,
                (await client.PostAsJsonAsync("/api/worker-assignments", new { workDate = date, workShiftId = day, equipmentId = eq1, employeeId = emp })).StatusCode);
            Assert.True((await client.PostAsJsonAsync("/api/worker-assignments", new { workDate = date, workShiftId = day, equipmentId = eq2, employeeId = emp, assignmentType = "SUPPORT" })).IsSuccessStatusCode);

            // 야간으로 옮기고 보조로
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/worker-assignments/{id}", new { workShiftId = night, assignmentType = "SUPPORT" })).StatusCode);
            // eq2 주간으로 옮기면 이미 있음 → 중복
            Assert.Equal(HttpStatusCode.UnprocessableEntity,
                (await client.PutAsJsonAsync($"/api/worker-assignments/{id}", new { workShiftId = day, equipmentId = eq2 })).StatusCode);

            board = await client.GetFromJsonAsync<JsonElement>($"/api/worker-assignments/board?workDate={date}");
            var moved = board.GetProperty("assignments").EnumerateArray().Single(a => a.GetProperty("workerAssignmentId").GetInt64() == id);
            Assert.Equal(night, moved.GetProperty("workShiftId").GetInt64());
            Assert.Equal("SUPPORT", moved.GetProperty("assignmentType").GetString());

            // 다음 날로 복사 (두 번 해도 늘지 않음)
            var copy = await client.PostAsJsonAsync("/api/worker-assignments/copy", new { fromDate = date, toDate = "2026-11-03" });
            Assert.Equal(2, (await copy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("copied").GetInt32());
            copy = await client.PostAsJsonAsync("/api/worker-assignments/copy", new { fromDate = date, toDate = "2026-11-03" });
            Assert.Equal(0, (await copy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("copied").GetInt32());

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/worker-assignments/{id}")).StatusCode);
            board = await client.GetFromJsonAsync<JsonElement>($"/api/worker-assignments/board?workDate={date}");
            Assert.DoesNotContain(board.GetProperty("assignments").EnumerateArray(), a => a.GetProperty("workerAssignmentId").GetInt64() == id);
        }
        finally
        {
            await using var c = await fx.OpenAsync();
            await c.ExecuteAsync("DELETE FROM worker_assignment WHERE work_shift_id IN (@day, @night)", new { day, night });
            await c.ExecuteAsync("DELETE FROM work_shift WHERE work_shift_id IN (@day, @night)", new { day, night });
        }
    }
}
