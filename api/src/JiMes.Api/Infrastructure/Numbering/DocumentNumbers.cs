using System.Text.RegularExpressions;
using Dapper;
using JiMes.Api.Infrastructure.Errors;
using MySqlConnector;

namespace JiMes.Api.Infrastructure.Numbering;

/// <summary>
/// 업무 번호(입고번호·출하 전표번호·검사번호 …) 부여. 형식은 설정값(<see cref="NumberFormat"/>), 순번은 같은 접두(형식에서 {SEQ} 앞뒤가
/// 같은 번호) 중 다음 번호. 동시에 등록해도 같은 번호가 나오지 않게 이름 잠금(GET_LOCK)으로 직렬화한다 —
/// 조회는 잠금 읽기(최신 커밋 기준 — 트랜잭션 스냅숏이 먼저 잡혀도 안전). 잠금은 연결 단위라 호출 쪽이 커밋한 뒤 <see cref="IAsyncDisposable.DisposeAsync"/> 로 푼다.
/// </summary>
public sealed partial class DocumentNumbers : IAsyncDisposable
{
    private readonly MySqlConnection _conn;
    private readonly MySqlTransaction _tx;
    private readonly string _lockName;

    private DocumentNumbers(MySqlConnection conn, MySqlTransaction tx, string lockName) => (_conn, _tx, _lockName) = (conn, tx, lockName);

    /// <param name="scope">잠금 이름 (보통 테이블.컬럼) — 같은 번호 체계끼리만 기다린다</param>
    public static async Task<DocumentNumbers> LockAsync(MySqlConnection conn, MySqlTransaction tx, string scope, int timeoutSec = 10)
    {
        var name = $"jimes.number.{scope}";
        if (await conn.ExecuteScalarAsync<int?>("SELECT GET_LOCK(@name, @timeoutSec)", new { name, timeoutSec }, tx) != 1)
            throw new BusinessRuleException("NUMBER_LOCK_TIMEOUT", "번호를 부여하는 중 다른 등록이 오래 걸리고 있습니다. 잠시 뒤 다시 시도하세요.");
        return new DocumentNumbers(conn, tx, name);
    }

    /// <summary>
    /// 다음 번호. <paramref name="table"/>.<paramref name="column"/> 에 같은 접두로 이미 있는 번호 수 + 1 부터, 비어 있는 첫 번호.
    /// </summary>
    /// <param name="taken">이번 요청에서 이미 쓴 번호 (한 번에 여러 개 부여할 때)</param>
    public async Task<string> NextAsync(string table, string column, string template, DateOnly date,
        IReadOnlyDictionary<string, string>? tokens = null, ISet<string>? taken = null)
    {
        if (!Identifier().IsMatch(table) || !Identifier().IsMatch(column))
            throw new ArgumentException("잘못된 테이블·컬럼 이름");
        // 같은 접두({SEQ} 자리만 다른) 번호 수
        var like = NumberFormat.LikePattern(template, date, tokens);
        var seq = await _conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM `{table}` WHERE `{column}` LIKE @like LOCK IN SHARE MODE", new { like }, _tx);
        while (true)
        {
            var no = NumberFormat.Format(template, date, ++seq, tokens);
            if (taken?.Contains(no) == true) continue;
            if (await _conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM `{table}` WHERE `{column}` = @no LOCK IN SHARE MODE", new { no }, _tx) == 0)
            {
                taken?.Add(no);
                return no;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_conn.State == System.Data.ConnectionState.Open)
            // 커밋·롤백 뒤면 트랜잭션 없이 (연결을 풀에 돌려줄 때도 초기화로 풀린다)
            await _conn.ExecuteAsync("SELECT RELEASE_LOCK(@name)", new { name = _lockName }, _tx.Connection is null ? null : _tx);
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex Identifier();
}
