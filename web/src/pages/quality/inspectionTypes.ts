// /api/inspections 응답 (api Features/Quality/InspectionModels.cs)

export interface Inspection {
  inspectionId: number
  inspectionNo: string
  inspectionType: string
  inspectionDate: string
  productionWorkId: number
  lotNo: string
  inspectionStandardVersionId: number | null
  reinspectionOfId: number | null
  reinspectionOfNo: string | null
  inspectorEmployeeId: number | null
  inspectorName: string | null
  status: 'WAITING' | 'IN_PROGRESS' | 'COMPLETED' | 'CANCELLED'
  decision: string | null
  completedAt: string | null
  remark: string | null
  targetSummary: string | null
  targetCount: number
  rowVersion: number
}

export interface InspectionTarget {
  inspectionTargetId: number
  subNo: number
  productionWorkInputId: number
  salesOrderItemId: number
  orderItemNo: string
  partId: number
  customerId: number
  customerName: string | null
  customerLot: string | null
  partName: string | null
  partNumber: string | null
  inspectionQty: number | null
  submitLotNo: string | null
  reportIssuedAt: string | null
  reportIssueCount: number
}

export interface InspectionItem {
  inspectionItemId: number
  inspectionCriteriaId: number | null
  sequenceNo: number
  itemType: string | null
  itemName: string
  location: string | null
  result: string | null
  decision: string | null
  remark: string | null
  values: (string | null)[]
}

export interface Criteria {
  inspectionCriteriaId: number
  sequenceNo: number
  itemType: string | null
  itemName: string
  location: string | null
  specificationValue: string | null
  scale: string | null
  rangeType: string | null
  lowerLimit: number | null
  upperLimit: number | null
  sampleCount: number
  testCount: number
  points: (string | null)[]
}

export interface InspectionDetail {
  inspection: Inspection
  targets: InspectionTarget[]
  items: InspectionItem[]
  criteria: Criteria[]
}

export interface TargetCandidate {
  productionWorkInputId: number
  salesOrderItemId: number
  orderItemNo: string
  partId: number
  customerId: number
  customerName: string | null
  customerLot: string | null
  partName: string | null
  partNumber: string | null
  inputQty: number
  goodQty: number
  mainLotNo: string | null
  inspectionCount: number
  inspectionStandardVersionId: number | null
}

export interface LotLookup {
  work: { productionWorkId: number; lotNo: string; status: string; unitProcessName: string | null; equipmentName: string | null; submitLotNo: string | null; workDate: string }
  candidates: TargetCandidate[]
}

/** 편집 중 항목 행 */
export interface ItemRow {
  key: number
  inspectionCriteriaId: number | null
  itemType: string | null
  itemName: string
  location: string | null
  decision: string | null
  remark: string | null
  values: string[]
}

/** 화면 미리 판정 (서버 InspectionJudge 와 같은 규칙 — 저장 시 서버가 다시 판정) */
export function previewJudge(c: Criteria | undefined, values: string[], manual: string | null): { decision: string | null; ng: boolean[] } {
  const ranged = c && ((c.rangeType === 'BETWEEN' && c.lowerLimit != null && c.upperLimit != null)
    || (c.rangeType === 'MIN' && c.lowerLimit != null) || (c.rangeType === 'MAX' && c.upperLimit != null))
  const ng = values.map((raw) => {
    const t = raw.trim()
    if (!ranged || t === '' || Number.isNaN(Number(t))) return false
    const v = Number(t)
    return (c!.rangeType !== 'MAX' && c!.lowerLimit != null && v < c!.lowerLimit) || (c!.rangeType !== 'MIN' && c!.upperLimit != null && v > c!.upperLimit)
  })
  const hasNumber = values.some((v) => v.trim() !== '' && !Number.isNaN(Number(v.trim())))
  if (ranged && hasNumber) return { decision: ng.some(Boolean) ? 'FAIL' : 'PASS', ng }
  return { decision: manual, ng }
}
