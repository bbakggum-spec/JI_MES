import { DeleteOutlined, FilePdfOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert, App, Button, Checkbox, Col, DatePicker, Form, Input, InputNumber, Popconfirm, Row, Select, Space, Statistic, Switch, Table, Tag, Typography,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { ApiError, api, apiFile, saveFile } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import EditorWindow from '../../components/EditorWindow'
import { useClientSettings } from '../../hooks/useClientSettings'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'
import { previewAmount, type Shipment, type ShipmentItem, type ShipmentLine, type StockRow } from './shipmentTypes'

const DATE = 'YYYY-MM-DD'
const qty = (n: number | null | undefined) => (n == null ? '' : n.toLocaleString(undefined, { maximumFractionDigits: 3 }))
const won = (n: number | null | undefined) => (n == null ? '' : n.toLocaleString())
const errorText = (e: unknown) => (e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))

/** 출하 — 구 F_OutForm(목록) + F_OutAddForm(전표 등록·수정) (설계 §6·§23.7) */
export default function ShipmentsPage({ menuKey }: PageProps) {
  const canCreate = useCan(menuKey, 'create')
  const codes = useCommonCodes()
  const settings = useClientSettings()
  const customers = useOptions('/api/master/customer/options')
  const queryClient = useQueryClient()
  const [range, setRange] = useState<[Dayjs, Dayjs] | null>(null)
  const [customerId, setCustomerId] = useState<number | undefined>()
  const [search, setSearch] = useState('')
  const [includeCancelled, setIncludeCancelled] = useState(false)
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const effectiveRange = range ?? (settings.orderListDays ? [dayjs().subtract(settings.orderListDays, 'day'), dayjs()] as [Dayjs, Dayjs] : null)
  const params = new URLSearchParams()
  if (effectiveRange) { params.set('from', effectiveRange[0].format(DATE)); params.set('to', effectiveRange[1].format(DATE)) }
  if (customerId) params.set('customerId', String(customerId))
  if (search) params.set('search', search)
  if (includeCancelled) params.set('includeCancelled', 'true')
  const list = useQuery({
    queryKey: [...queryKeys.shipments, 'list', params.toString()],
    queryFn: ({ signal }) => api<Shipment[]>(`/api/shipments?${params}`, { signal }),
    enabled: effectiveRange !== null,
  })
  const rows = list.data ?? []
  const live = rows.filter((r) => r.status !== 'CANCELLED')

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>출하</Typography.Title>
        <Space wrap>
          <DatePicker.RangePicker value={effectiveRange} allowClear={false} onChange={(v) => v?.[0] && v[1] && setRange([v[0], v[1]])} />
          <Select allowClear showSearch optionFilterProp="label" placeholder="거래처 전체" style={{ width: 160 }} value={customerId} onChange={setCustomerId} options={customers.options} />
          <Input.Search allowClear placeholder="전표번호·입고번호·품명·LOT" style={{ width: 220 }} onSearch={(v) => setSearch(v.trim())} />
          <Space size={4}><Switch size="small" checked={includeCancelled} onChange={setIncludeCancelled} />취소 포함</Space>
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>출하 등록</Button>}
        </Space>
      </Space>
      <Table<Shipment> rowKey="shipmentId" size="small" loading={list.isFetching} dataSource={rows} scroll={{ x: 1200 }}
        pagination={{ defaultPageSize: 50, showSizeChanger: true, showTotal: (n) => `${n}건` }}
        onRow={(r) => ({ onClick: () => setEditing(r.shipmentId), style: { cursor: 'pointer', opacity: r.status === 'CANCELLED' ? 0.5 : 1 } })}
        summary={() => live.length > 0 && (
          <Table.Summary.Row>
            <Table.Summary.Cell index={0} colSpan={4}>합계 (취소 제외)</Table.Summary.Cell>
            <Table.Summary.Cell index={4} align="right">{qty(live.reduce((s, r) => s + r.totalQty, 0))}</Table.Summary.Cell>
            <Table.Summary.Cell index={5} align="right">{won(live.reduce((s, r) => s + (r.supplyAmount ?? 0), 0))}</Table.Summary.Cell>
            <Table.Summary.Cell index={6} align="right">{won(live.reduce((s, r) => s + (r.vatAmount ?? 0), 0))}</Table.Summary.Cell>
            <Table.Summary.Cell index={7} align="right">{won(live.reduce((s, r) => s + (r.totalAmount ?? 0), 0))}</Table.Summary.Cell>
            <Table.Summary.Cell index={8} colSpan={2} />
          </Table.Summary.Row>
        )}
        columns={[
          { title: '출하일', dataIndex: 'shipmentDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
          { title: '전표번호', dataIndex: 'shipmentNo', width: 125 },
          { title: '거래처', dataIndex: 'customerName', width: 120, ellipsis: true },
          { title: '품목', ellipsis: true, render: (_: unknown, r) => <>{r.itemSummary}<Typography.Text type="secondary"> ({r.itemCount})</Typography.Text></> },
          { title: '수량', dataIndex: 'totalQty', width: 80, align: 'right', render: qty },
          { title: '공급가액', dataIndex: 'supplyAmount', width: 110, align: 'right', render: won },
          { title: '세액', dataIndex: 'vatAmount', width: 90, align: 'right', render: won },
          { title: '합계', dataIndex: 'totalAmount', width: 110, align: 'right', render: won },
          {
            title: '마감', width: 120, render: (_: unknown, r) => r.status === 'CANCELLED' ? <Tag>{codes.name('ORDER_STATUS', 'CANCELLED')}</Tag> : (
              <Space size={2}><Tag color={codes.attr<{ color?: string }>('CLOSING_STATUS', r.closingStatus)?.color}>{codes.name('CLOSING_STATUS', r.closingStatus)}</Tag>
                {r.closingYear && <Typography.Text type="secondary" style={{ fontSize: 12 }}>{r.closingYear}-{String(r.closingMonth).padStart(2, '0')}</Typography.Text>}</Space>
            ),
          },
          { title: '비고', dataIndex: 'remark', width: 120, ellipsis: true },
        ]} />
      {editing !== null && <ShipmentWindow id={editing === 'new' ? null : editing} menuKey={menuKey} onClose={() => setEditing(null)}
        onChanged={(id) => { setEditing(id); void queryClient.invalidateQueries({ queryKey: queryKeys.shipments }) }} />}
    </>
  )
}

interface HeaderForm {
  shipmentDate: Dayjs
  customerId: number
  closingDueDate?: Dayjs | null
  printSumByPart: boolean
  remark?: string
}

function ShipmentWindow({ id, menuKey, onClose, onChanged }: { id: number | null; menuKey: string; onClose: () => void; onChanged: (id: number) => void }) {
  const { message, modal } = App.useApp()
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const canDelete = useCan(menuKey, 'delete')
  const codes = useCommonCodes()
  const customers = useOptions('/api/master/customer/options')
  const [form] = Form.useForm<HeaderForm>()
  const detail = useQuery({
    queryKey: [...queryKeys.shipments, 'detail', id],
    queryFn: ({ signal }) => api<{ shipment: Shipment; items: ShipmentItem[] }>(`/api/shipments/${id}`, { signal }),
    enabled: id !== null,
  })
  const d = detail.data
  const watchedCustomer = Form.useWatch('customerId', form)
  const customerId = d?.shipment.customerId ?? watchedCustomer
  const stock = useQuery({
    queryKey: [...queryKeys.shipments, 'stock', customerId, id],
    queryFn: ({ signal }) => api<StockRow[]>(`/api/shipments/stock?customerId=${customerId}${id ? `&excludeShipmentId=${id}&includeZero=true` : ''}`, { signal }),
    enabled: !!customerId,
  })
  const [lines, setLines] = useState<ShipmentLine[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // 거래명세표 양식: 기본(당사 양식) 외에 등록한 엑셀 양식(업체 전용 등)으로 바꿔 출력 — 설계 §12 ⑤
  const slipTemplates = useQuery({
    queryKey: [...queryKeys.print, 'choices', 'SHIPMENT_SLIP'],
    queryFn: () => api<{ printTemplateId: number; printTemplateName: string; templateKind: string; isDefault: boolean }[]>('/api/shipments/slip-templates'),
    enabled: id !== null,
  })
  const [slipTemplateId, setSlipTemplateId] = useState<number>()
  const selectedSlip = slipTemplateId ?? slipTemplates.data?.find((t) => t.isDefault)?.printTemplateId ?? slipTemplates.data?.[0]?.printTemplateId
  if (id !== null && !d) return null
  const s = d?.shipment
  const editable = s ? s.status !== 'CANCELLED' && s.closingStatus !== 'CLOSED' && canUpdate : canCreate
  const keyOf = (x: { salesOrderItemId: number; mainWorkId: number | null }) => `${x.salesOrderItemId}:${x.mainWorkId ?? 0}`
  // 기존 전표: 재고(이 전표 제외) 행에 전표 수량을 얹어 편집
  const effectiveLines: ShipmentLine[] = lines ?? (d && stock.data ? d.items.map((i) => ({
    stock: stock.data!.find((x) => keyOf(x) === keyOf(i)) ?? {
      salesOrderItemId: i.salesOrderItemId, orderItemNo: i.orderItemNo, orderDate: '', partName: i.partName, partNumber: i.partNumber, customerLot: i.customerLot,
      orderQty: 0, unitPrice: i.unitPrice, priceBasis: i.priceBasis, unitWeight: null, mainWorkId: i.mainWorkId, mainLotNo: i.mainLotNo, mainStatus: null,
      submitLotNo: i.submitLotNo, lotQty: 0, shippedQty: 0, availableQty: i.shipmentQty + i.testSpecimenQty, orderRemainingQty: 0,
    },
    shipmentQty: i.shipmentQty, testSpecimenQty: i.testSpecimenQty, chargeCount: i.chargeCount,
  })) : [])
  const setLine = (k: string, patch: Partial<ShipmentLine>) => setLines(effectiveLines.map((l) => (keyOf(l.stock) === k ? { ...l, ...patch } : l)))
  const inLines = new Set(effectiveLines.map((l) => keyOf(l.stock)))
  const supply = effectiveLines.reduce((sum, l) => sum + (previewAmount(l) ?? 0), 0)

  const save = async () => {
    const h = await form.validateFields()
    setError(null)
    setBusy(true)
    try {
      const body = {
        rowVersion: s?.rowVersion, shipmentDate: h.shipmentDate.format(DATE), customerId: h.customerId, closingDueDate: h.closingDueDate?.format(DATE) ?? null,
        printSumByPart: h.printSumByPart, remark: h.remark,
        items: effectiveLines.map((l) => ({ salesOrderItemId: l.stock.salesOrderItemId, mainWorkId: l.stock.mainWorkId, shipmentQty: l.shipmentQty ?? 0, testSpecimenQty: l.testSpecimenQty, chargeCount: l.chargeCount })),
      }
      const r = s
        ? await api<{ shipmentId: number; totalAmount: number }>(`/api/shipments/${id}`, { method: 'PUT', body })
        : await api<{ shipmentId: number; shipmentNo: string; totalAmount: number }>('/api/shipments', { method: 'POST', body })
      message.success(`저장했습니다 — 합계 ${won(r.totalAmount)}원`)
      setLines(null)
      if (!s) onChanged(r.shipmentId)
      else { await detail.refetch(); await stock.refetch(); onChanged(r.shipmentId) }
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }
  const slip = async () => {
    const q = selectedSlip ? `?printTemplateId=${selectedSlip}` : ''
    try { saveFile(await apiFile(`/api/shipments/${id}/slip${q}`, { method: 'POST' })) } catch (e) { message.error(errorText(e)) }
  }

  return (
    <EditorWindow onClose={onClose} size={1400}
      title={s ? <Space>{s.shipmentNo}{s.status === 'CANCELLED' ? <Tag>{codes.name('ORDER_STATUS', 'CANCELLED')}</Tag>
        : <Tag color={codes.attr<{ color?: string }>('CLOSING_STATUS', s.closingStatus)?.color}>{codes.name('CLOSING_STATUS', s.closingStatus)}</Tag>}</Space> : '출하 등록'}
      extra={(
        <Space>
          {s && s.status !== 'CANCELLED' && (
            <Space.Compact>
              <Select aria-label="거래명세표 양식" style={{ width: 200 }} value={selectedSlip} loading={slipTemplates.isLoading}
                onChange={setSlipTemplateId} popupMatchSelectWidth={false}
                options={(slipTemplates.data ?? []).map((t) => ({ value: t.printTemplateId, label: t.isDefault ? `${t.printTemplateName} (기본)` : t.printTemplateName }))} />
              <Button icon={<FilePdfOutlined />} onClick={() => void slip()}>거래명세표</Button>
            </Space.Compact>
          )}
          {s && editable && canDelete && (
            <Popconfirm title="이 전표를 취소합니다" description="출하 수량은 재고로 돌아갑니다." onConfirm={() => void (async () => {
              try {
                await api(`/api/shipments/${id}/cancel`, { method: 'POST', body: { rowVersion: s.rowVersion } })
                message.success('전표를 취소했습니다.')
                await detail.refetch()
                onChanged(id!)
              } catch (e) { setError(errorText(e)) }
            })()}>
              <Button danger>전표 취소</Button>
            </Popconfirm>
          )}
          {editable && <Button type="primary" loading={busy} disabled={effectiveLines.length === 0} onClick={() => void save()}>저장</Button>}
        </Space>
      )}>
      <Form form={form} layout="vertical" disabled={!editable} initialValues={s
        ? { shipmentDate: dayjs(s.shipmentDate), customerId: s.customerId, closingDueDate: s.closingDueDate ? dayjs(s.closingDueDate) : null, printSumByPart: s.printSumByPart, remark: s.remark ?? undefined }
        : { shipmentDate: dayjs(), printSumByPart: false }}>
        <Row gutter={12}>
          <Col span={4}><Form.Item name="shipmentDate" label="출하일" rules={[{ required: true }]}><DatePicker style={{ width: '100%' }} /></Form.Item></Col>
          <Col span={6}>
            <Form.Item name="customerId" label="거래처" rules={[{ required: true }]}>
              <Select showSearch optionFilterProp="label" options={customers.options} disabled={!!s}
                onChange={() => effectiveLines.length > 0 && modal.confirm({ title: '거래처를 바꾸면 담은 품목을 비웁니다.', onOk: () => setLines([]) })} />
            </Form.Item>
          </Col>
          <Col span={4}><Form.Item name="closingDueDate" label="마감일"><DatePicker style={{ width: '100%' }} /></Form.Item></Col>
          <Col span={3}><Form.Item name="printSumByPart" label="품목 합산 출력" valuePropName="checked"><Checkbox /></Form.Item></Col>
          <Col span={7}><Form.Item name="remark" label="비고"><Input maxLength={255} /></Form.Item></Col>
        </Row>
      </Form>

      {editable && customerId && (
        <>
          <Typography.Text strong>출하 재고 — 수주 × 출하 LOT (주 LOT)</Typography.Text>
          <Table<StockRow> size="small" style={{ margin: '8px 0 16px' }} rowKey={keyOf} loading={stock.isFetching} pagination={false} scroll={{ x: 1000, y: 260 }}
            dataSource={(stock.data ?? []).filter((x) => x.availableQty > 0 || inLines.has(keyOf(x)))}
            locale={{ emptyText: '출하할 수 있는 재고가 없습니다 (주 LOT 투입·부적합·기출하를 반영)' }}
            columns={[
              { title: '입고번호', dataIndex: 'orderItemNo', width: 125 },
              { title: '품명', render: (_: unknown, x) => <>{x.partName}{x.partNumber && <Typography.Text type="secondary"> {x.partNumber}</Typography.Text>}</> },
              { title: '고객LOT', dataIndex: 'customerLot', width: 100 },
              { title: '출하 LOT', width: 150, render: (_: unknown, x) => x.mainLotNo ? <>{x.mainLotNo}{x.mainStatus && x.mainStatus !== 'COMPLETED' && <Tag style={{ marginLeft: 4 }}>{codes.name('WORK_STATUS', x.mainStatus)}</Tag>}</> : <Typography.Text type="secondary">수주 단위</Typography.Text> },
              { title: '제출 LOT', dataIndex: 'submitLotNo', width: 100 },
              { title: '가능', dataIndex: 'availableQty', width: 80, align: 'right', render: qty },
              { title: '단가', width: 110, align: 'right', render: (_: unknown, x) => <>{won(x.unitPrice)} <Typography.Text type="secondary">/{codes.name('PRICE_BASIS', x.priceBasis)}</Typography.Text></> },
              {
                title: '', width: 70, render: (_: unknown, x) => inLines.has(keyOf(x)) ? <Tag>담음</Tag>
                  : <Button size="small" onClick={() => setLines([...effectiveLines, { stock: x, shipmentQty: x.availableQty, testSpecimenQty: 0, chargeCount: null }])}>담기</Button>,
              },
            ]} />
        </>
      )}

      <Space style={{ width: '100%', justifyContent: 'space-between' }}>
        <Typography.Text strong>전표 품목</Typography.Text>
        {s ? <Space size="large"><Statistic title="공급가액" value={s.supplyAmount ?? 0} /><Statistic title="세액" value={s.vatAmount ?? 0} /><Statistic title="합계" value={s.totalAmount ?? 0} /></Space>
          : <Typography.Text type="secondary">예상 공급가액 {won(supply)}원 (세액은 저장 시 계산)</Typography.Text>}
      </Space>
      <Table<ShipmentLine> size="small" bordered style={{ marginTop: 8 }} rowKey={(l) => keyOf(l.stock)} pagination={false} dataSource={effectiveLines} scroll={{ x: 1100 }}
        columns={[
          { title: '입고번호', width: 125, render: (_: unknown, l) => l.stock.orderItemNo },
          { title: '품명', render: (_: unknown, l) => l.stock.partName },
          { title: '출하 LOT', width: 130, render: (_: unknown, l) => l.stock.mainLotNo ?? '-' },
          { title: '가능', width: 80, align: 'right', render: (_: unknown, l) => qty(l.stock.availableQty) },
          {
            title: '출하수량', width: 110, render: (_: unknown, l) => editable
              ? <InputNumber size="small" min={0} max={l.stock.availableQty} value={l.shipmentQty} onChange={(v) => setLine(keyOf(l.stock), { shipmentQty: v })} style={{ width: 95 }} />
              : qty(l.shipmentQty),
          },
          {
            title: '시험편', width: 90, render: (_: unknown, l) => editable
              ? <InputNumber size="small" min={0} value={l.testSpecimenQty} onChange={(v) => setLine(keyOf(l.stock), { testSpecimenQty: v ?? 0 })} style={{ width: 75 }} />
              : qty(l.testSpecimenQty),
          },
          {
            title: 'charge 수', width: 90, render: (_: unknown, l) => l.stock.priceBasis !== 'CHARGE' ? '' : editable
              ? <InputNumber size="small" min={0} value={l.chargeCount} status={l.chargeCount == null ? 'error' : undefined} onChange={(v) => setLine(keyOf(l.stock), { chargeCount: v })} style={{ width: 75 }} />
              : qty(l.chargeCount),
          },
          { title: '단가', width: 120, align: 'right', render: (_: unknown, l) => <>{won(l.stock.unitPrice)} <Typography.Text type="secondary">/{codes.name('PRICE_BASIS', l.stock.priceBasis)}</Typography.Text></> },
          {
            title: '금액', width: 110, align: 'right', render: (_: unknown, l) => {
              const saved = d?.items.find((i) => keyOf(i) === keyOf(l.stock))
              return lines === null && saved ? won(saved.amount) : won(previewAmount(l))
            },
          },
          ...(editable ? [{
            title: '', width: 40, render: (_: unknown, l: ShipmentLine) =>
              <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={() => setLines(effectiveLines.filter((x) => keyOf(x.stock) !== keyOf(l.stock)))} />,
          }] : []),
        ]} />
      {error && <Alert style={{ marginTop: 12 }} type="error" showIcon title={error} />}
    </EditorWindow>
  )
}
