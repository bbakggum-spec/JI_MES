using JiMes.Api.Features.Printing;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.Quality;

/// <summary>검사 API — 설계 §23.5. 성적서 발행도 검사 화면 권한으로 (§12 ⑦ — 출력 양식 관리 권한과 분리).</summary>
public static class InspectionEndpoints
{
    public const string ReportPurpose = "INSPECTION_REPORT";

    public static void MapInspectionEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/inspections").WithTags("Inspection");
        const string key = MenuKeys.QualityInspection;
        g.MapGet("/", (InspectionService s, CancellationToken ct, DateOnly? from, DateOnly? to, string? type, string? search) => s.ListAsync(from, to, type, search, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapGet("/{id:long}", (long id, InspectionService s, CancellationToken ct) => s.DetailAsync(id, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapGet("/lot", (string lotNo, InspectionService s, CancellationToken ct) => s.LotAsync(lotNo, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapGet("/criteria/{versionId:long}", (long versionId, InspectionService s, CancellationToken ct) => s.CriteriaAsync(versionId, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapPost("/", (InspectionCreateRequest r, InspectionService s, CancellationToken ct) => s.CreateAsync(r, ct))
            .RequirePermission(key, PermissionAction.Create);
        g.MapPut("/{id:long}", (long id, InspectionUpdateRequest r, InspectionService s, CancellationToken ct) => s.UpdateAsync(id, r, ct))
            .RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/complete", (long id, InspectionVersionRequest r, InspectionService s, CancellationToken ct) => s.CompleteAsync(id, r, ct))
            .RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/reinspect", (long id, InspectionVersionRequest r, InspectionService s, CancellationToken ct) => s.ReinspectAsync(id, r, ct))
            .RequirePermission(key, PermissionAction.Create);
        g.MapPost("/{id:long}/cancel", async (long id, InspectionVersionRequest r, InspectionService s, CancellationToken ct) => { await s.CancelAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Delete);
        // 성적서 = 대상(품목)별 1장 — 양식은 품목+업체 > 품목 > 용도 기본, printTemplateId 로 바꿀 수 있다
        g.MapPost("/targets/{targetId:long}/report", async (long targetId, long? printTemplateId, InspectionService s, PrintService print, CancellationToken ct) =>
            {
                await s.EnsureTargetAsync(targetId, ct);
                return PrintEndpoints.FileOf(await print.IssueAsync(new IssueRequest(ReportPurpose, targetId, printTemplateId), ct));
            })
            .RequirePermission(key, PermissionAction.Read);
    }
}
