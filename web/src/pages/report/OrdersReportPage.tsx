import { useQuery } from '@tanstack/react-query'
import { DatePicker, Input, Segmented, Select, Space, Table, Tag, Tooltip, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { api } from '../../api/client'
import ExportButton from '../../components/ExportButton'
import { useClientSettings } from '../../hooks/useClientSettings'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'

interface OrderProgress {
  salesOrderItemId: number
  orderItemNo: string
  orderDate: string
  dueDate: string | null
  customerName: string | null
  partName: string | null
  partNumber: string | null
  customerLot: string | null
  heatProcessName: string | null
  priority: number
  orderQty: number
  orderAmount: number
  mainInputQty: number
  defectQty: number
  openDefectQty: number
  shipmentQty: number
  testSpecimenQty: number
  shipmentAmount: number
  stockQty: number
  notInputQty: number
  remainingShipmentQty: number
  processes: { sequenceNo: number; unitProcessName: string; isMainProcess: boolean; inputQty: number; goodQty: number; lotCount: number }[]
}

const DATE = 'YYYY-MM-DD'
const qty = (n: number | null | undefined) => (n == null ? '' : n.toLocaleString(undefined, { maximumFractionDigits: 3 }))
const sum = (rows: OrderProgress[], f: (r: OrderProgress) => number) => rows.reduce((s, r) => s + f(r), 0)

/** 수주 진행·재고 — 구 F_OrderStatus · F_InventoryForm · F_OutcomeStatus (설계 §24.2) */
export default function OrdersReportPage() {
  const settings = useClientSettings()
  const codes = useCommonCodes()
  const customers = useOptions('/api/master/customer/options')
  const [range, setRange] = useState<[Dayjs, Dayjs] | null>(null)
  const [customerId, setCustomerId] = useState<number | undefined>()
  const [search, setSearch] = useState('')
  const [view, setView] = useState<string>('OPEN')
  const effectiveRange = range ?? (settings.orderListDays ? [dayjs().subtract(settings.orderListDays, 'day'), dayjs()] as [Dayjs, Dayjs] : null)
  const params = new URLSearchParams()
  if (effectiveRange) { params.set('from', effectiveRange[0].format(DATE)); params.set('to', effectiveRange[1].format(DATE)) }
  if (customerId) params.set('customerId', String(customerId))
  if (search) params.set('search', search)
  if (view !== 'ALL') params.set('view', view)
  const list = useQuery({
    queryKey: [...queryKeys.reports, 'orders', params.toString()],
    queryFn: ({ signal }) => api<OrderProgress[]>(`/api/reports/orders?${params}`, { signal }),
    enabled: effectiveRange !== null,
  })
  const rows = list.data ?? []

  // 표·내보내기 공용 열 (내보내기 = 화면 표시 글자 그대로)
  const listColumns: ColumnsType<OrderProgress> = [
      { title: '입고일', dataIndex: 'orderDate', width: 95, fixed: 'left', render: (d: string) => dayjs(d).format(DATE) },
      {
        title: '입고번호', dataIndex: 'orderItemNo', width: 130, fixed: 'left',
        render: (no: string, r) => <Space size={2}>{no}{r.priority >= 2 && <Tag color={codes.attr<{ color?: string }>('PRIORITY', String(r.priority))?.color}>{codes.name('PRIORITY', String(r.priority))}</Tag>}</Space>,
      },
      { title: '거래처', dataIndex: 'customerName', width: 100, ellipsis: true },
      { title: '품명', width: 180, ellipsis: true, render: (_: unknown, r) => <>{r.partName}{r.partNumber && <Typography.Text type="secondary"> {r.partNumber}</Typography.Text>}</> },
      { title: '납기', dataIndex: 'dueDate', width: 90, render: (d: string | null) => d && <Typography.Text type={dayjs(d).isBefore(dayjs(), 'day') ? 'danger' : undefined}>{dayjs(d).format('MM-DD')}</Typography.Text> },
      { title: '수주', dataIndex: 'orderQty', width: 75, align: 'right', render: qty },
      {
        title: '공정 진행 (투입/양품)', width: 360, render: (_: unknown, r) => r.processes.length === 0 ? <Typography.Text type="secondary">{r.heatProcessName ?? '공정 미지정'}</Typography.Text> : (
          <Space size={2} wrap>
            {r.processes.map((p) => {
              const done = p.inputQty >= r.orderQty && r.orderQty > 0
              return (
                <Tooltip key={p.sequenceNo} title={`${p.unitProcessName}: LOT ${p.lotCount}개 · 투입 ${qty(p.inputQty)} · 양품 ${qty(p.goodQty)}`}>
                  <Tag color={done ? 'success' : p.inputQty > 0 ? 'processing' : 'default'} style={{ marginInlineEnd: 0, fontWeight: p.isMainProcess ? 600 : undefined }}>
                    {p.unitProcessName}{p.isMainProcess ? '*' : ''} {p.inputQty > 0 ? `${qty(p.inputQty)}${p.goodQty !== p.inputQty ? `/${qty(p.goodQty)}` : ''}` : '-'}
                  </Tag>
                </Tooltip>
              )
            })}
          </Space>
        ),
      },
      { title: '미투입', dataIndex: 'notInputQty', width: 75, align: 'right', render: (n: number) => n > 0 ? qty(n) : '' },
      { title: '미처리 부적합', dataIndex: 'openDefectQty', width: 90, align: 'right', render: (n: number) => n > 0 && <Typography.Text type="danger">{qty(n)}</Typography.Text> },
      { title: '재고', dataIndex: 'stockQty', width: 75, align: 'right', render: (n: number) => n > 0 && <Typography.Text strong>{qty(n)}</Typography.Text> },
      { title: '출하', dataIndex: 'shipmentQty', width: 75, align: 'right', render: (n: number, r) => <>{qty(n)}{r.testSpecimenQty > 0 && <Typography.Text type="secondary"> +{qty(r.testSpecimenQty)}</Typography.Text>}</> },
      { title: '출하 잔량', dataIndex: 'remainingShipmentQty', width: 80, align: 'right', render: qty },
      { title: '출하 금액', dataIndex: 'shipmentAmount', width: 100, align: 'right', render: (n: number) => n.toLocaleString() },
    ]

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>수주 진행·재고</Typography.Title>
        <Space wrap>
          <Segmented value={view} onChange={(v) => setView(v as string)} options={[
            { value: 'OPEN', label: '미출하' }, { value: 'STOCK', label: '재고 있음' }, { value: 'NOT_INPUT', label: '미투입' }, { value: 'ALL', label: '전체' },
          ]} />
          <DatePicker.RangePicker value={effectiveRange} allowClear={false} onChange={(v) => v?.[0] && v[1] && setRange([v[0], v[1]])} />
          <Select allowClear showSearch optionFilterProp="label" placeholder="거래처 전체" style={{ width: 150 }} value={customerId} onChange={setCustomerId} options={customers.options} />
          <Input.Search allowClear placeholder="입고번호·품명·품번·고객LOT" style={{ width: 220 }} onSearch={(v) => setSearch(v.trim())} />
          <ExportButton title="수주 진행·재고" columns={listColumns} rows={rows} />
        </Space>
      </Space>
      <Table<OrderProgress> rowKey="salesOrderItemId" size="small" loading={list.isFetching} dataSource={rows} scroll={{ x: 1600 }}
        pagination={{ defaultPageSize: 50, showSizeChanger: true, showTotal: (n) => `${n}건` }}
        summary={() => rows.length > 0 && (
          <Table.Summary.Row>
            <Table.Summary.Cell index={0} colSpan={5}>합계</Table.Summary.Cell>
            <Table.Summary.Cell index={5} align="right">{qty(sum(rows, (r) => r.orderQty))}</Table.Summary.Cell>
            <Table.Summary.Cell index={6} />
            <Table.Summary.Cell index={7} align="right">{qty(sum(rows, (r) => r.notInputQty))}</Table.Summary.Cell>
            <Table.Summary.Cell index={8} align="right">{qty(sum(rows, (r) => r.openDefectQty))}</Table.Summary.Cell>
            <Table.Summary.Cell index={9} align="right">{qty(sum(rows, (r) => r.stockQty))}</Table.Summary.Cell>
            <Table.Summary.Cell index={10} align="right">{qty(sum(rows, (r) => r.shipmentQty))}</Table.Summary.Cell>
            <Table.Summary.Cell index={11} align="right">{qty(sum(rows, (r) => r.remainingShipmentQty))}</Table.Summary.Cell>
            <Table.Summary.Cell index={12} align="right">{sum(rows, (r) => r.shipmentAmount).toLocaleString()}</Table.Summary.Cell>
          </Table.Summary.Row>
        )}
        columns={listColumns} />
      <Typography.Text type="secondary">
        공정 진행: 경로 순 단위공정별 투입(/양품), *는 주공정. 재고 = 주 LOT 양품(특채 포함) − 출하 − 시험편 = 출하 화면에서 출하할 수 있는 수량. 재작업은 공정 진행에서 뺌.
      </Typography.Text>
    </>
  )
}
