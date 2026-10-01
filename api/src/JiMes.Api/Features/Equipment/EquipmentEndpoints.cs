using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.Equipment;

/// <summary>설비 영역 API — 설계 §28 (9단계). 비가동(①)</summary>
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
    }
}
