using JiMes.Api.Features.Printing;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.Sales;

/// <summary>출하·마감 API — 설계 §23.7. 거래명세표 발행은 출하 화면 권한으로 (§12 ⑦).</summary>
public static class ShipmentEndpoints
{
    public const string SlipPurpose = "SHIPMENT_SLIP";

    public static void MapShipmentEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/shipments").WithTags("Shipment");
        const string key = MenuKeys.SalesShipment;
        g.MapGet("/", (ShipmentService s, CancellationToken ct, DateOnly? from, DateOnly? to, long? customerId, string? search, bool includeCancelled = false) =>
                s.ListAsync(from, to, customerId, search, includeCancelled, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapGet("/{id:long}", (long id, ShipmentService s, CancellationToken ct) => s.DetailAsync(id, ct)).RequirePermission(key, PermissionAction.Read);
        g.MapGet("/stock", (long customerId, ShipmentService s, CancellationToken ct, long? excludeShipmentId, bool includeZero = false) =>
                s.StockAsync(customerId, excludeShipmentId, includeZero, ct))
            .RequirePermission(key, PermissionAction.Read);
        g.MapPost("/", (ShipmentSaveRequest r, ShipmentService s, CancellationToken ct) => s.CreateAsync(r, ct)).RequirePermission(key, PermissionAction.Create);
        g.MapPut("/{id:long}", (long id, ShipmentSaveRequest r, ShipmentService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequirePermission(key, PermissionAction.Update);
        g.MapPost("/{id:long}/cancel", async (long id, ShipmentVersionRequest r, ShipmentService s, CancellationToken ct) => { await s.CancelAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(key, PermissionAction.Delete);
        // 거래명세표 양식 선택 콤보 (기본 = 당사 FIXED SALES_SLIP, 그 외 = 등록한 엑셀 양식) — §12 ⑤
        g.MapGet("/slip-templates", (PrintService print, CancellationToken ct) => print.ChoicesAsync(SlipPurpose, ct))
            .RequirePermission(key, PermissionAction.Read);
        // 거래명세표 (FIXED SALES_SLIP 기본, 업체·용도 양식으로 바꿀 수 있음)
        g.MapPost("/{id:long}/slip", async (long id, long? printTemplateId, ShipmentService s, PrintService print, CancellationToken ct) =>
            {
                await s.EnsureExistsAsync(id, ct);
                return PrintEndpoints.FileOf(await print.IssueAsync(new IssueRequest(SlipPurpose, id, printTemplateId), ct));
            })
            .RequirePermission(key, PermissionAction.Read);

        var c = app.MapGroup("/api/closings").WithTags("Closing");
        const string closingKey = MenuKeys.SalesClosing;
        c.MapGet("/customers", (int year, int month, ClosingService s, CancellationToken ct) => s.CustomersAsync(year, month, ct))
            .RequirePermission(closingKey, PermissionAction.Read);
        c.MapGet("/candidates", (long customerId, int year, int month, ClosingService s, CancellationToken ct) => s.CandidatesAsync(customerId, year, month, ct))
            .RequirePermission(closingKey, PermissionAction.Read);
        c.MapGet("/", (int year, int month, ClosingService s, CancellationToken ct, long? customerId) => s.ListAsync(year, month, customerId, ct))
            .RequirePermission(closingKey, PermissionAction.Read);
        c.MapGet("/{id:long}", (long id, ClosingService s, CancellationToken ct) => s.DetailAsync(id, ct)).RequirePermission(closingKey, PermissionAction.Read);
        c.MapPost("/", (ClosingCreateRequest r, ClosingService s, CancellationToken ct) => s.CloseAsync(r, ct)).RequirePermission(closingKey, PermissionAction.Create);
        c.MapPost("/carry-over", (CarryOverRequest r, ClosingService s, CancellationToken ct) => s.CarryOverAsync(r, ct)).RequirePermission(closingKey, PermissionAction.Update);
        c.MapPost("/{id:long}/reopen", async (long id, ClosingVersionRequest r, ClosingService s, CancellationToken ct) => { await s.ReopenAsync(id, r, ct); return Results.NoContent(); })
            .RequirePermission(closingKey, PermissionAction.Delete);
    }
}
