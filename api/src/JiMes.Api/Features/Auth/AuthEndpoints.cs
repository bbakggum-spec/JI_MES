using System.Security.Claims;
using Dapper;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace JiMes.Api.Features.Auth;

public sealed record LoginRequest(string? LoginId, string? Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record MenuPermissionDto(bool Read, bool Create, bool Update, bool Delete);
public sealed record MenuNodeDto(string MenuKey, string MenuName, string? Route, IReadOnlyList<MenuNodeDto> Children);
public sealed record MeResponse(
    long AppUserId, string LoginId, string UserName, IReadOnlyList<string> Roles,
    IReadOnlyList<MenuNodeDto> Menus, IReadOnlyDictionary<string, MenuPermissionDto> Permissions);

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    private sealed class LoginUserRow
    {
        public long AppUserId { get; init; }
        public string LoginId { get; init; } = "";
        public string PasswordHash { get; init; } = "";
        public string UserName { get; init; } = "";
        public bool IsActive { get; init; }
    }

    private sealed class MenuRow
    {
        public long MenuId { get; init; }
        public string MenuKey { get; init; } = "";
        public string MenuName { get; init; } = "";
        public long? ParentMenuId { get; init; }
        public string? Route { get; init; }
    }

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy);
        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireLogin();
        group.MapGet("/me", async (HttpContext http, IDbConnectionFactory db, PermissionService permissions, CancellationToken ct) =>
            Results.Ok(await BuildMeAsync(http.User.GetUserId()!.Value, db, permissions, ct))).RequireLogin();
        group.MapPost("/change-password", ChangePasswordAsync).RequireLogin();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, HttpContext http, IDbConnectionFactory db, PasswordService passwords,
        PermissionService permissions, SettingsCache settings, TimeProvider time, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.LoginId) || string.IsNullOrEmpty(request.Password))
            throw new RequestValidationException(nameof(request.LoginId), "아이디와 비밀번호를 입력하세요.");

        await using var conn = await db.OpenAsync(ct);
        var user = await conn.QuerySingleOrDefaultAsync<LoginUserRow>(
            "SELECT app_user_id, login_id, password_hash, user_name, is_active FROM app_user WHERE login_id = @loginId",
            new { loginId = request.LoginId.Trim() });

        var (ok, needsRehash) = passwords.Verify(user?.PasswordHash, request.Password);
        if (user is null || !ok || !user.IsActive)
        {
            // 계정 없음·비밀번호 틀림·비활성을 구분하지 않는다 (계정 추측 방지)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "아이디 또는 비밀번호가 올바르지 않습니다.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_CREDENTIALS" });
        }

        await conn.ExecuteAsync(
            needsRehash
                ? "UPDATE app_user SET last_login_at = NOW(), password_hash = @hash WHERE app_user_id = @id"
                : "UPDATE app_user SET last_login_at = NOW() WHERE app_user_id = @id",
            new { id = user.AppUserId, hash = needsRehash ? passwords.Hash(request.Password) : null });

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.AppUserId.ToString()),
                new Claim(ClaimTypes.Name, user.LoginId),
                new Claim(AppClaimTypes.UserName, user.UserName),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        // 세션 쿠키(브라우저 종료 시 삭제) + 서버 측 만료 = auth.session_timeout_min, 요청마다 연장(sliding)
        var now = time.GetUtcNow();
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false,
                IssuedUtc = now,
                ExpiresUtc = now.AddMinutes(settings.GetInt(SettingKeys.AuthSessionTimeoutMin)),
            });

        return Results.Ok(await BuildMeAsync(user.AppUserId, db, permissions, ct));
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request, HttpContext http, IDbConnectionFactory db, PasswordService passwords,
        AuditWriter audit, CancellationToken ct)
    {
        var userId = http.User.GetUserId()!.Value;
        if (string.IsNullOrEmpty(request.CurrentPassword))
            throw new RequestValidationException(nameof(request.CurrentPassword), "현재 비밀번호를 입력하세요.");
        passwords.EnsurePolicy(nameof(request.NewPassword), request.NewPassword);

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var hash = await conn.ExecuteScalarAsync<string?>(
            "SELECT password_hash FROM app_user WHERE app_user_id = @userId FOR UPDATE", new { userId }, tx);
        if (!passwords.Verify(hash, request.CurrentPassword).Ok)
            throw new RequestValidationException(nameof(request.CurrentPassword), "현재 비밀번호가 올바르지 않습니다.");

        await conn.ExecuteAsync("UPDATE app_user SET password_hash = @hash WHERE app_user_id = @userId",
            new { userId, hash = passwords.Hash(request.NewPassword!) }, tx);
        await audit.WriteAsync(conn, tx, AuditAction.Update, "app_user", userId, null,
            new { password_changed = true }, "비밀번호 변경");
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    /// <summary>내 정보 + 읽기 권한이 있는 메뉴 트리 + 메뉴별 권한 (웹 사이드바·버튼 표시용).</summary>
    private static async Task<MeResponse> BuildMeAsync(
        long userId, IDbConnectionFactory db, PermissionService permissions, CancellationToken ct)
    {
        var access = await permissions.GetAsync(userId, ct);

        await using var conn = await db.OpenAsync(ct);
        var user = await conn.QuerySingleAsync<(string LoginId, string UserName)>(
            "SELECT login_id, user_name FROM app_user WHERE app_user_id = @userId", new { userId });
        var menus = (await conn.QueryAsync<MenuRow>(
            "SELECT menu_id, menu_key, menu_name, parent_menu_id, route FROM menu WHERE is_active = 1 ORDER BY sort_order, menu_id"))
            .ToList();

        var children = menus.ToLookup(m => m.ParentMenuId);

        // 읽기 권한이 있는 메뉴와 그 상위 메뉴만 남긴다
        List<MenuNodeDto> Build(long? parentId) =>
            children[parentId]
                .Select(m => (m, kids: Build(m.MenuId)))
                .Where(x => x.kids.Count > 0 || access.Has(x.m.MenuKey, PermissionAction.Read))
                .Select(x => new MenuNodeDto(x.m.MenuKey, x.m.MenuName, x.m.Route, x.kids))
                .ToList();

        var perms = access.Menus.ToDictionary(
            kv => kv.Key,
            kv => new MenuPermissionDto(
                kv.Value.HasFlag(PermissionAction.Read), kv.Value.HasFlag(PermissionAction.Create),
                kv.Value.HasFlag(PermissionAction.Update), kv.Value.HasFlag(PermissionAction.Delete)));

        return new MeResponse(userId, user.LoginId, user.UserName, access.RoleCodes, Build(null), perms);
    }
}
