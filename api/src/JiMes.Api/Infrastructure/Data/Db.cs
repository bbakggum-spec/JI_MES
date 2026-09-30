using Dapper;
using MySqlConnector;

namespace JiMes.Api.Infrastructure.Data;

public interface IDbConnectionFactory
{
    Task<MySqlConnection> OpenAsync(CancellationToken ct = default);
}

/// <summary>접속 문자열은 서버 설정(환경변수·비밀 저장소)의 ConnectionStrings:Main 에서만 읽는다 (설계 §15.4).</summary>
public sealed class MySqlConnectionFactory(IConfiguration configuration) : IDbConnectionFactory
{
    public const string ConnectionName = "Main";

    private readonly string _connectionString = configuration.GetConnectionString(ConnectionName) is { Length: > 0 } cs
        ? cs
        : throw new InvalidOperationException(
            $"ConnectionStrings:{ConnectionName} 이 설정되지 않았습니다. 환경변수 ConnectionStrings__{ConnectionName} 로 지정하세요.");

    public async Task<MySqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

public static class DapperConfig
{
    /// <summary>snake_case 컬럼 → PascalCase 속성.</summary>
    public static void Configure() => DefaultTypeMap.MatchNamesWithUnderscores = true;
}
