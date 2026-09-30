namespace JiMes.Api.Features.Master.Parts;

public sealed class PartDto
{
    public long PartId { get; init; }
    public string PartCode { get; init; } = "";
    public string PartName { get; init; } = "";
    public string? PartNumber { get; init; }
    public string? Specification { get; init; }
    public string? Model { get; init; }
    public string? Material { get; init; }
    public decimal? UnitWeight { get; init; }
    public string? UnitCode { get; init; }
    public string PriceBasis { get; init; } = "EA";
    public decimal? UnitPrice { get; init; }
    public string? DrawingNo { get; init; }
    public string? Hardness { get; init; }
    public string? CoreHardness { get; init; }
    public string? EffectiveHardeningDepth { get; init; }
    public string? Grade { get; init; }
    public string? Texture { get; init; }
    public string? Remark { get; init; }
    public bool IsActive { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? CustomerNames { get; init; }
    public string? DefaultHeatProcessName { get; init; }
    public long AttachmentCount { get; init; }
}

public sealed class PartCustomerDto
{
    public long CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerPartCode { get; init; }
    public bool IsCustomerLotRequired { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsActive { get; init; }
}

public sealed class PartHeatProcessDto
{
    public long HeatProcessId { get; init; }
    public string? HeatProcessName { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
}

public sealed class PartPrintTemplateDto
{
    public long PrintTemplateId { get; init; }
    public string? PrintTemplateName { get; init; }
    public string? PurposeCode { get; init; }
    public string? PurposeName { get; init; }
    public long? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public bool IsDefault { get; init; }
}

public sealed class AttachmentDto
{
    public long AttachmentId { get; init; }
    public string AttachmentKind { get; init; } = "";
    public string FileName { get; init; } = "";
    public string? ContentType { get; init; }
    public long? FileSize { get; init; }
    public string? Caption { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class PartHistoryDto
{
    public long PartHistoryId { get; init; }
    public DateTime ChangedAt { get; init; }
    public string? ChangedByName { get; init; }
    public string ChangeType { get; init; } = "";
    public string? OldDataJson { get; init; }
    public string? NewDataJson { get; init; }
}

public sealed record PartDetail(
    PartDto Part, IReadOnlyList<PartCustomerDto> Customers, IReadOnlyList<PartHeatProcessDto> HeatProcesses,
    IReadOnlyList<PartPrintTemplateDto> PrintTemplates, IReadOnlyList<AttachmentDto> Attachments);

public sealed record PartCustomerInput(long CustomerId, string? CustomerPartCode, bool IsCustomerLotRequired, bool IsPrimary);

public sealed record PartHeatProcessInput(long HeatProcessId, bool IsDefault);

public sealed record PartPrintTemplateInput(long PrintTemplateId, long? CustomerId, bool IsDefault);

/// <param name="AllowDuplicatePartNumber">같은 품번이 이미 있어도 저장 (화면 확인 후) — 구 F_PartDetailForm 은 무조건 거부</param>
public sealed record PartSaveRequest(
    string? PartCode, string? PartName, string? PartNumber, string? Specification, string? Model, string? Material,
    decimal? UnitWeight, string? UnitCode, string? PriceBasis, decimal? UnitPrice, string? DrawingNo,
    string? Hardness, string? CoreHardness, string? EffectiveHardeningDepth, string? Grade, string? Texture, string? Remark,
    bool IsActive, PartCustomerInput[]? Customers, PartHeatProcessInput[]? HeatProcesses,
    bool AllowDuplicatePartNumber, string? Reason);

public sealed class LookupOption
{
    public long Value { get; init; }
    public string Label { get; init; } = "";
    public bool Active { get; init; }
    public string? Group { get; init; }
}
