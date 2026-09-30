namespace JiMes.Api.Features.Quality;

// Dapper 는 BIGINT UNSIGNED 를 생성자·튜플의 long 으로 바꾸지 못하므로 조회 행은 속성 클래스로 받는다

public sealed class InspectionDto
{
    public long InspectionId { get; init; }
    public string InspectionNo { get; init; } = "";
    public string InspectionType { get; init; } = "";
    public DateTime InspectionDate { get; init; }
    public long ProductionWorkId { get; init; }
    public string LotNo { get; init; } = "";
    public long? InspectionStandardVersionId { get; init; }
    public long? ReinspectionOfId { get; init; }
    public string? ReinspectionOfNo { get; init; }
    public long? InspectorEmployeeId { get; init; }
    public string? InspectorName { get; init; }
    public string Status { get; init; } = "";
    public string? Decision { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Remark { get; init; }
    public string? TargetSummary { get; init; }
    public long TargetCount { get; init; }
    public int RowVersion { get; init; }
}

public sealed class InspectionTargetDto
{
    public long InspectionTargetId { get; init; }
    public int SubNo { get; init; }
    public long ProductionWorkInputId { get; init; }
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public long PartId { get; init; }
    public long CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerLot { get; init; }
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
    public decimal? InspectionQty { get; init; }
    public string? SubmitLotNo { get; init; }
    public DateTime? ReportIssuedAt { get; init; }
    public int ReportIssueCount { get; init; }
}

public sealed class InspectionItemDto
{
    public long InspectionItemId { get; init; }
    public long? InspectionCriteriaId { get; init; }
    public int SequenceNo { get; init; }
    public string? ItemType { get; init; }
    public string ItemName { get; init; } = "";
    public string? Location { get; init; }
    public string? Result { get; init; }
    public string? Decision { get; init; }
    public string? Remark { get; init; }
    public List<string?> Values { get; set; } = [];
}

public sealed class MeasurementRow
{
    public long InspectionItemId { get; init; }
    public int SampleNo { get; init; }
    public decimal? MeasuredValue { get; init; }
    public string? MeasuredText { get; init; }
}

/// <summary>검사기준 항목 (판정 근거)</summary>
public sealed class CriteriaDto
{
    public long InspectionCriteriaId { get; init; }
    public int SequenceNo { get; init; }
    public string? ItemType { get; init; }
    public string ItemName { get; init; } = "";
    public string? Location { get; init; }
    public string? SpecificationValue { get; init; }
    public string? Scale { get; init; }
    public string? RangeType { get; init; }
    public decimal? LowerLimit { get; init; }
    public decimal? UpperLimit { get; init; }
    public int SampleCount { get; init; }
    public int TestCount { get; init; }
    public List<string?> Points { get; set; } = [];
}

/// <summary>LOT 입력 → 검사 대상 후보 (그 LOT 의 투입 행)</summary>
public sealed class TargetCandidateDto
{
    public long ProductionWorkInputId { get; init; }
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public long PartId { get; init; }
    public long CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerLot { get; init; }
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
    public decimal InputQty { get; init; }
    public decimal GoodQty { get; init; }
    public string? MainLotNo { get; init; }
    /// <summary>이 투입 행이 대상인 (취소 안 된) 검사 수</summary>
    public long InspectionCount { get; init; }
    /// <summary>품목(+거래처) 검사기준 현재 Version — 거래처 전용이 먼저</summary>
    public long? InspectionStandardVersionId { get; init; }
}

public sealed record InspectionItemInput(long? InspectionCriteriaId, string? ItemType, string? ItemName, string? Location, string? Decision,
    string? Remark, string?[]? Values);

public sealed record InspectionCreateRequest(string? InspectionType, DateOnly InspectionDate, long ProductionWorkId, long[]? InputIds,
    long? InspectionStandardVersionId, long? InspectorEmployeeId, string? Remark, InspectionItemInput[]? Items);

public sealed record InspectionUpdateRequest(int RowVersion, DateOnly InspectionDate, long[]? InputIds, long? InspectionStandardVersionId,
    long? InspectorEmployeeId, string? Remark, InspectionItemInput[]? Items);

public sealed record InspectionVersionRequest(int RowVersion, string? Reason);
