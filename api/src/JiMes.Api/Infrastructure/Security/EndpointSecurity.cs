using Microsoft.AspNetCore.Authorization;

namespace JiMes.Api.Infrastructure.Security;

public sealed record PermissionRequirement(string MenuKey, PermissionAction Action) : IAuthorizationRequirement;

/// <summary>엔드포인트가 요구하는 메뉴 권한 (선언 누락 검사·문서화용 메타데이터).</summary>
public sealed record PermissionMetadata(string MenuKey, PermissionAction Action);

/// <summary>로그인만 요구하는 엔드포인트임을 명시 (예: 내 정보, 공통코드 조회).</summary>
public sealed class LoginOnlyMetadata;

public sealed class PermissionHandler(PermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.GetUserId() is not { } userId)
            return;
        var access = await permissions.GetAsync(userId);
        if (access.Has(requirement.MenuKey, requirement.Action))
            context.Succeed(requirement);
    }
}

/// <summary>
/// 모든 /api 엔드포인트는 셋 중 하나를 반드시 선언한다 — <see cref="RequirePermission{TBuilder}"/>,
/// <see cref="RequireLogin{TBuilder}"/>, AllowAnonymous. 선언이 없어도 기본 정책(로그인 필수)으로 막히며,
/// 선언 누락은 테스트(EndpointSecurityTests)가 잡는다 (fail-closed, 설계 §17 권한).
/// </summary>
public static class EndpointSecurityExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string menuKey, PermissionAction action)
        where TBuilder : IEndpointConventionBuilder =>
        builder
            .RequireAuthorization(p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(menuKey, action)))
            .WithMetadata(new PermissionMetadata(menuKey, action));

    public static TBuilder RequireLogin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization().WithMetadata(new LoginOnlyMetadata());
}
