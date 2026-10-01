// /api/shipments · /api/closings 응답 (api Features/Sales/ShipmentService.cs · ClosingService.cs)

export interface Shipment {
  shipmentId: number
  shipmentNo: string
  shipmentDate: string
  customerId: number
  customerName: string | null
  status: 'DRAFT' | 'CONFIRMED' | 'SHIPPED' | 'CANCELLED'
  closingStatus: 'UNCLOSED' | 'CLOSED' | 'CARRIED_OVER'
  closingYear: number | null
  closingMonth: number | null
  closingNo: string | null
  closingDueDate: string | null
  printSumByPart: boolean
  supplyAmount: number | null
  vatAmount: number | null
  totalAmount: number | null
  totalQty: number
  itemCount: number
  itemSummary: string | null
  remark: string | null
  rowVersion: number
}

export interface ShipmentItem {
  shipmentItemId: number
  lineNo: number
  salesOrderItemId: number
  orderItemNo: string
  mainWorkId: number | null
  mainLotNo: string | null
  shipmentQty: number
  testSpecimenQty: number
  shipmentWeight: number | null
  chargeCount: number | null
  priceBasis: string
  unitPrice: number | null
  amount: number | null
  submitLotNo: string | null
  customerLot: string | null
  partName: string | null
  partNumber: string | null
}

export interface StockRow {
  salesOrderItemId: number
  orderItemNo: string
  orderDate: string
  partName: string | null
  partNumber: string | null
  customerLot: string | null
  orderQty: number
  unitPrice: number | null
  priceBasis: string
  unitWeight: number | null
  mainWorkId: number | null
  mainLotNo: string | null
  mainStatus: string | null
  submitLotNo: string | null
  lotQty: number
  shippedQty: number
  availableQty: number
  orderRemainingQty: number
}

/** 등록 창의 담은 행 */
export interface ShipmentLine {
  stock: StockRow
  shipmentQty: number | null
  testSpecimenQty: number
  chargeCount: number | null
}

export interface ClosingCustomer {
  customerId: number
  customerName: string
  closingDay: number | null
  closingDate: string
  openCount: number
  openAmount: number
  closedCount: number
  closedAmount: number
}

export interface Closing {
  shipmentClosingId: number
  closingNo: string
  customerId: number
  customerName: string | null
  closingDate: string
  closingYear: number
  closingMonth: number
  totalQty: number
  totalWeight: number
  totalAmount: number
  closingStatus: 'OPEN' | 'CLOSED' | 'REOPENED'
  closedAt: string | null
  closedByName: string | null
  shipmentCount: number
  remark: string | null
  rowVersion: number
}

/** 예상 금액 (서버 ShipmentMath 와 같은 규칙, 반올림 방식은 서버 설정 — 저장 시 서버 값이 정답) */
export function previewAmount(l: ShipmentLine): number | null {
  const s = l.stock
  if (s.unitPrice == null || !l.shipmentQty) return null
  if (s.priceBasis === 'KG') return s.unitWeight == null ? null : Math.round(l.shipmentQty * s.unitWeight * s.unitPrice)
  if (s.priceBasis === 'CHARGE') return l.chargeCount == null ? null : Math.round(l.chargeCount * s.unitPrice)
  return Math.round(l.shipmentQty * s.unitPrice)
}
