using Dapper;
using JiMes.Api.Features.Master.Parts;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Files;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Equipment;

public sealed class CalibrationInstrumentDto
{
    public long InstrumentId { get; init; }
    public string InstrumentCode { get; init; } = "";
    public string InstrumentName { get; init; } = "";
    public string? InstrumentType { get; init; }
    public string? SerialNo { get; init; }
    public int? CalibrationCycleDay { get; init; }
    public DateTime? LastCalibratedDate { get; init; }
    public DateTime? NextCalibrationDate { get; init; }
    public long CalibrationCount { get; init; }
    public bool IsActive { get; init; }
    /// <summary>OVERDUE / DUE_SOON / NONE(다음 교정일 없음) / null</summary>
    public string? DueState { get; set; }
}

public sealed class CalibrationDto
{
    public long InstrumentCalibrationId { get; init; }
    public long InstrumentId { get; init; }
    public DateTime CalibrationDate { get; init; }
    public string Result { get; init; } = "";
    public string? AgencyName { get; init; }
    public string? CertificateNo { get; init; }
    public DateTime? NextCalibrationDate { get; init; }
    public decimal? Cost { get; init; }
    public string? Remark { get; init; }
    public long AttachmentCount { get; init; }
}

public sealed record CalibrationDetail(CalibrationDto Calibration, IReadOnlyList<AttachmentDto> Attachments);

public sealed record CalibrationSaveRequest(
    long? InstrumentId, DateOnly? CalibrationDate, string? Result, string? AgencyName, string? CertificateNo,
    DateOnly? NextCalibrationDate, decimal? Cost, string? Remark);

/// <summary>
/// 측정기구 교정 이력 (설계 §26.2 B, §28.4) — 설비 보전과 별도 관리.
/// 교정을 등록·수정·삭제하면 측정기구의 최근 교정일 = 마지막 교정일, 다음 교정일 = 그 교정의 다음 교정일(비우면 교정일 + 교정 주기).
/// 판정 = 공통코드 DECISION (PASS/FAIL/CONDITIONAL — 항목 전용 NA 제외). 교정 성적서 파일 = 공통 첨부 CALIBRATION_CERT.
/// </summary>
public sealed class CalibrationService(
    IDbConnectionFactory db, AuditWriter audit, ICurrentUser currentUser, CommonCodeCache codes, SettingsCache settings, TimeProvider time,
    AttachmentStore attachments)
{
    public const string Table = "instrument_calibration";

    private const string SelectSql =
        """
        SELECT c.instrument_calibration_id, c.instrument_id, c.calibration_date, c.result, c.agency_name, c.certificate_no,
               c.next_calibration_date, c.cost, c.remark,
               (SELECT COUNT(*) FROM attachment a WHERE a.owner_table = 'instrument_calibration' AND a.owner_id = c.instrument_calibration_id) AS attachment_count
          FROM instrument_calibration c
        """;

    /// <summary>측정기구 목록 + 교정 상태 (지남·임박 먼저)</summary>
    public async Task<IEnumerable<CalibrationInstrumentDto>> InstrumentsAsync(bool includeInactive, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<CalibrationInstrumentDto>(
            """
            SELECT i.instrument_id, i.instrument_code, i.instrument_name, i.instrument_type, i.serial_no, i.calibration_cycle_day,
                   i.last_calibrated_date, i.next_calibration_date, i.is_active,
                   (SELECT COUNT(*) FROM instrument_calibration c WHERE c.instrument_id = i.instrument_id) AS calibration_count
              FROM instrument i
             WHERE @includeInactive OR i.is_active = 1
             ORDER BY i.instrument_code
            """, new { includeInactive })).ToList();
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var soon = settings.GetInt(SettingKeys.InstrumentCalibrationDueSoonDays);
        foreach (var r in rows)
            r.DueState = r.NextCalibrationDate is null ? "NONE" : MaintenanceService.DueState(r.NextCalibrationDate, today, soon);
        return rows;
    }

    public async Task<IEnumerable<CalibrationDto>> HistoryAsync(long instrumentId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.QueryAsync<CalibrationDto>(
            SelectSql + " WHERE c.instrument_id = @instrumentId ORDER BY c.calibration_date DESC, c.instrument_calibration_id DESC", new { instrumentId });
    }

    public async Task<CalibrationDetail> GetAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var c = await conn.QuerySingleOrDefaultAsync<CalibrationDto>(SelectSql + " WHERE c.instrument_calibration_id = @id", new { id })
            ?? throw new NotFoundException(Table, id);
        return new CalibrationDetail(c, await AttachmentStore.ListAsync(conn, null, Table, id));
    }

    public async Task<long> CreateAsync(CalibrationSaveRequest r, CancellationToken ct)
    {
        Validate(r);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await LockInstrumentAsync(conn, tx, r.InstrumentId!.Value);
        var id = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO instrument_calibration (instrument_id, calibration_date, result, agency_name, certificate_no, next_calibration_date, cost, remark,
                                                created_by, updated_by)
            VALUES (@InstrumentId, @date, @Result, @agency, @certificate, @next, @Cost, @remark, @UserId, @UserId);
            SELECT LAST_INSERT_ID();
            """, Params(r, 0), tx);
        await audit.WriteAsync(conn, tx, AuditAction.Create, Table, id, null, r);
        await SyncInstrumentAsync(conn, tx, r.InstrumentId!.Value);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task UpdateAsync(long id, CalibrationSaveRequest r, CancellationToken ct)
    {
        Validate(r);
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id);
        await LockInstrumentAsync(conn, tx, r.InstrumentId!.Value);
        await conn.ExecuteAsync(
            """
            UPDATE instrument_calibration SET instrument_id = @InstrumentId, calibration_date = @date, result = @Result, agency_name = @agency,
                   certificate_no = @certificate, next_calibration_date = @next, cost = @Cost, remark = @remark, updated_by = @UserId
             WHERE instrument_calibration_id = @id
            """, Params(r, id), tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, Table, id, before, r);
        await SyncInstrumentAsync(conn, tx, r.InstrumentId!.Value);
        if (before.InstrumentId != r.InstrumentId) await SyncInstrumentAsync(conn, tx, before.InstrumentId);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await LockAsync(conn, tx, id);
        await conn.ExecuteAsync("DELETE FROM attachment WHERE owner_table = @Table AND owner_id = @id", new { Table, id }, tx);
        await conn.ExecuteAsync("DELETE FROM instrument_calibration WHERE instrument_calibration_id = @id", new { id }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Delete, Table, id, before, null);
        await SyncInstrumentAsync(conn, tx, before.InstrumentId);
        await tx.CommitAsync(ct);
    }

    public Task<long> AddAttachmentAsync(long id, string kind, string fileName, string? contentType, byte[] content, string? caption, CancellationToken ct) =>
        attachments.AddAsync(Table, id, kind, fileName, contentType, content, caption, ct);

    public Task<(byte[] Content, string FileName, string ContentType)> GetAttachmentAsync(long id, long attachmentId, CancellationToken ct) =>
        attachments.GetAsync(Table, id, attachmentId, ct);

    public Task DeleteAttachmentAsync(long id, long attachmentId, CancellationToken ct) => attachments.DeleteAsync(Table, id, attachmentId, ct);

    /// <summary>측정기구 최근·다음 교정일 = 마지막 교정 (다음 교정일이 비면 교정일 + 교정 주기). 교정 기록이 없으면 그대로 둔다</summary>
    private static async Task SyncInstrumentAsync(MySqlConnection conn, MySqlTransaction tx, long instrumentId) =>
        await conn.ExecuteAsync(
            """
            UPDATE instrument i
              JOIN (SELECT c.instrument_id, c.calibration_date, c.next_calibration_date FROM instrument_calibration c
                     WHERE c.instrument_id = @instrumentId ORDER BY c.calibration_date DESC, c.instrument_calibration_id DESC LIMIT 1) x
                ON x.instrument_id = i.instrument_id
               SET i.last_calibrated_date = x.calibration_date,
                   i.next_calibration_date = COALESCE(x.next_calibration_date,
                                                      IF(i.calibration_cycle_day IS NULL, NULL, x.calibration_date + INTERVAL i.calibration_cycle_day DAY))
            """, new { instrumentId }, tx);

    private void Validate(CalibrationSaveRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.InstrumentId is null) errors["instrumentId"] = ["측정기구를 고르세요."];
        if (r.CalibrationDate is null) errors["calibrationDate"] = ["교정일을 입력하세요."];
        var decisions = codes.GetGroup("DECISION").Codes.Where(c => c.IsActive && c.Code != "NA");
        if (decisions.All(c => c.Code != r.Result)) errors["result"] = ["판정을 고르세요."];
        if (r.CalibrationDate is { } d && r.NextCalibrationDate is { } n && n < d) errors["nextCalibrationDate"] = ["다음 교정일이 교정일보다 앞입니다."];
        if (r.Cost is < 0) errors["cost"] = ["비용은 0 이상입니다."];
        if (r.AgencyName is { Length: > 100 }) errors["agencyName"] = ["100자 이하입니다."];
        if (r.CertificateNo is { Length: > 100 }) errors["certificateNo"] = ["100자 이하입니다."];
        if (r.Remark is { Length: > 500 }) errors["remark"] = ["500자 이하입니다."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    private object Params(CalibrationSaveRequest r, long id) => new
    {
        id, r.InstrumentId, date = r.CalibrationDate!.Value.ToDateTime(TimeOnly.MinValue), r.Result, agency = Trim(r.AgencyName),
        certificate = Trim(r.CertificateNo), next = r.NextCalibrationDate?.ToDateTime(TimeOnly.MinValue), r.Cost, remark = Trim(r.Remark),
        currentUser.UserId,
    };

    private static async Task LockInstrumentAsync(MySqlConnection conn, MySqlTransaction tx, long instrumentId)
    {
        if (await conn.ExecuteScalarAsync<long?>("SELECT instrument_id FROM instrument WHERE instrument_id = @instrumentId FOR UPDATE", new { instrumentId }, tx) is null)
            throw new RequestValidationException("instrumentId", "측정기구가 없습니다.");
    }

    private static async Task<CalibrationDto> LockAsync(MySqlConnection conn, MySqlTransaction tx, long id) =>
        await conn.QuerySingleOrDefaultAsync<CalibrationDto>(SelectSql + " WHERE c.instrument_calibration_id = @id FOR UPDATE", new { id }, tx)
        ?? throw new NotFoundException(Table, id);

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
