/** 수주(입고) 품목 행 — API SalesOrderItemDto */
export interface OrderItem {
  salesOrderItemId: number
  orderItemNo: string
  salesOrderId: number
  salesOrderNo: string
  orderDate: string
  dueDate: string | null
  customerId: number
  customerName: string
  lineNo: number
  partId: number
  partCode: string
  partName: string | null
  partNumber: string | null
  specification: string | null
  model: string | null
  material: string | null
  heatProcessId: number | null
  heatProcessVersionId: number | null
  heatProcessName: string | null
  customerLot: string | null
  coilNo: string | null
  orderQty: number
  orderWeight: number | null
  unitWeight: number | null
  unitPrice: number | null
  unitCode: string | null
  priceBasis: string
  priority: number
  customerWorkOrderNo: string | null
  isSeparatelyManaged: boolean
  isReturn: boolean
  status: string
  hardness: string | null
  coreHardness: string | null
  caseDepth: string | null
  texture: string | null
  remark: string | null
  mainInputQty: number
  shipmentQty: number
  remainingShipmentQty: number
  usedQty: number
  isScheduledOrInput: boolean
  rowVersion: number
}

export interface PartCandidate {
  partId: number
  partCode: string
  partName: string
  partNumber: string | null
  specification: string | null
  model: string | null
  material: string | null
  unitWeight: number | null
  unitPrice: number | null
  priceBasis: string
  customerPartCode: string | null
  isCustomerLotRequired: boolean
  isCustomerPart: boolean
  heatProcesses: { heatProcessId: number; heatProcessName: string; isDefault: boolean }[]
}

/** 등록 화면의 담은 행 */
export interface OrderLine {
  key: number
  part: PartCandidate
  heatProcessId: number | null
  orderQty: number | null
  unitPrice: number | null
  customerLot: string
  coilNo: string
  customerWorkOrderNo: string
  priority: number
  isSeparatelyManaged: boolean
  remark: string
}
