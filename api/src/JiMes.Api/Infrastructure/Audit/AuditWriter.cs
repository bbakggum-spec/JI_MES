using System.Text.Encodings.Web;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Infrastructure.Audit;

/// <summary>audit_log.action_type CHECK 제약과 같은 값.</summary>
public static class AuditAction
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string StatusChange = "STATUS_CHANGE";
    public const string Close = "CLOSE";
    public const string Reopen = "REOPEN";
}

/// <summary>
/// 변경 감사 기록. 변경과 <b>같은 트랜잭션</b>에서 호출한다 (변경은 됐는데 이력이 빠지는 일이 없도록).
/// before/after 는 snake_case JSON 으로 저장해 컬럼명과 맞춘다. 비밀번호 해시 등 민감값은 넘기지 않는다.
/// </summary>
public sealed class AuditWriter(ICurrentUser currentUser)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 한글을 \uXXXX 로 바꾸지 않음
    };

    public Task WriteAsync(
        MySqlConnection conn, MySqlTransaction tx, string action, string table, long recordId,
        object? before, object? after, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(tx);
        return conn.ExecuteAsync(
            """
            INSERT INTO audit_log (app_user_id, action_type, table_name, record_id, before_json, after_json, reason, client_ip)
            VALUES (@UserId, @Action, @Table, @RecordId, @Before, @After, @Reason, @ClientIp)
            """,
            new
            {
                currentUser.UserId,
                Action = action,
                Table = table,
                RecordId = recordId,
                Before = Serialize(before),
                After = Serialize(after),
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                currentUser.ClientIp,
            },
            tx);
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
}
