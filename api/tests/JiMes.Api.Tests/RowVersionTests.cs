using Dapper;
using JiMes.Api.Infrastructure.Data;
using JiMes.Api.Infrastructure.Errors;

namespace JiMes.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class RowVersionTests(ApiFixture fx)
{
    private const string UpdateSql =
        "UPDATE rv_sample SET name = @Name, row_version = row_version + 1 WHERE rv_sample_id = @Id AND row_version = @RowVersion";

    private async Task<MySqlConnector.MySqlConnection> OpenWithSampleAsync()
    {
        var conn = await fx.OpenAsync();
        await conn.ExecuteAsync(
            """
            CREATE TEMPORARY TABLE rv_sample (
                rv_sample_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                name VARCHAR(20) NOT NULL,
                row_version INT UNSIGNED NOT NULL DEFAULT 0);
            INSERT INTO rv_sample (rv_sample_id, name) VALUES (1, 'a');
            """);
        return conn;
    }

    [Fact]
    public async Task Matching_version_updates_and_increments()
    {
        await using var conn = await OpenWithSampleAsync();
        await conn.ExecuteVersionedUpdateAsync(UpdateSql, new { Id = 1L, Name = "b", RowVersion = 0 }, "rv_sample", 1);
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT row_version FROM rv_sample WHERE rv_sample_id = 1"));
    }

    [Fact]
    public async Task Stale_version_throws_conflict_and_missing_row_throws_not_found()
    {
        await using var conn = await OpenWithSampleAsync();
        await conn.ExecuteVersionedUpdateAsync(UpdateSql, new { Id = 1L, Name = "b", RowVersion = 0 }, "rv_sample", 1);

        // 같은 버전(0)으로 한 번 더 저장 = 다른 사용자가 먼저 수정한 상황
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            conn.ExecuteVersionedUpdateAsync(UpdateSql, new { Id = 1L, Name = "c", RowVersion = 0 }, "rv_sample", 1));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            conn.ExecuteVersionedUpdateAsync(UpdateSql, new { Id = 2L, Name = "c", RowVersion = 0 }, "rv_sample", 2));
    }

    [Fact]
    public async Task Sql_without_version_clause_is_refused()
    {
        await using var conn = await OpenWithSampleAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            conn.ExecuteVersionedUpdateAsync("UPDATE rv_sample SET name = 'x' WHERE rv_sample_id = 1", new { }, "rv_sample", 1));
    }
}
