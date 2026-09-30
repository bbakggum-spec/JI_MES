using System.Text.Json;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;

namespace JiMes.Api.Features.Master;

public sealed record MasterMeta(
    string Key, string Label, string MenuKey, string Description, bool HasActive, bool AllowDelete,
    string[]? RequireOneOf, IReadOnlyList<MasterField> Fields, IReadOnlyList<MasterImageField> Images);

/// <summary>
/// 단순 기준정보 API — 정의마다 /api/master/{key} 경로를 따로 등록해 메뉴 권한(master.*)을 엔드포인트에 고정한다
/// (경로 변수로 권한을 바꾸지 않음 → fail-closed 선언 검사 대상).
/// 선택 목록(options)은 다른 화면의 드롭다운용이라 로그인만 요구 (id·표시명만 노출).
/// </summary>
public static class MasterEndpoints
{
    public static void MapMasterEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/master", () => Results.Ok(MasterCatalog.All.Select(e => new { e.Key, e.Label, e.MenuKey })))
            .WithTags("Master").RequireLogin();

        foreach (var entity in MasterCatalog.All)
        {
            entity.Validate();
            var e = entity;
            var g = app.MapGroup($"/api/master/{e.Key}").WithTags("Master");

            g.MapGet("/meta", () => Results.Ok(new MasterMeta(e.Key, e.Label, e.MenuKey, e.Description, e.HasActive, e.AllowDelete,
                    e.RequireOneOf, e.Fields, e.Images)))
                .RequirePermission(e.MenuKey, PermissionAction.Read);
            g.MapGet("/", async (MasterService s, CancellationToken ct, string? search, bool includeInactive = false, int page = 1, int pageSize = 100) =>
                    Results.Ok(await s.ListAsync(e, search, includeInactive, page, pageSize, ct)))
                .RequirePermission(e.MenuKey, PermissionAction.Read);
            g.MapGet("/options", async (MasterService s, CancellationToken ct) => Results.Ok(await s.OptionsAsync(e, ct)))
                .RequireLogin();
            g.MapGet("/{id:long}", async (long id, MasterService s, CancellationToken ct) => Results.Ok(await s.GetAsync(e, id, ct)))
                .RequirePermission(e.MenuKey, PermissionAction.Read);
            g.MapPost("/", async (Dictionary<string, JsonElement> body, MasterService s, CancellationToken ct) =>
                    Results.Ok(new { id = await s.CreateAsync(e, body, ct) }))
                .RequirePermission(e.MenuKey, PermissionAction.Create);
            g.MapPut("/{id:long}", async (long id, Dictionary<string, JsonElement> body, MasterService s, CancellationToken ct) =>
                {
                    var reason = body.Remove("__reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                    await s.UpdateAsync(e, id, body, reason, ct);
                    return Results.NoContent();
                })
                .RequirePermission(e.MenuKey, PermissionAction.Update);
            if (e.AllowDelete)
            {
                g.MapDelete("/{id:long}", async (long id, MasterService s, CancellationToken ct) =>
                    { await s.DeleteAsync(e, id, null, ct); return Results.NoContent(); })
                    .RequirePermission(e.MenuKey, PermissionAction.Delete);
            }
            foreach (var image in e.Images)
            {
                var name = image.Name;
                g.MapGet($"/{{id:long}}/image/{name}", async (long id, MasterService s, CancellationToken ct) =>
                        await s.GetImageAsync(e, id, name, ct) is { } img
                            ? Results.File(img.Content, img.Content[0] == 0x89 ? "image/png" : "image/jpeg", img.FileName)
                            : throw new NotFoundException($"{e.Table}.{name}", id))
                    .RequirePermission(e.MenuKey, PermissionAction.Read);
                g.MapPut($"/{{id:long}}/image/{name}", async (long id, IFormFile file, MasterService s, CancellationToken ct) =>
                    {
                        using var buffer = new MemoryStream();
                        await file.CopyToAsync(buffer, ct);
                        await s.SetImageAsync(e, id, name, buffer.ToArray(), file.FileName, ct);
                        return Results.NoContent();
                    })
                    .RequirePermission(e.MenuKey, PermissionAction.Update)
                    .DisableAntiforgery();   // SameSite=Strict 쿠키로 CSRF 방지
                g.MapDelete($"/{{id:long}}/image/{name}", async (long id, MasterService s, CancellationToken ct) =>
                    { await s.SetImageAsync(e, id, name, null, null, ct); return Results.NoContent(); })
                    .RequirePermission(e.MenuKey, PermissionAction.Update);
            }
        }
    }
}
