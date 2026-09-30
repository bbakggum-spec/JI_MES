using Dapper;
using JiMes.Api.Infrastructure.Data;

namespace JiMes.Api.Features.Health;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (IDbConnectionFactory db, ILoggerFactory loggers, CancellationToken ct) =>
        {
            try
            {
                await using var conn = await db.OpenAsync(ct);
                await conn.ExecuteScalarAsync<int>("SELECT 1");
                return Results.Ok(new { status = "ok", database = "ok" });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                loggers.CreateLogger("Health").LogError(ex, "DB 연결 실패");
                return Results.Json(new { status = "degraded", database = "unavailable" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithTags("Health").AllowAnonymous();
    }
}
