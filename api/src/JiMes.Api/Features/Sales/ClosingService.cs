using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Numbering;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Sales;

public sealed class ClosingDto
{
    public long ShipmentClosingId { get; init; }
    public string ClosingNo { get; init; } = "";
    public long CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public DateTime ClosingDate { get; init; }
    public int ClosingYear { get; init; }
    public int ClosingMonth { get; init; }
    public decimal TotalQty { get; init; }
    public decimal TotalWeight { get; init; }
    public decimal TotalAmount { get; init; }
    public string ClosingStatus { get; init; } = "";
    public DateTime? ClosedAt { get; init; }
    public string? ClosedByName { get; init; }
    public long ShipmentCount { get; init; }
    public string? Remark { get; init; }
    public int RowVersion { get; init; }
}

/// <summary>마감 화면의 업체별 요약 (그 달 마감 기준일·미마감·마감 금액)</summary>
public sealed class ClosingCustomerDto
{
    public long CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public int? ClosingDay { get; init; }
    public DateTime ClosingDate { get; set; }
    public long OpenCount { get; init; }
    public decimal OpenAmount { get; init; }
    public long ClosedCount { get; init; }
    public decimal ClosedAmount { get; init; }
}

public sealed record ClosingCreateRequest(long CustomerId, int Year, int Month, DateOnly? ClosingDate, long[]? ShipmentIds, bool CarryOverOthers, string? Remark);
public sealed record CarryOverRequest(long[]? ShipmentIds, int Year, int Month);
public sealed record ClosingVersionRequest(int RowVersion, string? Reason);

/// <summary>
/// 마감 — 구 F_MonthlyClosing (설계 §6, S6·S7). 업체별로, 전표 단위로 마감·이월·마감 취소.
/// 마감 실행 = shipment_closing 1건(누가·언제·합계 Snapshot) + 선택 전표 CLOSED(귀속 연·월). 선택하지 않은 전표는 이월(CARRIED_OVER, 다음 달) 가능.
/// 마감 기준일 = 업체 마감일(customer.closing_day, 말일 보정). 마감된 전표는 수정·취소 불가, 마감 취소(REOPENED)는 감사 기록.
/// </summary>
public sealed class ClosingService(IDbConnectionFactory db, AuditWriter audit, ICurrentUser currentUser, SettingsCache settings, TimeProvider time)
{
    private const string Table = "shipment_closing";

    private const string ClosingSql =
        """
        SELECT sc.shipment_closing_id, sc.closing_no, sc.customer_id, c.customer_name, sc.closing_date, sc.closing_year, sc.closing_month, sc.total_qty,
               sc.total_weight, sc.total_amount, sc.closing_status, sc.closed_at, u.user_name AS closed_by_name,
               (SELECT COUNT(*) FROM shipment s WHERE s.shipment_closing_id = sc.shipment_closing_id) AS shipment_count, sc.remark, sc.row_version
          FROM shipment_closing sc
          JOIN customer c ON c.customer_id = sc.customer_id
          LEFT JOIN app_user u ON u.app_user_id = sc.closed_by
        """;

    /// <summary>그 달 업체별 요약 — 미마감(미마감 + 그 달로 이월된 전표, 마감 기준일까지)과 마감 건</summary>
    public async Task<IEnumerable<ClosingCustomerDto>> CustomersAsync(int year, int month, CancellationToken ct)
    {
        Month(year, month);
        await using var conn = await db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<ClosingCustomerDto>(
            """
            SELECT c.customer_id, c.customer_name, c.closing_day,
                   COUNT(CASE WHEN s.closing_status <> 'CLOSED' THEN 1 END) AS open_count,
                   COALESCE(SUM(CASE WHEN s.closing_status <> 'CLOSED' THEN s.total_amount END), 0) AS open_amount,
                   COUNT(CASE WHEN s.closing_status = 'CLOSED' AND s.closing_year = @year AND s.closing_month = @month THEN 1 END) AS closed_count,
                   COALESCE(SUM(CASE WHEN s.closing_status = 'CLOSED' AND s.closing_year = @year AND s.closing_month = @month THEN s.total_amount END), 0) AS closed_amount
              FROM customer c
              JOIN shipment s ON s.customer_id = c.customer_id AND s.is_deleted = 0 AND s.status <> 'CANCELLED'
             WHERE (s.closing_status <> 'CLOSED' AND s.shipment_date <= LAST_DAY(@monthStart))
                OR (s.closing_status = 'CLOSED' AND s.closing_year = @year AND s.closing_month = @month)
             GROUP BY c.customer_id, c.customer_name, c.closing_day
             ORDER BY c.customer_name
            """, new { year, month, monthStart = new DateTime(year, month, 1) })).ToList();
        foreach (var r in rows) r.ClosingDate = ShipmentMath.ClosingDate(year, month, r.ClosingDay).ToDateTime(TimeOnly.MinValue);
        return rows;
    }

    /// <summary>마감 후보 전표 — 그 업체의 미마감·이월 전표 (마감 기준일 이후 출하도 보이되 기본 선택은 기준일까지)</summary>
    public async Task<object> CandidatesAsync(long customerId, int year, int month, CancellationToken ct)
    {
        Month(year, month);
        await using var conn = await db.OpenAsync(ct);
        var closingDay = await conn.ExecuteScalarAsync<int?>("SELECT closing_day FROM customer WHERE customer_id = @customerId", new { customerId });
        var closingDate = ShipmentMath.ClosingDate(year, month, closingDay);
        var shipments = await conn.QueryAsync<ShipmentDto>(
            ShipmentService.ShipmentSql + """
             WHERE s.customer_id = @customerId AND s.is_deleted = 0 AND s.status <> 'CANCELLED' AND s.closing_status <> 'CLOSED'
               AND s.shipment_date <= LAST_DAY(@monthStart)
             ORDER BY s.shipment_date, s.shipment_no
            """, new { customerId, monthStart = new DateTime(year, month, 1) });
        var closings = await conn.QueryAsync<ClosingDto>(ClosingSql + " WHERE sc.customer_id = @customerId AND sc.closing_year = @year AND sc.closing_month = @month ORDER BY sc.shipment_closing_id",
            new { customerId, year, month });
        return new { closingDate = closingDate.ToDateTime(TimeOnly.MinValue), shipments, closings };
    }

    public async Task<IEnumerable<ClosingDto>> ListAsync(int year, int month, long? customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.QueryAsync<ClosingDto>(
            ClosingSql + " WHERE sc.closing_year = @year AND sc.closing_month = @month AND (@customerId IS NULL OR sc.customer_id = @customerId) ORDER BY c.customer_name, sc.shipment_closing_id",
            new { year, month, customerId });
    }

    public async Task<object> DetailAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var closing = await conn.QuerySingleOrDefaultAsync<ClosingDto>(ClosingSql + " WHERE sc.shipment_closing_id = @id", new { id })
            ?? throw new NotFoundException(Table, id);
        var shipments = await conn.QueryAsync<ShipmentDto>(ShipmentService.ShipmentSql + " WHERE s.shipment_closing_id = @id ORDER BY s.shipment_date, s.shipment_no", new { id });
        return new { closing, shipments };
    }

    /// <summary>마감 (구 마감 버튼 + 자동 이월) — 선택 전표 마감, carryOverOthers 면 나머지 미마감 전표(그 달까지)를 다음 달로 이월</summary>
    public async Task<object> CloseAsync(ClosingCreateRequest r, CancellationToken ct)
    {
        Month(r.Year, r.Month);
        var ids = (r.ShipmentIds ?? []).Distinct().ToArray();
        if (ids.Length == 0) throw new RequestValidationException("shipmentIds", "마감할 전표를 고르세요.");
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var closingDay = await conn.ExecuteScalarAsync<int?>("SELECT closing_day FROM customer WHERE customer_id = @CustomerId", r, tx);
        var closingDate = r.ClosingDate ?? ShipmentMath.ClosingDate(r.Year, r.Month, closingDay);
        var locked = (await conn.QueryAsync<(long Id, long CustomerId, string Status, string ClosingStatus)>(
            "SELECT CAST(shipment_id AS SIGNED), CAST(customer_id AS SIGNED), status, closing_status FROM shipment WHERE shipment_id IN @ids AND is_deleted = 0 FOR UPDATE",
            new { ids }, tx)).ToList();
        if (locked.Count != ids.Length || locked.Any(s => s.CustomerId != r.CustomerId))
            throw new RequestValidationException("shipmentIds", "이 거래처의 전표가 아닌 것이 있습니다.");   // 전표·마감 업체 일치 (§6)
        if (locked.Any(s => s.Status == "CANCELLED" || s.ClosingStatus == "CLOSED"))
            throw new BusinessRuleException("SHIPMENT_NOT_CLOSABLE", "취소되었거나 이미 마감된 전표가 있습니다.");

        var totals = await conn.QuerySingleAsync<(decimal Qty, decimal Weight, decimal Amount)>(
            """
            SELECT COALESCE(SUM(si.shipment_qty), 0), COALESCE(SUM(si.shipment_weight), 0),
                   (SELECT COALESCE(SUM(total_amount), 0) FROM shipment WHERE shipment_id IN @ids)
              FROM shipment_item si WHERE si.shipment_id IN @ids
            """, new { ids }, tx);
        await using var numbers = await DocumentNumbers.LockAsync(conn, tx, "shipment_closing");
        var no = await numbers.NextAsync(Table, "closing_no", settings.GetString(SettingKeys.ClosingNumberFormat), closingDate);
        var now = time.GetLocalNow().DateTime;
        var closingId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO shipment_closing (closing_no, customer_id, closing_date, closing_year, closing_month, total_qty, total_weight, total_amount,
                   closing_status, closed_at, closed_by, remark)
            VALUES (@no, @CustomerId, @date, @Year, @Month, @qty, @weight, @amount, 'CLOSED', @now, @UserId, @remark);
            SELECT LAST_INSERT_ID();
            """,
            new { no, r.CustomerId, date = closingDate.ToDateTime(TimeOnly.MinValue), r.Year, r.Month, qty = totals.Qty, weight = totals.Weight, amount = totals.Amount, now,
                  currentUser.UserId, remark = r.Remark?.Trim() is { Length: > 0 } rm ? rm : null }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE shipment SET closing_status = 'CLOSED', shipment_closing_id = @closingId, closing_year = @Year, closing_month = @Month,
                   updated_by = @UserId, row_version = row_version + 1
             WHERE shipment_id IN @ids
            """, new { closingId, r.Year, r.Month, ids, currentUser.UserId }, tx);

        var carried = 0;
        if (r.CarryOverOthers)
        {
            var next = new DateOnly(r.Year, r.Month, 1).AddMonths(1);
            carried = await conn.ExecuteAsync(
                """
                UPDATE shipment SET closing_status = 'CARRIED_OVER', closing_year = @y, closing_month = @m, updated_by = @UserId, row_version = row_version + 1
                 WHERE customer_id = @CustomerId AND is_deleted = 0 AND status <> 'CANCELLED' AND closing_status <> 'CLOSED'
                   AND shipment_date <= LAST_DAY(@monthStart) AND shipment_id NOT IN @ids
                """, new { y = next.Year, m = next.Month, r.CustomerId, monthStart = new DateTime(r.Year, r.Month, 1), ids, currentUser.UserId }, tx);
        }
        await audit.WriteAsync(conn, tx, AuditAction.Close, Table, closingId, null,
            new { closing_no = no, r.CustomerId, r.Year, r.Month, shipments = ids, carried, totals.Amount });
        await tx.CommitAsync(ct);
        return new { shipmentClosingId = closingId, closingNo = no, totalAmount = totals.Amount, carried };
    }

    /// <summary>이월 (구 이월 버튼) — 선택 미마감 전표를 지정 월로</summary>
    public async Task<object> CarryOverAsync(CarryOverRequest r, CancellationToken ct)
    {
        Month(r.Year, r.Month);
        var ids = (r.ShipmentIds ?? []).Distinct().ToArray();
        if (ids.Length == 0) throw new RequestValidationException("shipmentIds", "이월할 전표를 고르세요.");
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var closed = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM shipment WHERE shipment_id IN @ids AND (closing_status = 'CLOSED' OR status = 'CANCELLED') FOR UPDATE", new { ids }, tx);
        if (closed > 0) throw new BusinessRuleException("SHIPMENT_NOT_CLOSABLE", "마감되었거나 취소된 전표는 이월할 수 없습니다.");
        var n = await conn.ExecuteAsync(
            """
            UPDATE shipment SET closing_status = 'CARRIED_OVER', closing_year = @Year, closing_month = @Month, updated_by = @UserId, row_version = row_version + 1
             WHERE shipment_id IN @ids AND is_deleted = 0
            """, new { r.Year, r.Month, ids, currentUser.UserId }, tx);
        foreach (var id in ids)
            await audit.WriteAsync(conn, tx, AuditAction.StatusChange, "shipment", id, null, new { closing_status = "CARRIED_OVER", r.Year, r.Month });
        await tx.CommitAsync(ct);
        return new { carried = n };
    }

    /// <summary>마감 취소 (구 마감 취소 → 상태 0) — 전표는 미마감으로, 마감 기록은 REOPENED 로 남긴다</summary>
    public async Task ReopenAsync(long id, ClosingVersionRequest r, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var version = await conn.ExecuteScalarAsync<int?>("SELECT row_version FROM shipment_closing WHERE shipment_closing_id = @id FOR UPDATE", new { id }, tx)
            ?? throw new NotFoundException(Table, id);
        if (version != r.RowVersion) throw new ConcurrencyConflictException(Table, id);
        var status = await conn.ExecuteScalarAsync<string>("SELECT closing_status FROM shipment_closing WHERE shipment_closing_id = @id", new { id }, tx);
        if (status != "CLOSED") throw new BusinessRuleException("CLOSING_NOT_CLOSED", "마감 상태가 아닙니다.");
        var n = await conn.ExecuteAsync(
            """
            UPDATE shipment SET closing_status = 'UNCLOSED', shipment_closing_id = NULL, closing_year = NULL, closing_month = NULL,
                   updated_by = @UserId, row_version = row_version + 1
             WHERE shipment_closing_id = @id
            """, new { id, currentUser.UserId }, tx);
        await conn.ExecuteAsync("UPDATE shipment_closing SET closing_status = 'REOPENED', row_version = row_version + 1 WHERE shipment_closing_id = @id", new { id }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Reopen, Table, id, new { closing_status = "CLOSED" }, new { closing_status = "REOPENED", shipments = n }, r.Reason);
        await tx.CommitAsync(ct);
    }

    private static void Month(int year, int month)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12) throw new RequestValidationException("month", "마감 연·월이 올바르지 않습니다.");
    }
}
