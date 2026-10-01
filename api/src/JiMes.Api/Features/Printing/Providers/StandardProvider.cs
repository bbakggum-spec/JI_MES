using System.Globalization;
using Dapper;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Features.Printing.Providers;

/// <summary>
/// 작업표준 Version 1건 — 작업표준서 (구 F_WorkStandardForm → WorkStandardSheet).
/// 품목 정보 / 요구사항(품목) / 작업 설비·사이클 / 작업 공정(공정 경로) / 작업표준 표(관리항목 × 스텝).
/// 원본 = standard_version (화면에서 고른 Version, 보통 현재 Version).
/// </summary>
public sealed class StandardProvider : IPrintDataProvider
{
    public string DataSourceCode => "STANDARD";
    public string MenuKey => MenuKeys.MasterStandard;
    public string SourceTable => "standard_version";

    private sealed class StandardRow
    {
        public string StandardCode { get; init; } = "";
        public string StandardName { get; init; } = "";
        public int VersionNo { get; init; }
        public bool IsCurrent { get; init; }
        public DateTime EffectiveFrom { get; init; }
        public long PartId { get; init; }
        public long? CustomerId { get; init; }
        public string? CustomerName { get; init; }
        public string? PartName { get; init; }
        public string? PartNumber { get; init; }
        public string? Specification { get; init; }
        public string? Model { get; init; }
        public string? Material { get; init; }
        public string? Hardness { get; init; }
        public string? CoreHardness { get; init; }
        public string? CaseDepth { get; init; }
        public string? Texture { get; init; }
        public string? EquipmentTypeName { get; init; }
        public string? EquipmentName { get; init; }
        public string? UnitProcessName { get; init; }
        public decimal ChargeQty { get; init; }
        public string? ChargeUnit { get; init; }
        public decimal? RunningTimeMin { get; init; }
        public long? HeatProcessId { get; init; }
        public string? HeatProcessName { get; init; }
        public string? Remark { get; init; }
    }

    private sealed class StepRow { public int SequenceNo { get; init; } public string StepName { get; init; } = ""; }
    private sealed class ItemRow { public long ConditionItemId { get; init; } public string ConditionItemName { get; init; } = ""; public string? UnitCode { get; init; } }
    private sealed class CellRow { public int? StepNo { get; init; } public long ConditionItemId { get; init; } public string? Value { get; init; } }

    public async Task<PrintSource> LoadAsync(MySqlConnection conn, long sourceId, FieldDictionary fields, DateTime issuedAt, CancellationToken ct)
    {
        // 공정: 표준에 지정한 공정, 없으면 품목 기본 공정
        var s = await conn.QuerySingleOrDefaultAsync<StandardRow>(
            """
            SELECT st.standard_code, st.standard_name, v.version_no, v.is_current, v.effective_from, st.part_id, st.customer_id, c.customer_name,
                   p.part_name, p.part_number, p.specification, p.model, p.material, p.hardness, p.core_hardness,
                   p.effective_hardening_depth AS case_depth, p.texture, et.equipment_type_name, e.equipment_name, u.unit_process_name,
                   v.charge_qty, v.charge_unit, v.running_time_min, h.heat_process_id, h.heat_process_name, v.remark
              FROM standard_version v
              JOIN standard st ON st.standard_id = v.standard_id
              JOIN part p ON p.part_id = st.part_id
              JOIN unit_process u ON u.unit_process_id = st.unit_process_id
              LEFT JOIN customer c ON c.customer_id = st.customer_id
              LEFT JOIN equipment e ON e.equipment_id = st.equipment_id
              LEFT JOIN equipment_type et ON et.equipment_type_id = COALESCE(st.equipment_type_id, e.equipment_type_id)
              LEFT JOIN heat_process h ON h.heat_process_id = COALESCE(st.heat_process_id,
                   (SELECT ph.heat_process_id FROM part_heat_process ph WHERE ph.part_id = st.part_id AND ph.is_default = 1 AND ph.is_active = 1 LIMIT 1))
             WHERE v.standard_version_id = @sourceId
            """, new { sourceId }) ?? throw new NotFoundException("standard_version", sourceId);
        var route = s.HeatProcessId is { } hp
            ? (await conn.QueryAsync<string>(
                """
                SELECT u.unit_process_name FROM heat_process_version hv
                  JOIN heat_process_operation o ON o.heat_process_version_id = hv.heat_process_version_id
                  JOIN unit_process u ON u.unit_process_id = o.unit_process_id
                 WHERE hv.heat_process_id = @hp AND hv.is_current = 1 ORDER BY o.sequence_no
                """, new { hp })).ToList()
            : [];

        var data = new PrintData();
        data.Values["StandardCode"] = s.StandardCode;
        data.Values["StandardName"] = s.StandardName;
        data.Values["VersionNo"] = s.VersionNo.ToString(CultureInfo.InvariantCulture);
        data.Values["WriteDate"] = s.EffectiveFrom.ToString(fields.FormatOf("WriteDate") ?? "yyyy-MM-dd", CultureInfo.InvariantCulture);
        data.Values["IssueDate"] = issuedAt.ToString(fields.FormatOf("IssueDate") ?? "yyyy-MM-dd", CultureInfo.InvariantCulture);
        data.Values["CustomerName"] = s.CustomerName;
        data.Values["PartName"] = s.PartName;
        data.Values["PartNumber"] = s.PartNumber;
        data.Values["Specification"] = s.Specification;
        data.Values["Model"] = s.Model;
        data.Values["Material"] = s.Material;
        data.Values["Hardness"] = s.Hardness;
        data.Values["CoreHardness"] = s.CoreHardness;
        data.Values["CaseDepth"] = s.CaseDepth;
        data.Values["Texture"] = s.Texture;
        data.Values["EquipmentTypeName"] = s.EquipmentTypeName;
        data.Values["EquipmentName"] = s.EquipmentName;
        data.Values["UnitProcessName"] = s.UnitProcessName;
        data.Values["ChargeQty"] = s.ChargeQty;
        data.Values["ChargeUnit"] = s.ChargeUnit;
        data.Values["Charge"] = $"{s.ChargeQty.ToString("#,##0.###", CultureInfo.InvariantCulture)} {s.ChargeUnit}".Trim();
        // 구 작업표준서는 시간(hr) 표기 — 신규 저장 단위는 분
        data.Values["RunningHours"] = s.RunningTimeMin is { } m ? Math.Round(m / 60, 2) : null;
        data.Values["HeatProcessName"] = s.HeatProcessName;
        data.Values["ProcessFlow"] = route.Count > 0 ? string.Join(" → ", route) : null;
        data.Values["Remark"] = s.Remark;

        var steps = await conn.QueryAsync<StepRow>(
            "SELECT sequence_no, step_name FROM standard_version_step WHERE standard_version_id = @sourceId ORDER BY sequence_no", new { sourceId });
        var items = await conn.QueryAsync<ItemRow>(
            """
            SELECT i.condition_item_id, c.condition_item_name, c.unit_code FROM standard_version_item i
              JOIN condition_item c ON c.condition_item_id = i.condition_item_id WHERE i.standard_version_id = @sourceId ORDER BY i.sequence_no
            """, new { sourceId });
        var cells = await conn.QueryAsync<CellRow>(
            "SELECT step_no, condition_item_id, condition_value AS value FROM standard_condition WHERE standard_version_id = @sourceId", new { sourceId });
        ConditionMatrix.Fill(data, "Standard",
            steps.Select(x => new ConditionMatrix.Step(x.SequenceNo, x.StepName)).ToList(),
            items.Select(x => new ConditionMatrix.Item(x.ConditionItemId, x.ConditionItemName, x.UnitCode)).ToList(),
            cells.Select(x => new ConditionMatrix.Cell(x.StepNo, x.ConditionItemId, x.Value)).ToList());

        return new PrintSource(data, s.PartId, s.CustomerId, $"{s.StandardCode}_v{s.VersionNo}");
    }
}
