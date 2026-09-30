using System.Text.RegularExpressions;
using Dapper;
using JiMes.Api.Infrastructure.Errors;
using MySqlConnector;

namespace JiMes.Api.Infrastructure.Data;

/// <summary>
/// row_version 낙관적 잠금 (설계 §1.3). 클라이언트는 읽을 때 받은 row_version 을 수정 요청에 그대로 돌려보낸다.
/// </summary>
public static partial class RowVersion
{
    private const string IncrementClause = "row_version = row_version + 1";

    /// <summary>
    /// 버전 확인 UPDATE 를 실행한다. SQL 은 <c>SET … row_version = row_version + 1</c> 과
    /// <c>WHERE {table}_id = … AND row_version = @RowVersion</c> 을 포함해야 한다.
    /// 0행이면 대상이 없으면 404, 있으면 409 를 던진다.
    /// </summary>
    public static async Task ExecuteVersionedUpdateAsync(
        this MySqlConnection conn, string sql, object param, string table, long id, MySqlTransaction? tx = null)
    {
        if (!sql.Contains(IncrementClause, StringComparison.OrdinalIgnoreCase)
            || !sql.Contains("@RowVersion", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"버전 확인 UPDATE 에는 '{IncrementClause}' 와 '@RowVersion' 조건이 필요합니다.", nameof(sql));
        if (!IdentifierPattern().IsMatch(table))
            throw new ArgumentException($"잘못된 테이블명: {table}", nameof(table));

        var affected = await conn.ExecuteAsync(sql, param, tx);
        if (affected == 1)
            return;
        if (affected > 1)
            throw new InvalidOperationException($"버전 확인 UPDATE 가 {affected}행을 변경했습니다 ({table} #{id}). WHERE 조건을 확인하세요.");

        var exists = await conn.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM `{table}` WHERE `{table}_id` = @id", new { id }, tx);
        throw exists == 0 ? new NotFoundException(table, id) : new ConcurrencyConflictException(table, id);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex IdentifierPattern();
}
