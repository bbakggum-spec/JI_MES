using JiMes.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace JiMes.Api.Features.Equipment;

/// <summary>설비 영역 API — 설계 §28 (9단계). 비가동(①), 설비 보전·측정기구 교정(②)</summary>
public static class EquipmentEndpoints
{
    public static void MapEquipmentEndpoints(this IEndpointRouteBuilder app)
    {
        var d = app.MapGroup("/api/downtimes").WithTags("Downtime");
        const string downtime = MenuKeys.EquipmentDowntime;
        d.MapGet("/", (DateOnly from, DateOnly to, DowntimeService s, CancellationToken ct, long? equipmentId, bool? planned) =>
                s.ListAsync(from, to, equipmentId, planned, ct))
            .RequirePermission(downtime, PermissionAction.Read);
        d.MapPost("/", async (DowntimeSaveRequest r, DowntimeService s, CancellationToken ct) => Results.Ok(new { id = await s.CreateAsync(r, ct) }))
            .RequirePermission(downtime, PermissionAction.Create);
        d.MapPut("/{id:long}", async (long id, DowntimeSaveRequest r, DowntimeService s, CancellationToken ct) => { await s.UpdateAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(downtime, PermissionAction.Update);
        d.MapPost("/{id:long}/end", async (long id, DowntimeEndRequest r, DowntimeService s, CancellationToken ct) => { await s.EndAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(downtime, PermissionAction.Update);
        d.MapDelete("/{id:long}", async (long id, DowntimeService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); })
            .RequirePermission(downtime, PermissionAction.Delete);

        var m = app.MapGroup("/api/maintenances").WithTags("Maintenance");
        const string maintenance = MenuKeys.EquipmentMaintenance;
        m.MapGet("/", (DateOnly from, DateOnly to, MaintenanceService s, CancellationToken ct, long? equipmentId, string? status, string? search) =>
                s.ListAsync(from, to, equipmentId, status, search, ct))
            .RequirePermission(maintenance, PermissionAction.Read);
        m.MapGet("/due", (MaintenanceService s, CancellationToken ct) => s.DueAsync(ct)).RequirePermission(maintenance, PermissionAction.Read);
        m.MapGet("/{id:long}", (long id, MaintenanceService s, CancellationToken ct) => s.GetAsync(id, ct)).RequirePermission(maintenance, PermissionAction.Read);
        m.MapPost("/", async (MaintenanceSaveRequest r, MaintenanceService s, CancellationToken ct) => Results.Ok(new { id = await s.CreateAsync(r, ct) }))
            .RequirePermission(maintenance, PermissionAction.Create);
        m.MapPut("/{id:long}", async (long id, MaintenanceSaveRequest r, MaintenanceService s, CancellationToken ct) => { await s.UpdateAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(maintenance, PermissionAction.Update);
        m.MapDelete("/{id:long}", async (long id, int rowVersion, MaintenanceService s, CancellationToken ct) => { await s.DeleteAsync(id, rowVersion, ct); return Results.NoContent(); })
            .RequirePermission(maintenance, PermissionAction.Delete);
        MapAttachments(m, maintenance,
            (s, id, kind, name, type, content, caption, ct) => s.GetRequiredService<MaintenanceService>().AddAttachmentAsync(id, kind, name, type, content, caption, ct),
            (s, id, a, ct) => s.GetRequiredService<MaintenanceService>().GetAttachmentAsync(id, a, ct),
            (s, id, a, ct) => s.GetRequiredService<MaintenanceService>().DeleteAttachmentAsync(id, a, ct));

        var w = app.MapGroup("/api/worker-assignments").WithTags("WorkerAssignment");
        const string assignment = MenuKeys.EquipmentWorkerAssignment;
        w.MapGet("/board", (DateOnly workDate, WorkerAssignmentService s, CancellationToken ct) => s.BoardAsync(workDate, ct))
            .RequirePermission(assignment, PermissionAction.Read);
        w.MapPost("/", async (AssignmentCreateRequest r, WorkerAssignmentService s, CancellationToken ct) => Results.Ok(new { id = await s.CreateAsync(r, ct) }))
            .RequirePermission(assignment, PermissionAction.Create);
        w.MapPut("/{id:long}", async (long id, AssignmentUpdateRequest r, WorkerAssignmentService s, CancellationToken ct) => { await s.UpdateAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(assignment, PermissionAction.Update);
        w.MapDelete("/{id:long}", async (long id, WorkerAssignmentService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); })
            .RequirePermission(assignment, PermissionAction.Delete);
        w.MapPost("/copy", async (AssignmentCopyRequest r, WorkerAssignmentService s, CancellationToken ct) => Results.Ok(new { copied = await s.CopyAsync(r, ct) }))
            .RequirePermission(assignment, PermissionAction.Create);

        var c = app.MapGroup("/api/calibrations").WithTags("Calibration");
        const string calibration = MenuKeys.QualityCalibration;
        c.MapGet("/instruments", (CalibrationService s, CancellationToken ct, bool includeInactive = false) => s.InstrumentsAsync(includeInactive, ct))
            .RequirePermission(calibration, PermissionAction.Read);
        c.MapGet("/", (long instrumentId, CalibrationService s, CancellationToken ct) => s.HistoryAsync(instrumentId, ct))
            .RequirePermission(calibration, PermissionAction.Read);
        c.MapGet("/{id:long}", (long id, CalibrationService s, CancellationToken ct) => s.GetAsync(id, ct)).RequirePermission(calibration, PermissionAction.Read);
        c.MapPost("/", async (CalibrationSaveRequest r, CalibrationService s, CancellationToken ct) => Results.Ok(new { id = await s.CreateAsync(r, ct) }))
            .RequirePermission(calibration, PermissionAction.Create);
        c.MapPut("/{id:long}", async (long id, CalibrationSaveRequest r, CalibrationService s, CancellationToken ct) => { await s.UpdateAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(calibration, PermissionAction.Update);
        c.MapDelete("/{id:long}", async (long id, CalibrationService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); })
            .RequirePermission(calibration, PermissionAction.Delete);
        MapAttachments(c, calibration,
            (s, id, kind, name, type, content, caption, ct) => s.GetRequiredService<CalibrationService>().AddAttachmentAsync(id, kind, name, type, content, caption, ct),
            (s, id, a, ct) => s.GetRequiredService<CalibrationService>().GetAttachmentAsync(id, a, ct),
            (s, id, a, ct) => s.GetRequiredService<CalibrationService>().DeleteAttachmentAsync(id, a, ct));
    }

    private delegate Task<long> AddFile(IServiceProvider s, long id, string kind, string fileName, string? contentType, byte[] content, string? caption, CancellationToken ct);
    private delegate Task<(byte[] Content, string FileName, string ContentType)> GetFile(IServiceProvider s, long id, long attachmentId, CancellationToken ct);
    private delegate Task DeleteFile(IServiceProvider s, long id, long attachmentId, CancellationToken ct);

    /// <summary>공통 첨부 3종 (올리기 = 수정 권한, 받기 = 읽기 권한, 지우기 = 수정 권한) — 품목 첨부와 같은 방식</summary>
    private static void MapAttachments(RouteGroupBuilder g, string key, AddFile add, GetFile get, DeleteFile delete)
    {
        g.MapPost("/{id:long}/attachments", async (long id, IFormFile file, [FromForm] string kind, [FromForm] string? caption, HttpContext http, CancellationToken ct) =>
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                return Results.Ok(new { attachmentId = await add(http.RequestServices, id, kind, file.FileName, file.ContentType, buffer.ToArray(), caption, ct) });
            })
            .RequirePermission(key, PermissionAction.Update)
            .DisableAntiforgery();   // SameSite=Strict 쿠키로 CSRF 방지
        g.MapGet("/{id:long}/attachments/{attachmentId:long}", async (long id, long attachmentId, HttpContext http, CancellationToken ct) =>
            {
                var (content, fileName, contentType) = await get(http.RequestServices, id, attachmentId, ct);
                return Results.File(content, contentType, fileName, enableRangeProcessing: true);
            })
            .RequirePermission(key, PermissionAction.Read);
        g.MapDelete("/{id:long}/attachments/{attachmentId:long}", async (long id, long attachmentId, HttpContext http, CancellationToken ct) =>
            { await delete(http.RequestServices, id, attachmentId, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
    }
}
