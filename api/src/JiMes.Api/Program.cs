using System.Threading.RateLimiting;
using JiMes.Api.Features.AuditLogs;
using JiMes.Api.Features.Auth;
using JiMes.Api.Features.CommonCodes;
using JiMes.Api.Features.Health;
using JiMes.Api.Features.Settings;
using JiMes.Api.Infrastructure.Audit;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Realtime;
using JiMes.Api.Infrastructure.Security;
using JiMes.Api.Infrastructure.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

DapperConfig.Configure();

// 인프라
services.AddSingleton(TimeProvider.System);
services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
services.AddSingleton<SettingsCache>();
services.AddSingleton<CommonCodeCache>();
services.AddMemoryCache();
services.AddHttpContextAccessor();
services.AddScoped<ICurrentUser, HttpCurrentUser>();
services.AddScoped<AuditWriter>();
services.AddSingleton<EventPublisher>();
services.AddSignalR();
services.AddProblemDetails();
services.AddExceptionHandler<AppExceptionHandler>();
services.AddOpenApi();

// 보안
services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.Section));
services.AddSingleton<PasswordService>();
services.AddSingleton<PermissionService>();
services.AddScoped<AdminBootstrapper>();
services.AddScoped<IAuthorizationHandler, PermissionHandler>();

services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "jimes.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;   // 다른 사이트에서 온 요청에는 쿠키가 실리지 않음 (CSRF 방지)
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.SlidingExpiration = true;                 // 만료 시간은 로그인 시 auth.session_timeout_min 으로 지정
        // API 이므로 로그인 페이지로 보내지 않고 상태 코드만
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        // 비활성·삭제된 계정은 기존 쿠키도 거부 (권한 캐시 주기 안에 반영)
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var userId = ctx.Principal?.GetUserId();
            var permissions = ctx.HttpContext.RequestServices.GetRequiredService<PermissionService>();
            if (userId is null || !(await permissions.GetAsync(userId.Value)).IsActive)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

services.AddAuthorization(o =>
{
    // 선언이 빠진 엔드포인트도 로그인 없이는 열리지 않게 (fail-closed)
    o.FallbackPolicy = o.DefaultPolicy;
});

services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<SettingsCache>().GetInt(SettingKeys.AuthLoginMaxAttemptsPerMin),
            Window = TimeSpan.FromMinutes(1),   // 설정 이름의 "1분" 단위
        }));
});

services.AddScoped<SettingChangeService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapSettingEndpoints();
app.MapCommonCodeEndpoints();
app.MapAuditLogEndpoints();
app.MapHub<EventsHub>(EventsHub.Route).RequireLogin();

// 시작 시 1회: 설정·공통코드 캐시 적재 → 최초 관리자 (사용자가 없을 때만)
await app.Services.GetRequiredService<SettingsCache>().ReloadAsync();
await app.Services.GetRequiredService<CommonCodeCache>().ReloadAsync();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().RunAsync();

app.Run();

public partial class Program;
