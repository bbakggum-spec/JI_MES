using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.Production;

/// <summary>작업(투입) API — 설계 §23.4. 조회 R, 즉시 작업 C, 투입·시작·완료·표준·조건 U.</summary>
public static class WorkEndpoints
{
    public static void MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/works").WithTags("Work");
        const string key = MenuKeys.ProductionWork;
        g.MapGet("/board", (WorkService s, CancellationToken ct, long? equipmentTypeId) => s.BoardAsync(equipmentTypeId, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapGet("/{id:long}", (long id, WorkService s, CancellationToken ct) => s.DetailAsync(id, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapGet("/{id:long}/standards", (long id, WorkService s, CancellationToken ct) => s.StandardCandidatesAsync(id, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapPost("/{id:long}/scan", (long id, ScanRequest r, WorkService s, CancellationToken ct) => s.ScanAsync(id, r, ct))
            .RequirePermission(key, PermissionAction.Read);

        g.MapPost("/", (AdHocWorkRequest r, WorkService s, CancellationToken ct) => s.CreateAdHocAsync(r, ct))
            .RequirePermission(key, PermissionAction.Create);

        g.MapPut("/{id:long}", async (long id, UpdateWorkRequest r, WorkService s, CancellationToken ct) => { await s.UpdateAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/inputs", (long id, AddInputRequest r, WorkService s, CancellationToken ct) => s.AddInputAsync(id, r, ct))
            .RequirePermission(key, PermissionAction.Update);
        g.MapPut("/{id:long}/inputs/{inputId:long}", async (long id, long inputId, UpdateInputRequest r, WorkService s, CancellationToken ct) =>
                { await s.UpdateInputAsync(id, inputId, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapDelete("/{id:long}/inputs/{inputId:long}", async (long id, long inputId, int rowVersion, WorkService s, CancellationToken ct) =>
                { await s.DeleteInputAsync(id, inputId, rowVersion, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/inputs/{inputId:long}/defects", (long id, long inputId, RegisterDefectRequest r, WorkService s, CancellationToken ct) => s.RegisterDefectAsync(id, inputId, r, ct))
            .RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/start", async (long id, StartWorkRequest r, WorkService s, CancellationToken ct) => { await s.StartAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/complete", async (long id, CompleteWorkRequest r, WorkService s, CancellationToken ct) => { await s.CompleteAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/fix-standard", async (long id, FixStandardRequest r, WorkService s, CancellationToken ct) => { await s.FixStandardAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapPut("/{id:long}/conditions", async (long id, SaveConditionsRequest r, WorkService s, CancellationToken ct) => { await s.SaveConditionsAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
    }
}
