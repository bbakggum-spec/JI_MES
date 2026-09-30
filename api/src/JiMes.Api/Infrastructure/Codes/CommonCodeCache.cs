using Dapper;
using JiMes.Api.Infrastructure.Data;

namespace JiMes.Api.Infrastructure.Codes;

public sealed class CommonCodeGroupRow
{
    public long CommonCodeGroupId { get; init; }
    public string GroupCode { get; init; } = "";
    public string GroupName { get; init; } = "";
    public string? Description { get; init; }
    public bool IsActive { get; init; }
}

public sealed class CommonCodeRow
{
    public long CommonCodeId { get; init; }
    public long CommonCodeGroupId { get; init; }
    public string Code { get; init; } = "";
    public string CodeName { get; init; } = "";
    public int SortOrder { get; init; }
    public string? AttrJson { get; init; }
    public bool IsSystem { get; init; }
    public bool IsActive { get; init; }
    public string? Remark { get; init; }
}

public sealed record CommonCodeGroup(CommonCodeGroupRow Group, IReadOnlyList<CommonCodeRow> Codes);

/// <summary>
/// 공통코드 전체 캐시. 로직은 code 로 비교하고, 표시명은 여기서 읽는다 (설계 §15.4).
/// 비활성 코드도 과거 데이터 표시를 위해 캐시에 둔다 — 선택 목록에서만 거른다.
/// </summary>
public sealed class CommonCodeCache(IDbConnectionFactory db)
{
    public const string SelectCodeSql =
        """
        SELECT common_code_id, common_code_group_id, code, code_name, sort_order, attr_json, is_system, is_active, remark
          FROM common_code
        """;

    private volatile IReadOnlyDictionary<string, CommonCodeGroup>? _groups;

    public IReadOnlyCollection<CommonCodeGroup> All => Groups.Values.ToList();

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        var groups = await conn.QueryAsync<CommonCodeGroupRow>(
            "SELECT common_code_group_id, group_code, group_name, description, is_active FROM common_code_group ORDER BY group_code");
        var codes = (await conn.QueryAsync<CommonCodeRow>(SelectCodeSql + " ORDER BY sort_order, code"))
            .ToLookup(c => c.CommonCodeGroupId);
        _groups = groups.ToDictionary(
            g => g.GroupCode,
            g => new CommonCodeGroup(g, codes[g.CommonCodeGroupId].ToList()),
            StringComparer.Ordinal);
    }

    public CommonCodeGroup GetGroup(string groupCode) =>
        Groups.TryGetValue(groupCode, out var g) ? g : throw new KeyNotFoundException($"공통코드 그룹 '{groupCode}' 가 없습니다.");

    /// <summary>표시명. 없는 코드면 코드값을 그대로 돌려준다.</summary>
    public string GetName(string groupCode, string code) =>
        GetGroup(groupCode).Codes.FirstOrDefault(c => c.Code == code)?.CodeName ?? code;

    private IReadOnlyDictionary<string, CommonCodeGroup> Groups =>
        _groups ?? throw new InvalidOperationException("공통코드 캐시가 아직 적재되지 않았습니다.");
}
