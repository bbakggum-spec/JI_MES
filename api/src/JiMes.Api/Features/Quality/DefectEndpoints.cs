using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.Quality;

/// <summary>부적합 API — 설계 §23.6. 등록은 검사 확정(불합격)과 작업 화면(투입 행 불량)에서, 여기서는 판정·완료·취소·재작업.</summary>
public static class DefectEndpoints
{
    public static void MapDefectEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/defects").WithTags("Defect");
        const string key = MenuKeys.QualityDefect;
        g.MapGet("/", (DefectService s, CancellationToken ct, DateOnly? from, DateOnly? to, string? status, string? decision, string? search, bool openOnly = false) =>
                s.ListAsync(from, to, status, decision, search, openOnly, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapGet("/{id:long}", (long id, DefectService s, CancellationToken ct) => s.GetAsync(id, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapPut("/{id:long}/decide", async (long id, DefectDecideRequest r, DefectService s, CancellationToken ct) => { await s.DecideAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/complete", async (long id, DefectCompleteRequest r, DefectService s, CancellationToken ct) => { await s.CompleteAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/cancel", async (long id, DefectCancelRequest r, DefectService s, CancellationToken ct) => { await s.CancelAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Delete);
        g.MapPost("/{id:long}/rework", (long id, DefectReworkRequest r, DefectService s, CancellationToken ct) => s.ReworkAsync(id, r, ct))
            .RequirePermission(key, PermissionAction.Update);
    }
}
