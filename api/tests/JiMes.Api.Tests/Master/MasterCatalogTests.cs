using Dapper;
using JiMes.Api.Features.Master;
using JiMes.Api.Infrastructure.Codes;
using Microsoft.Extensions.DependencyInjection;

namespace JiMes.Api.Tests.Master;

/// <summary>기준정보 정의 ↔ DDL 대조 — DDL 을 바꾸고 정의를 안 고치면 여기서 잡힌다.</summary>
[Collection(ApiCollection.Name)]
public sealed class MasterCatalogTests(ApiFixture fx)
{
    private sealed class ColumnInfo
    {
        public string TableName { get; init; } = "";
        public string ColumnName { get; init; } = "";
        public string DataType { get; init; } = "";
        public string ColumnType { get; init; } = "";
        public string IsNullable { get; init; } = "";
        public long? CharacterMaximumLength { get; init; }
    }

    [Fact]
    public async Task Every_field_matches_a_column_with_compatible_type_and_length()
    {
        await using var c = await fx.OpenAsync();
        var columns = (await c.QueryAsync<ColumnInfo>(
                "SELECT table_name, column_name, data_type, column_type, is_nullable, character_maximum_length FROM information_schema.columns WHERE table_schema = DATABASE()"))
            .ToDictionary(x => (x.TableName, x.ColumnName));

        var problems = new List<string>();
        foreach (var e in MasterCatalog.All)
        {
            if (!columns.ContainsKey((e.Table, e.PrimaryKey))) problems.Add($"{e.Key}: PK {e.PrimaryKey} 없음");
            foreach (var f in e.Fields)
            {
                if (!columns.TryGetValue((e.Table, f.Name), out var col)) { problems.Add($"{e.Key}.{f.Name}: 컬럼 없음"); continue; }
                var ok = f.Type switch
                {
                    MasterFieldType.Text or MasterFieldType.TextArea or MasterFieldType.Code => col.DataType is "varchar" or "text" or "char",
                    MasterFieldType.Integer or MasterFieldType.Lookup => col.DataType is "int" or "bigint" or "tinyint" or "smallint",
                    MasterFieldType.Decimal => col.DataType == "decimal",
                    MasterFieldType.Bool => col.ColumnType == "tinyint(1)",
                    MasterFieldType.Date => col.DataType == "date",
                    MasterFieldType.Time => col.DataType == "time",
                    _ => false,
                };
                if (!ok) problems.Add($"{e.Key}.{f.Name}: 형식 {f.Type} ↔ {col.ColumnType}");
                if (f.MaxLength is { } max && col.CharacterMaximumLength is { } len && col.DataType == "varchar" && max != len)
                    problems.Add($"{e.Key}.{f.Name}: 길이 {max} ↔ {len}");
                if (col.IsNullable == "NO" && !f.Required && f.Default is null && f.Type != MasterFieldType.Bool)
                    problems.Add($"{e.Key}.{f.Name}: DB NOT NULL 인데 정의가 필수 아님·기본값 없음");
            }
            foreach (var image in e.Images)
                if (!columns.ContainsKey((e.Table, image.Name))) problems.Add($"{e.Key}.{image.Name}: 이미지 컬럼 없음");
        }
        Assert.Empty(problems);
    }

    [Fact]
    public async Task Unique_fields_have_unique_index_and_references_exist()
    {
        await using var c = await fx.OpenAsync();
        var unique = (await c.QueryAsync<(string Table, string Column)>(
                """
                SELECT table_name, column_name FROM information_schema.statistics
                 WHERE table_schema = DATABASE() AND non_unique = 0 AND seq_in_index = 1
                   AND index_name IN (SELECT index_name FROM information_schema.statistics s2
                                       WHERE s2.table_schema = DATABASE() AND s2.table_name = statistics.table_name
                                       GROUP BY index_name HAVING COUNT(*) = 1)
                """)).ToHashSet();
        var menus = (await c.QueryAsync<string>("SELECT menu_key FROM menu")).ToHashSet();
        var codes = fx.Factory.Services.GetRequiredService<CommonCodeCache>();

        var problems = new List<string>();
        foreach (var e in MasterCatalog.All)
        {
            if (!menus.Contains(e.MenuKey)) problems.Add($"{e.Key}: 메뉴 {e.MenuKey} 없음");
            foreach (var f in e.Fields)
            {
                if (f.Unique && !unique.Contains((e.Table, f.Name))) problems.Add($"{e.Key}.{f.Name}: UNIQUE 인덱스 없음");
                if (f.Type == MasterFieldType.Lookup && MasterCatalog.All.All(x => x.Key != f.Lookup)) problems.Add($"{e.Key}.{f.Name}: 참조 {f.Lookup} 없음");
                if (f.Type == MasterFieldType.Code)
                {
                    try { codes.GetGroup(f.CodeGroup!); }
                    catch (KeyNotFoundException) { problems.Add($"{e.Key}.{f.Name}: 공통코드 {f.CodeGroup} 없음"); }
                }
            }
        }
        Assert.Empty(problems);
    }
}
