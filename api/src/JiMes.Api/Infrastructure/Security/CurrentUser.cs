using System.Security.Claims;

namespace JiMes.Api.Infrastructure.Security;

public static class AppClaimTypes
{
    public const string UserName = "user_name";
}

public interface ICurrentUser
{
    /// <summary>로그인 사용자 app_user_id. 비로그인(부트스트랩 등)은 null.</summary>
    long? UserId { get; }
    string? ClientIp { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public long? UserId => accessor.HttpContext?.User.GetUserId();
    public string? ClientIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

public static class ClaimsPrincipalExtensions
{
    public static long? GetUserId(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
