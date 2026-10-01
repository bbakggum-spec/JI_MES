using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.Reports;

/// <summary>조회 API — 설계 §23(7단계) §24.</summary>
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/reports").WithTags("Report");
        const string lotKey = MenuKeys.ReportLot;
        g.MapGet("/lots", (LotReportService s, CancellationToken ct, DateOnly? from, DateOnly? to, long? equipmentId, long? unitProcessId, string? status,
                bool mainOnly = false, string? search = null) => s.LotsAsync(from, to, equipmentId, unitProcessId, status, mainOnly, search, ct))
            .RequirePermission(lotKey, PermissionAction.Read);
        g.MapGet("/trace/resolve", (string code, LotReportService s, CancellationToken ct) => s.ResolveAsync(code, ct))
            .RequirePermission(lotKey, PermissionAction.Read);
        g.MapGet("/trace/{mainWorkId:long}", (long mainWorkId, LotReportService s, CancellationToken ct) => s.TraceAsync(mainWorkId, ct))
            .RequirePermission(lotKey, PermissionAction.Read);
        g.MapGet("/orders", (OrderReportService s, CancellationToken ct, DateOnly? from, DateOnly? to, long? customerId, string? search, string? view) =>
                s.OrdersAsync(from, to, customerId, search, view, ct))
            .RequirePermission(MenuKeys.ReportOrder, PermissionAction.Read);

        // 대시보드 — 패널마다 업무 메뉴 읽기 권한 (영업·추이는 권한 있는 부분만 채움)
        var d = app.MapGroup("/api/dashboard").WithTags("Dashboard");
        d.MapGet("/equipment", (DashboardService s, CancellationToken ct) => s.EquipmentAsync(ct)).RequirePermission(MenuKeys.ProductionWork, PermissionAction.Read);
        d.MapGet("/quality", (DashboardService s, CancellationToken ct) => s.QualityAsync(ct)).RequirePermission(MenuKeys.QualityDefect, PermissionAction.Read);
        d.MapGet("/sales", (DashboardService s, CancellationToken ct) => s.SalesAsync(ct)).RequireLogin();
        d.MapGet("/trend", (DashboardService s, CancellationToken ct, int? days) => s.TrendAsync(days, ct)).RequireLogin();
    }
}
