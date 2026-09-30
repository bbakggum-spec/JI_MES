// GET /api/schedule/board · /backlog 응답 (api Features/Scheduling/ScheduleEndpoints.cs)

export interface BoardEquipment {
  equipmentId: number
  equipmentCode: string
  equipmentName: string
  equipmentTypeId: number | null
  equipmentTypeName: string | null
}

export interface BoardBlockItem {
  salesOrderItemId: number
  orderItemNo: string
  partName: string | null
  customerName: string | null
  plannedQty: number
  priority: number
}

export type ScheduleStatus = 'PLANNED' | 'CONFIRMED' | 'RELEASED' | 'CANCELLED'

export interface BoardBlock {
  productionScheduleId: number
  equipmentId: number
  unitProcessId: number
  unitProcessName: string | null
  workDate: string
  sequenceNo: number
  plannedLotNo: string | null
  plannedQty: number
  plannedDurationMin: number
  durationSource: string | null
  plannedStartAt: string
  plannedEndAt: string
  status: ScheduleStatus
  isTimeLocked: boolean
  isRework: boolean
  rowVersion: number
  /** 작업지시(RELEASED)된 블록의 작업 LOT */
  productionWorkId: number | null
  workLotNo: string | null
  workStatus: string | null
  items: BoardBlockItem[]
}

export interface BoardWork {
  productionWorkId: number
  equipmentId: number
  productionScheduleId: number | null
  lotNo: string
  status: string
  actualStartAt: string | null
  actualEndAt: string | null
  expectedDurationMin: number | null
}

export interface BoardDowntime {
  equipmentId: number
  startedAt: string
  endedAt: string
  isPlanned: boolean
}

export interface Board {
  dayStart: string
  from: string
  to: string
  equipmentTypes: { equipmentTypeId: number; equipmentTypeName: string }[]
  equipment: BoardEquipment[]
  blocks: BoardBlock[]
  works: BoardWork[]
  holidays: string[]
  downtimes: BoardDowntime[]
}

export interface BacklogRow {
  salesOrderItemId: number
  orderItemNo: string
  customerName: string | null
  partName: string | null
  unitProcessId: number
  unitProcessName: string
  operationSeq: number
  orderQty: number
  remainingQty: number
  priority: number
  dueDate: string | null
}

/** 계획 이동·삽입이 가능한 상태 (RELEASED 이후 불변 — 설계 §7) */
export function isMovable(b: BoardBlock) {
  return (b.status === 'PLANNED' || b.status === 'CONFIRMED') && !b.isTimeLocked
}
