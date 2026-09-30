namespace JiMes.Api.Features.Production;

// Dapper 는 BIGINT UNSIGNED 를 생성자·튜플의 long 으로 바꾸지 못하므로 조회 행은 속성 클래스로 받는다

/// <summary>작업 LOT (작업 화면 머리)</summary>
public sealed class WorkDto
{
    public long ProductionWorkId { get; init; }
    public string LotNo { get; init; } = "";
    public long? ProductionScheduleId { get; init; }
    public long UnitProcessId { get; init; }
    public string? UnitProcessName { get; init; }
    public long? EquipmentId { get; init; }
    public string? EquipmentName { get; init; }
    public bool IsMainProcess { get; init; }
    public bool IsRework { get; init; }
    public string? HeatProcessName { get; init; }
    public DateTime WorkDate { get; init; }
    public string Status { get; init; } = "";
    public DateTime? PlannedStartAt { get; init; }
    public DateTime? PlannedEndAt { get; init; }
    public DateTime? ActualStartAt { get; init; }
    public DateTime? ActualEndAt { get; init; }
    public decimal? ExpectedDurationMin { get; init; }
    public decimal? ActualDurationMin { get; init; }
    public long? StandardVersionId { get; init; }
    public string? StandardName { get; init; }
    public int? StandardVersionNo { get; init; }
    public bool IsStandardFixed { get; init; }
    public DateTime? StandardFixedAt { get; init; }
    public string? SubmitLotNo { get; init; }
    public string? Marking { get; init; }
    public string? Remark { get; init; }
    public long InputCount { get; init; }
    public decimal InputQty { get; init; }
    public int RowVersion { get; init; }
}

/// <summary>투입 행 (작업 LOT × 수주)</summary>
public sealed class WorkInputDto
{
    public long ProductionWorkInputId { get; init; }
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public string? CustomerName { get; init; }
    public string? CustomerLot { get; init; }
    public long PartId { get; init; }
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
    public long? MainWorkId { get; init; }
    public string? MainLotNo { get; init; }
    public long? MainInputId { get; init; }
    public bool IsStandardBasis { get; init; }
    public decimal InputQty { get; init; }
    public decimal DefectQty { get; init; }
    public decimal GoodQty { get; init; }
    public string? TrayMark { get; init; }
    public string? Remark { get; init; }
    /// <summary>이 행을 줄일 수 있는 하한 — 후공정·검사·부적합이 이미 쓰는 수량</summary>
    public bool IsReferenced { get; init; }
}

public sealed class WorkConditionDto
{
    public long ConditionItemId { get; init; }
    public string ConditionItemName { get; init; } = "";
    public string? UnitCode { get; init; }
    public string ValueType { get; init; } = "NUMBER";
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public int? StepSequenceNo { get; init; }
    public string? StepNameSnapshot { get; init; }
    public string? SetValue { get; init; }
}

public sealed class WorkEventDto
{
    public string EventType { get; init; } = "";
    public DateTime EventAt { get; init; }
    public string? UserName { get; init; }
    public string? Remark { get; init; }
}

/// <summary>스캔 결과 후보 1행 — 화면이 수량을 받아 투입 추가</summary>
public sealed class InputCandidateDto
{
    public long SalesOrderItemId { get; init; }
    public string OrderItemNo { get; init; } = "";
    public string? CustomerName { get; init; }
    public string? PartName { get; init; }
    public string? PartNumber { get; init; }
    public string? CustomerLot { get; init; }
    public long? MainWorkId { get; set; }
    public string? MainLotNo { get; set; }
    public long? MainInputId { get; init; }
    public string Phase { get; set; } = "";
    public decimal BaseQty { get; set; }
    public decimal AlreadyQty { get; set; }
    public decimal RemainingQty => BaseQty - AlreadyQty;
    public bool AlreadyInThisWork { get; set; }
}

public sealed record ScanResult(string Kind, string Code, IReadOnlyList<InputCandidateDto> Candidates);

/// <summary>표준 확정 후보 (투입 품목별 작업표준 현재 Version)</summary>
public sealed class StandardCandidateDto
{
    public long ProductionWorkInputId { get; init; }
    public string? PartName { get; init; }
    public long StandardId { get; init; }
    public long StandardVersionId { get; init; }
    public string StandardCode { get; init; } = "";
    public string StandardName { get; init; } = "";
    public int VersionNo { get; init; }
    public string? EquipmentName { get; init; }
    public string? EquipmentTypeName { get; init; }
    public string? CustomerName { get; init; }
    public decimal? RunningTimeMin { get; init; }
    public decimal ChargeQty { get; init; }
    /// <summary>작은 값이 더 구체적 (설비 지정 > 설비유형 > 공통, 거래처 지정 우선)</summary>
    public int Specificity { get; init; }
}

public sealed record ScanRequest(string? Code);
public sealed record AddInputRequest(int RowVersion, long SalesOrderItemId, long? MainInputId, decimal InputQty, string? TrayMark, string? Remark);
public sealed record UpdateInputRequest(int RowVersion, decimal InputQty, string? TrayMark, string? Remark);
public sealed record WorkVersionRequest(int RowVersion, string? Reason);
public sealed record StartWorkRequest(int RowVersion, DateTime? StartAt);
public sealed record CompleteWorkRequest(int RowVersion, DateTime? EndAt);
public sealed record UpdateWorkRequest(int RowVersion, string? SubmitLotNo, string? Marking, string? Remark);
public sealed record FixStandardRequest(int RowVersion, long ProductionWorkInputId, long StandardVersionId);
public sealed record WorkConditionInput(int? StepNo, long ConditionItemId, string? ConditionValue);
public sealed record SaveConditionsRequest(int RowVersion, string[]? Steps, long[]? Items, WorkConditionInput[]? Conditions);
public sealed record AdHocWorkRequest(long EquipmentId, long UnitProcessId);
