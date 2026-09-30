using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Tests.Scheduling;

/// <summary>생산계획 API — 설비 잠금·row_version·재계산·작업시간 결정 흐름.</summary>
[Collection(ApiCollection.Name)]
public sealed class SchedulingApiTests(ApiFixture fx) : IAsyncLifetime
{
    private sealed record Seed(long Gas1, long Gas2, long Carb, long ItemA, long ItemB, long ItemC, long ItemD);

    private Seed _s = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = await fx.LoginAdminAsync();
        _s = await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// 테스트마다 독립된 설비·수주를 만든다 (같은 테스트 DB 를 여러 테스트가 쓴다).
    /// A: 표준 있음(120분, charge 100) / B·C: 표준 없음 / D: 표준 300분.
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var c = await fx.OpenAsync();
        async Task<long> Insert(string sql, object? args = null) =>
            await c.ExecuteScalarAsync<long>(sql + "; SELECT LAST_INSERT_ID();", args);

        var type = await Insert("INSERT INTO equipment_type (equipment_type_code, equipment_type_name) VALUES (@code, '가스로')", new { code = $"GAS-{tag}" });
        var gas1 = await Insert("INSERT INTO equipment (equipment_type_id, equipment_code, equipment_initial, equipment_name) VALUES (@type, @code, 'G1', '가스로1')", new { type, code = $"G1-{tag}" });
        var gas2 = await Insert("INSERT INTO equipment (equipment_type_id, equipment_code, equipment_initial, equipment_name) VALUES (@type, @code, 'G2', '가스로2')", new { type, code = $"G2-{tag}" });
        var carb = await Insert("INSERT INTO unit_process (unit_process_code, unit_process_name) VALUES (@code, '침탄')", new { code = $"CARB-{tag}" });
        await Insert("INSERT INTO step_template (step_template_code, step_template_name, unit_process_id, equipment_type_id) VALUES (@code, '침탄', @carb, @type)", new { code = $"ST-{tag}", carb, type });
        var hp = await Insert("INSERT INTO heat_process (heat_process_code, heat_process_name) VALUES (@code, '침탄')", new { code = $"HP-{tag}" });
        var hpv = await Insert("INSERT INTO heat_process_version (heat_process_id, version_no, effective_from, is_current) VALUES (@hp, 1, '2026-01-01', 1)", new { hp });
        await Insert("INSERT INTO heat_process_operation (heat_process_version_id, sequence_no, unit_process_id, is_main_process) VALUES (@hpv, 10, @carb, 1)", new { hpv, carb });
        var customer = await Insert("INSERT INTO customer (customer_code, customer_name) VALUES (@code, '고객')", new { code = $"C-{tag}" });

        async Task<long> Part(string name, decimal? minutes, decimal charge)
        {
            var part = await Insert("INSERT INTO part (part_code, part_name) VALUES (@code, @name)", new { code = $"{name}-{tag}", name });
            if (minutes is not null)
            {
                var std = await Insert(
                    "INSERT INTO standard (standard_code, standard_name, part_id, unit_process_id, equipment_type_id) VALUES (@code, @name, @part, @carb, @type)",
                    new { code = $"STD-{name}-{tag}", name, part, carb, type });
                await Insert(
                    "INSERT INTO standard_version (standard_id, version_no, charge_qty, running_time_min, effective_from, is_current) VALUES (@std, 1, @charge, @minutes, '2026-01-01', 1)",
                    new { std, charge, minutes });
            }
            return part;
        }
        var partA = await Part("A", 120, 100);
        var partB = await Part("B", null, 0);
        var partD = await Part("D", 300, 1000);

        var order = await Insert("INSERT INTO sales_order (sales_order_no, order_date, customer_id) VALUES (@no, CURDATE(), @customer)", new { no = $"SO-{tag}", customer });
        var line = 0;
        async Task<long> Item(long part, decimal qty) => await Insert(
            """
            INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, heat_process_version_id, order_qty)
            VALUES (@no, @order, @line, @part, @hpv, @qty)
            """, new { no = $"I-{tag}-{++line}", order, line, part, hpv, qty });

        return new Seed(gas1, gas2, carb, await Item(partA, 250), await Item(partB, 50), await Item(partB, 100), await Item(partD, 10));
    }

    private sealed class BlockView
    {
        public long Id { get; init; }
        public long EquipmentId { get; init; }
        public DateTime Start { get; init; }
        public DateTime End { get; init; }
        public decimal Minutes { get; init; }
        public string? Source { get; init; }
        public int RowVersion { get; init; }
        public int Seq { get; init; }
        public string? Lot { get; init; }
        public bool Locked { get; init; }
        public decimal Qty { get; init; }
        public long ItemCount { get; init; }
    }

    private async Task<List<BlockView>> BlocksAsync(long equipmentId)
    {
        await using var c = await fx.OpenAsync();
        return (await c.QueryAsync<BlockView>(
            """
            SELECT ps.production_schedule_id AS id, ps.equipment_id, ps.planned_start_at AS start, ps.planned_end_at AS end,
                   ps.planned_duration_min AS minutes, ps.duration_source AS source, ps.row_version, ps.sequence_no AS seq,
                   ps.planned_lot_no AS lot, ps.is_time_locked AS locked, ps.planned_qty AS qty,
                   (SELECT COUNT(*) FROM production_schedule_item i WHERE i.production_schedule_id = ps.production_schedule_id) AS item_count
              FROM production_schedule ps
             WHERE ps.equipment_id = @equipmentId AND ps.is_deleted = 0 AND ps.status IN ('PLANNED','CONFIRMED')
             ORDER BY ps.planned_start_at
            """, new { equipmentId })).ToList();
    }

    private async Task<long> CreateAsync(long itemId, long equipmentId, long? before = null, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["salesOrderItemId"] = itemId, ["unitProcessId"] = _s.Carb, ["equipmentId"] = equipmentId, ["beforeBlockId"] = before,
        };
        foreach (var p in extra?.GetType().GetProperties() ?? [])
            body[p.Name] = p.GetValue(extra);
        var res = await _client.PostAsJsonAsync("/api/schedule/blocks", body);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("productionScheduleId").GetInt64();
    }

    [Fact]
    public async Task Create_uses_standard_time_and_charge_and_chains_blocks()
    {
        var before = DateTime.Now.AddMinutes(-1);
        var first = await CreateAsync(_s.ItemA, _s.Gas1);
        var second = await CreateAsync(_s.ItemA, _s.Gas1);
        var blocks = await BlocksAsync(_s.Gas1);

        Assert.Equal([first, second], blocks.Select(b => b.Id));
        Assert.All(blocks, b => Assert.Equal((120m, "STANDARD", 100m), (b.Minutes, b.Source, b.Qty)));   // 표준시간, charge 수량
        Assert.True(blocks[0].Start >= before);
        Assert.Equal(blocks[0].End, blocks[1].Start);
        Assert.Matches(@"^P\d{6}-G1-00[12]$", blocks[0].Lot);
        Assert.Equal(1, blocks[0].Seq);
    }

    [Fact]
    public async Task Backlog_shows_remaining_per_unit_process()
    {
        await CreateAsync(_s.ItemA, _s.Gas1);   // 250 중 100 계획
        var rows = await _client.GetFromJsonAsync<JsonElement>("/api/schedule/backlog");
        var a = rows.EnumerateArray().Single(r => r.GetProperty("salesOrderItemId").GetInt64() == _s.ItemA);
        Assert.Equal(150m, a.GetProperty("remainingQty").GetDecimal());
    }

    [Fact]
    public async Task Missing_time_asks_user_then_registers_default_time()
    {
        var res = await _client.PostAsJsonAsync("/api/schedule/blocks",
            new { salesOrderItemId = _s.ItemB, unitProcessId = _s.Carb, equipmentId = _s.Gas1 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DURATION_REQUIRED", problem.GetProperty("code").GetString());
        Assert.Equal(480m, problem.GetProperty("defaultMin").GetDecimal());   // schedule.default_running_time_min

        await CreateAsync(_s.ItemB, _s.Gas1, extra: new { durationMin = 90m, saveAsDefault = true });
        Assert.Equal("USER_INPUT", (await BlocksAsync(_s.Gas1)).Single().Source);

        // 같은 설비유형의 다음 계획은 ③ 기준시간으로 결정
        await CreateAsync(_s.ItemC, _s.Gas2);
        Assert.Equal((90m, "DEFAULT_TIME"), (await BlocksAsync(_s.Gas2)).Select(b => (b.Minutes, b.Source)).Single());
    }

    [Fact]
    public async Task Move_reorders_within_equipment_and_stale_version_conflicts()
    {
        var first = await CreateAsync(_s.ItemA, _s.Gas1);
        var second = await CreateAsync(_s.ItemD, _s.Gas1);
        var blocks = await BlocksAsync(_s.Gas1);
        var secondVersion = blocks.Single(b => b.Id == second).RowVersion;

        var move = await _client.PutAsJsonAsync($"/api/schedule/blocks/{second}/move",
            new { rowVersion = secondVersion, equipmentId = _s.Gas1, beforeBlockId = first });
        Assert.Equal(HttpStatusCode.NoContent, move.StatusCode);
        blocks = await BlocksAsync(_s.Gas1);
        Assert.Equal([second, first], blocks.Select(b => b.Id));
        Assert.Equal(blocks[0].End, blocks[1].Start);

        // 같은 버전으로 다시 이동 = 다른 사용자가 먼저 수정한 상황
        var stale = await _client.PutAsJsonAsync($"/api/schedule/blocks/{second}/move",
            new { rowVersion = secondVersion, equipmentId = _s.Gas1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Move_to_other_equipment_recalculates_both()
    {
        var first = await CreateAsync(_s.ItemA, _s.Gas1);
        var second = await CreateAsync(_s.ItemA, _s.Gas1);
        var v = (await BlocksAsync(_s.Gas1)).Single(b => b.Id == first).RowVersion;

        var res = await _client.PutAsJsonAsync($"/api/schedule/blocks/{first}/move", new { rowVersion = v, equipmentId = _s.Gas2 });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var gas1 = await BlocksAsync(_s.Gas1);
        var gas2 = await BlocksAsync(_s.Gas2);
        Assert.Equal(second, gas1.Single().Id);
        Assert.Equal(first, gas2.Single().Id);
        Assert.Equal(gas2[0].Start, gas1[0].Start);   // 두 설비 모두 체인 맨 앞 (같은 시작점)
        Assert.Matches("-G2-", gas2[0].Lot);
    }

    [Fact]
    public async Task Merge_takes_max_time_of_items()
    {
        var block = await CreateAsync(_s.ItemA, _s.Gas1);   // 120분
        var v = (await BlocksAsync(_s.Gas1)).Single().RowVersion;

        var res = await _client.PostAsJsonAsync($"/api/schedule/blocks/{block}/items", new { rowVersion = v, salesOrderItemId = _s.ItemD });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var merged = (await BlocksAsync(_s.Gas1)).Single();
        Assert.Equal((300m, "STANDARD", 2L, 110m), (merged.Minutes, merged.Source, merged.ItemCount, merged.Qty));   // 최대값, 비례 아님
        Assert.Equal(merged.Start.AddMinutes(300), merged.End);

        var again = await _client.PostAsJsonAsync($"/api/schedule/blocks/{block}/items", new { rowVersion = merged.RowVersion, salesOrderItemId = _s.ItemD });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
    }

    [Fact]
    public async Task Locked_block_keeps_time_when_others_move_in_front()
    {
        var first = await CreateAsync(_s.ItemA, _s.Gas1);
        var second = await CreateAsync(_s.ItemA, _s.Gas1);
        var blocks = await BlocksAsync(_s.Gas1);
        var lockedStart = blocks.Single(b => b.Id == second).Start;

        var lockRes = await _client.PutAsJsonAsync($"/api/schedule/blocks/{second}/lock",
            new { rowVersion = blocks.Single(b => b.Id == second).RowVersion, locked = true });
        Assert.Equal(HttpStatusCode.NoContent, lockRes.StatusCode);

        // 고정 블록 앞에 300분짜리를 넣어도 고정 블록 시각은 그대로, 새 블록은 비켜 간다
        var third = await CreateAsync(_s.ItemD, _s.Gas1, before: first);
        blocks = await BlocksAsync(_s.Gas1);
        var locked = blocks.Single(b => b.Id == second);
        Assert.Equal(lockedStart, locked.Start);
        Assert.All(blocks.Where(b => b.Id != second), b => Assert.True(b.End <= locked.Start || b.Start >= locked.End));

        var moveLocked = await _client.PutAsJsonAsync($"/api/schedule/blocks/{second}/move",
            new { rowVersion = locked.RowVersion, equipmentId = _s.Gas2 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, moveLocked.StatusCode);
        _ = third;
    }

    [Fact]
    public async Task Cancel_returns_quantity_to_backlog_and_closes_gap()
    {
        var first = await CreateAsync(_s.ItemA, _s.Gas1);
        var second = await CreateAsync(_s.ItemA, _s.Gas1);
        var blocks = await BlocksAsync(_s.Gas1);
        var firstStart = blocks[0].Start;

        var res = await _client.PostAsJsonAsync($"/api/schedule/blocks/{first}/cancel", new { rowVersion = blocks[0].RowVersion });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var remaining = (await BlocksAsync(_s.Gas1)).Single();
        Assert.Equal(second, remaining.Id);
        Assert.Equal(firstStart, remaining.Start);   // 앞으로 당겨짐

        await using var c = await fx.OpenAsync();
        Assert.Equal(1, await c.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_log WHERE table_name = 'production_schedule' AND record_id = @first AND action_type = 'STATUS_CHANGE'", new { first }));
    }

    [Fact]
    public async Task Planned_downtime_pushes_block_after_it()
    {
        var now = DateTime.Now;
        await using (var c = await fx.OpenAsync())
            await c.ExecuteAsync(
                "INSERT INTO equipment_downtime (equipment_id, downtime_date, started_at, ended_at, is_planned) VALUES (@e, CURDATE(), @s, @end, 1)",
                new { e = _s.Gas1, s = now.AddMinutes(-5), end = now.AddHours(3) });
        await CreateAsync(_s.ItemA, _s.Gas1);
        var block = (await BlocksAsync(_s.Gas1)).Single();
        Assert.True(block.Start >= now.AddHours(3).AddMinutes(-1), $"{block.Start} 는 비가동 종료 이후여야 함");
    }

    [Fact]
    public async Task Running_work_delays_following_plans()
    {
        var now = DateTime.Now;
        await using (var c = await fx.OpenAsync())
            await c.ExecuteAsync(
                """
                INSERT INTO production_work (lot_no, unit_process_id, equipment_id, work_date, status, actual_start_at, expected_duration_min)
                VALUES (@lot, @up, @e, CURDATE(), 'INPUT', @start, 240)
                """, new { lot = $"RUN-{Guid.NewGuid():N}"[..20], up = _s.Carb, e = _s.Gas1, start = now.AddHours(-1) });
        await CreateAsync(_s.ItemA, _s.Gas1);
        var block = (await BlocksAsync(_s.Gas1)).Single();
        // 진행 중 작업 예상 종료(시작 + 240분) 뒤
        Assert.Equal(TrimToMinute(now.AddHours(-1)).AddMinutes(240), TrimToMinute(block.Start));
    }

    [Fact]
    public async Task Board_returns_blocks_with_items_and_limits_range()
    {
        await CreateAsync(_s.ItemA, _s.Gas1);
        var board = await _client.GetFromJsonAsync<JsonElement>("/api/schedule/board?days=2");
        var block = board.GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("equipmentId").GetInt64() == _s.Gas1);
        Assert.Equal("침탄", block.GetProperty("unitProcessName").GetString());
        Assert.Single(block.GetProperty("items").EnumerateArray());
        Assert.Equal("08:00", board.GetProperty("dayStart").GetString());

        var tooMany = await _client.GetAsync("/api/schedule/board?days=99");
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task Read_only_user_cannot_change_plans()
    {
        var login = $"sched_ro_{Guid.NewGuid():N}"[..20];
        await fx.CreateUserAsync(login, "sched-ro-pw", (MenuKeys.ProductionSchedule, PermissionAction.Read));
        var client = await fx.LoginAsync(login, "sched-ro-pw");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/schedule/board")).StatusCode);
        var res = await client.PostAsJsonAsync("/api/schedule/blocks",
            new { salesOrderItemId = _s.ItemA, unitProcessId = _s.Carb, equipmentId = _s.Gas1 });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    private sealed class ScheduleState
    {
        public string Status { get; init; } = "";
        public int RowVersion { get; init; }
        public DateTime WorkDate { get; init; }
    }

    private async Task<ScheduleState> StateAsync(long blockId)
    {
        await using var c = await fx.OpenAsync();
        return await c.QuerySingleAsync<ScheduleState>(
            "SELECT status, row_version, work_date FROM production_schedule WHERE production_schedule_id = @blockId", new { blockId });
    }

    [Fact]
    public async Task Confirm_release_and_unrelease_manage_work_lots()
    {
        var first = await CreateAsync(_s.ItemA, _s.Gas1);
        var second = await CreateAsync(_s.ItemD, _s.Gas1);

        // 확정 (표시만, 여전히 재계산 대상)
        var confirm = await _client.PutAsJsonAsync($"/api/schedule/blocks/{second}/confirm", new { rowVersion = (await StateAsync(second)).RowVersion, confirmed = true });
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal("CONFIRMED", (await StateAsync(second)).Status);

        // 작업지시 (앞선 계획 포함) → 작업 LOT 2개, 배정 상태
        var res = await _client.PostAsJsonAsync($"/api/schedule/blocks/{second}/release", new { rowVersion = (await StateAsync(second)).RowVersion, includePrevious = true });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var lots = (await res.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Equal([first, second], lots.Select(l => l.GetProperty("productionScheduleId").GetInt64()));
        var workDate = (await StateAsync(first)).WorkDate;
        Assert.Equal($"{workDate:yyMMdd}-G1-001", lots[0].GetProperty("lotNo").GetString());
        Assert.Empty(await BlocksAsync(_s.Gas1));   // 재계산 대상에서 빠짐

        await using (var c = await fx.OpenAsync())
        {
            var work = await c.QuerySingleAsync<(string Status, bool IsMain, long EventCount)>(
                """
                SELECT w.status, w.is_main_process, (SELECT COUNT(*) FROM production_work_event e WHERE e.production_work_id = w.production_work_id AND e.event_type = 'ALLOCATE')
                  FROM production_work w WHERE w.production_schedule_id = @first
                """, new { first });
            Assert.Equal(("ALLOCATED", true, 1L), work);
        }

        // 보드에 작업 LOT 표시
        var board = await _client.GetFromJsonAsync<JsonElement>($"/api/schedule/board?from={workDate:yyyy-MM-dd}&days=3");
        var released = board.GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("productionScheduleId").GetInt64() == first);
        Assert.Equal(("RELEASED", lots[0].GetProperty("lotNo").GetString()), (released.GetProperty("status").GetString(), released.GetProperty("workLotNo").GetString()));

        // 작업지시 취소 → 확정 상태로, LOT 은 취소(삭제 표시). 다시 작업지시하면 새 순번
        var unrelease = await _client.PostAsJsonAsync($"/api/schedule/blocks/{first}/unrelease", new { rowVersion = (await StateAsync(first)).RowVersion, reason = "순서 변경" });
        Assert.Equal(HttpStatusCode.NoContent, unrelease.StatusCode);
        Assert.Equal("CONFIRMED", (await StateAsync(first)).Status);
        var again = await _client.PostAsJsonAsync($"/api/schedule/blocks/{first}/release", new { rowVersion = (await StateAsync(first)).RowVersion, includePrevious = false });
        var againLot = (await again.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("lotNo").GetString();
        // 같은 작업일이면 001(취소)·002 다음 003, 두 계획의 작업일이 갈리면(작업일 시작 직전) 001(취소) 다음 002
        Assert.EndsWith(workDate == (await StateAsync(second)).WorkDate ? "-G1-003" : "-G1-002", againLot);

        // 투입된 LOT 은 작업지시 취소 불가
        await using (var c = await fx.OpenAsync())
            await c.ExecuteAsync("UPDATE production_work SET status = 'INPUT' WHERE production_schedule_id = @second", new { second });
        var started = await _client.PostAsJsonAsync($"/api/schedule/blocks/{second}/unrelease", new { rowVersion = (await StateAsync(second)).RowVersion });
        Assert.Equal("WORK_STARTED", (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private static DateTime TrimToMinute(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);
}
