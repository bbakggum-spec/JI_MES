// GET /api/works/* 응답 (api Features/Production/WorkModels.cs)

export interface Work {
  productionWorkId: number
  lotNo: string
  productionScheduleId: number | null
  unitProcessId: number
  unitProcessName: string | null
  equipmentId: number | null
  equipmentName: string | null
  isMainProcess: boolean
  isRework: boolean
  heatProcessName: string | null
  workDate: string
  status: 'ALLOCATED' | 'INPUT' | 'COMPLETED' | 'CANCELLED'
  plannedStartAt: string | null
  plannedEndAt: string | null
  actualStartAt: string | null
  actualEndAt: string | null
  expectedDurationMin: number | null
  actualDurationMin: number | null
  standardVersionId: number | null
  standardName: string | null
  standardVersionNo: number | null
  isStandardFixed: boolean
  standardFixedAt: string | null
  submitLotNo: string | null
  marking: string | null
  remark: string | null
  inputCount: number
  inputQty: number
  rowVersion: number
}

export interface WorkInput {
  productionWorkInputId: number
  salesOrderItemId: number
  orderItemNo: string
  customerName: string | null
  customerLot: string | null
  partId: number
  partName: string | null
  partNumber: string | null
  mainWorkId: number | null
  mainLotNo: string | null
  mainInputId: number | null
  isStandardBasis: boolean
  inputQty: number
  defectQty: number
  goodQty: number
  trayMark: string | null
  remark: string | null
  isReferenced: boolean
}

export interface WorkCondition {
  conditionItemId: number
  conditionItemName: string
  unitCode: string | null
  valueType: string
  isActive: boolean
  sortOrder: number
  stepSequenceNo: number | null
  stepNameSnapshot: string | null
  setValue: string | null
}

export interface WorkDetail {
  work: Work
  inputs: WorkInput[]
  conditions: WorkCondition[]
  events: { eventType: string; eventAt: string; userName: string | null; remark: string | null }[]
}

export interface WorkBoard {
  workDate: string
  equipment: { equipmentId: number; equipmentName: string; equipmentTypeId: number | null; equipmentTypeName: string | null }[]
  works: Work[]
}

export interface InputCandidate {
  salesOrderItemId: number
  orderItemNo: string
  customerName: string | null
  partName: string | null
  partNumber: string | null
  customerLot: string | null
  mainWorkId: number | null
  mainLotNo: string | null
  mainInputId: number | null
  phase: 'PRE' | 'MAIN' | 'POST'
  baseQty: number
  alreadyQty: number
  remainingQty: number
  alreadyInThisWork: boolean
}

export interface ScanResult {
  kind: 'ORDER_ITEM' | 'MAIN_LOT'
  code: string
  candidates: InputCandidate[]
}

export interface StandardCandidate {
  productionWorkInputId: number
  partName: string | null
  standardId: number
  standardVersionId: number
  standardCode: string
  standardName: string
  versionNo: number
  equipmentName: string | null
  equipmentTypeName: string | null
  customerName: string | null
  runningTimeMin: number | null
  chargeQty: number
  specificity: number
}
