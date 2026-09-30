using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JiMes.Api.Features.Master;

[JsonConverter(typeof(JsonStringEnumConverter<MasterFieldType>))]
public enum MasterFieldType
{
    Text,
    TextArea,
    Integer,
    Decimal,
    Bool,
    Date,
    Time,
    /// <summary>공통코드 값 (CodeGroup)</summary>
    Code,
    /// <summary>다른 기준정보 id (Lookup = 엔티티 키)</summary>
    Lookup,
}

/// <summary>기준정보 필드 정의 — API 검증·SQL·화면 구성의 단일 원천.</summary>
public sealed record MasterField(string Name, string Label, MasterFieldType Type = MasterFieldType.Text)
{
    public bool Required { get; init; }
    public int? MaxLength { get; init; }
    /// <summary>테이블 안에서 중복 금지 (DDL UNIQUE 와 일치)</summary>
    public bool Unique { get; init; }
    public string? Lookup { get; init; }
    public string? CodeGroup { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public object? Default { get; init; }
    public bool Searchable { get; init; }
    public bool InList { get; init; } = true;
    public int? Width { get; init; }
    public string? Help { get; init; }
}

/// <summary>이미지 컬럼 (예: company.stamp_image) — 목록 조회에는 넣지 않고 별도 API 로 올리고 받는다.</summary>
public sealed record MasterImageField(string Name, string Label, string? FileNameColumn);

/// <summary>기준정보 1개 = 테이블 1개. PK 는 명명 규칙 {table}_id (설계 §1.1).</summary>
public sealed partial record MasterEntity(string Key, string Table, string Label, string MenuKey, string DisplayField, MasterField[] Fields)
{
    public string PrimaryKey => $"{Table}_id";

    /// <summary>목록 기본 정렬 (SQL ORDER BY — 코드 상수)</summary>
    public string OrderBy { get; init; } = "";

    /// <summary>is_active 가 있으면 삭제 대신 사용 중지 (참조 무결성·과거 데이터 보존)</summary>
    public bool HasActive => Fields.Any(f => f.Name == "is_active");

    /// <summary>참조되지 않는 설정성 데이터만 실제 삭제 허용 (예: 공장 달력, 기준시간)</summary>
    public bool AllowDelete { get; init; }

    public bool HasCreatedBy { get; init; }
    public bool HasUpdatedBy { get; init; }

    /// <summary>둘 중 하나 이상 필수 (DDL CHECK — 예: 기준시간은 설비유형 또는 설비)</summary>
    public string[]? RequireOneOf { get; init; }

    public MasterImageField[] Images { get; init; } = [];

    public string Description { get; init; } = "";

    public void Validate()
    {
        foreach (var name in Fields.Select(f => f.Name).Append(Table).Append(DisplayField)
                     .Concat(Images.SelectMany(i => new[] { i.Name, i.FileNameColumn }).OfType<string>()))
        {
            if (!Identifier().IsMatch(name))
                throw new InvalidOperationException($"기준정보 {Key}: 잘못된 식별자 '{name}'");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex Identifier();
}
