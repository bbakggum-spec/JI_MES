import { CopyOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert, App, Button, Checkbox, Col, DatePicker, Descriptions, Form, Input, InputNumber, Popconfirm, Row, Select, Space, Switch,
  Table, Tag, Tooltip, Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import ExportButton from '../../components/ExportButton'
import EditorWindow from '../../components/EditorWindow'
import PrintButton from '../../components/PrintButton'
import { useClientSettings } from '../../hooks/useClientSettings'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useDataVersion } from '../../hooks/useDataVersion'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'
import type { OrderItem, OrderLine, PartCandidate } from './salesTypes'

const DATE = 'YYYY-MM-DD'
const qty = (n: number | null | undefined) => (n == null ? '' : n.toLocaleString(undefined, { maximumFractionDigits: 3 }))

/** 수주(입고) — 구 F_IncomeForm(목록) + F_IncomeAddForm(등록·수정). 입고번호 = 현장에서 스캔하는 수주번호 (설계 §17) */
export default function SalesOrdersPage({ menuKey }: PageProps) {
  const canCreate = useCan(menuKey, 'create')
  const queryClient = useQueryClient()
  const settings = useClientSettings()
  const customers = useOptions('/api/master/customer/options')
  const codes = useCommonCodes()
  const [range, setRange] = useState<[Dayjs, Dayjs] | null>(null)
  const [customerId, setCustomerId] = useState<number | undefined>()
  const [search, setSearch] = useState('')
  const [openOnly, setOpenOnly] = useState(false)
  const [includeCancelled, setIncludeCancelled] = useState(false)
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<number | null>(null)
  const [selected, setSelected] = useState<number[]>([])

  // 기본 기간 = 오늘 − 설정 sales_order.list_default_days ~ 오늘
  const effectiveRange = range ?? (settings.orderListDays ? [dayjs().subtract(settings.orderListDays, 'day'), dayjs()] as [Dayjs, Dayjs] : null)
  const params = new URLSearchParams()
  if (effectiveRange) { params.set('from', effectiveRange[0].format(DATE)); params.set('to', effectiveRange[1].format(DATE)) }
  if (customerId) params.set('customerId', String(customerId))
  if (search) params.set('search', search)
  if (openOnly) params.set('openOnly', 'true')
  if (includeCancelled) params.set('includeCancelled', 'true')
  const key = [...queryKeys.sales, 'orders']
  const list = useQuery({
    queryKey: [...key, params.toString()],
    queryFn: ({ signal }) => api<{ items: OrderItem[] }>(`/api/sales-orders/items?${params}`, { signal }),
    enabled: effectiveRange !== null,
  })
  const rows = list.data?.items ?? []
  const refresh = () => void queryClient.invalidateQueries({ queryKey: queryKeys.sales })

  // 표·내보내기 공용 열 (내보내기 = 화면 표시 글자 그대로)
  const listColumns: ColumnsType<OrderItem> = [
      { title: '입고일', dataIndex: 'orderDate', width: 95, fixed: 'left', render: (d: string) => dayjs(d).format(DATE) },
      { title: '입고번호', dataIndex: 'orderItemNo', width: 125, fixed: 'left', render: (no: string, r) => <Typography.Text strong={r.priority >= 2}>{no}</Typography.Text> },
      { title: '거래처', dataIndex: 'customerName', width: 110, ellipsis: true },
      {
        title: '품목', ellipsis: true, render: (_: unknown, r) => (
          <>{r.partName}{r.partNumber && <Typography.Text type="secondary"> {r.partNumber}</Typography.Text>}{r.isReturn && <Tag color="purple" style={{ marginLeft: 4 }}>반입</Tag>}</>
        ),
      },
      { title: '공정', dataIndex: 'heatProcessName', width: 100, render: (n: string | null) => n ?? <Typography.Text type="danger">미지정</Typography.Text> },
      { title: '수량', dataIndex: 'orderQty', width: 80, align: 'right', render: qty },
      { title: '투입', dataIndex: 'mainInputQty', width: 70, align: 'right', render: qty },
      { title: '출하', dataIndex: 'shipmentQty', width: 70, align: 'right', render: qty },
      { title: '출하 잔량', dataIndex: 'remainingShipmentQty', width: 80, align: 'right', render: qty },
      {
        title: '우선', dataIndex: 'priority', width: 60,
        render: (p: number) => <Tag color={codes.attr<{ color?: string }>('PRIORITY', String(p))?.color}>{codes.name('PRIORITY', String(p))}</Tag>,
      },
      {
        title: '상태', dataIndex: 'status', width: 70,
        render: (s: string) => <Tag color={codes.attr<{ color?: string }>('ORDER_STATUS', s)?.color}>{codes.name('ORDER_STATUS', s)}</Tag>,
      },
      { title: '고객LOT', dataIndex: 'customerLot', width: 100, ellipsis: true },
      { title: '작업지시', dataIndex: 'customerWorkOrderNo', width: 100, ellipsis: true },
      { title: '별도', dataIndex: 'isSeparatelyManaged', width: 50, align: 'center', render: (v: boolean) => v && <Tooltip title="별도관리"><Tag color="red">별</Tag></Tooltip> },
      { title: '비고', dataIndex: 'remark', width: 140, ellipsis: true },
    ]

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>수주(입고)</Typography.Title>
        <Space wrap>
          <DatePicker.RangePicker value={effectiveRange} allowClear={false} onChange={(v) => v?.[0] && v[1] && setRange([v[0], v[1]])} />
          <Select allowClear showSearch optionFilterProp="label" placeholder="거래처 전체" style={{ width: 160 }} value={customerId} onChange={setCustomerId} options={customers.options} />
          <Input.Search allowClear placeholder="입고번호·품목·고객LOT" style={{ width: 220 }} onSearch={(v) => setSearch(v.trim())} />
          <Space size={4}><Switch size="small" checked={openOnly} onChange={setOpenOnly} />미출하만</Space>
          <Space size={4}><Switch size="small" checked={includeCancelled} onChange={setIncludeCancelled} />취소 포함</Space>
          {/* 구 F_IncomeForm 출력 — 고른 입고 행마다 1장, 한 파일로 */}
          <ExportButton title="수주(입고)" columns={listColumns} rows={rows} />
          <PrintButton purposeCode="PROCESS_SHEET" sourceIds={selected}>공정이동표</PrintButton>
          <PrintButton purposeCode="PRODUCT_LABEL" sourceIds={selected}>제품표시 라벨</PrintButton>
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>수주 등록</Button>}
        </Space>
      </Space>
      <Table<OrderItem> rowKey="salesOrderItemId" size="small" loading={list.isFetching} dataSource={rows} scroll={{ x: 1500 }}
        pagination={{ defaultPageSize: 50, showSizeChanger: true, showTotal: (n) => `${n}건` }}
        rowSelection={{ selectedRowKeys: selected, onChange: (keys) => setSelected(keys as number[]), preserveSelectedRowKeys: true }}
        // 선택 칸(체크박스)을 누르면 창을 열지 않음
        onRow={(r) => ({ onClick: (e) => { if (!(e.target as HTMLElement).closest(".ant-table-selection-column")) setEditing(r.salesOrderItemId) }, style: { cursor: 'pointer', opacity: r.status === 'CANCELLED' ? 0.5 : 1 } })}
        summary={() => rows.length > 0 && (
          <Table.Summary.Row>
            <Table.Summary.Cell index={0} />
            <Table.Summary.Cell index={1} colSpan={5}>합계</Table.Summary.Cell>
            <Table.Summary.Cell index={6} align="right">{qty(rows.reduce((s, r) => s + (r.status === 'CANCELLED' ? 0 : r.orderQty), 0))}</Table.Summary.Cell>
            <Table.Summary.Cell index={7} align="right">{qty(rows.reduce((s, r) => s + r.mainInputQty, 0))}</Table.Summary.Cell>
            <Table.Summary.Cell index={8} align="right">{qty(rows.reduce((s, r) => s + r.shipmentQty, 0))}</Table.Summary.Cell>
            <Table.Summary.Cell index={9} colSpan={7} />
          </Table.Summary.Row>
        )}
        columns={listColumns} />
      {creating && <NewOrderWindow onClose={() => setCreating(false)} onSaved={() => { setCreating(false); refresh() }} />}
      {editing !== null && <ItemWindow itemId={editing} menuKey={menuKey} onClose={() => setEditing(null)} onChanged={refresh} />}
    </>
  )
}

interface HeaderForm {
  orderDate: Dayjs
  customerId: number
  dueDate?: Dayjs | null
  isReturn: boolean
  remark?: string
}

let lineSeq = 0
const lineOf = (p: PartCandidate): OrderLine => ({
  key: ++lineSeq, part: p, heatProcessId: p.heatProcesses.find((h) => h.isDefault)?.heatProcessId ?? p.heatProcesses[0]?.heatProcessId ?? null,
  orderQty: null, unitPrice: p.unitPrice, customerLot: '', coilNo: '', customerWorkOrderNo: '', priority: 1, isSeparatelyManaged: false, remark: '',
})

/** 수주 등록 — 거래처 품목을 담고(장바구니) 행마다 수량·LOT 입력. 저장 = 행마다 입고번호 (구 F_IncomeAddForm) */
function NewOrderWindow({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const { modal } = App.useApp()
  const [form] = Form.useForm<HeaderForm>()
  const customers = useOptions('/api/master/customer/options')
  const codes = useCommonCodes()
  const customerId = Form.useWatch('customerId', form)
  const [partSearch, setPartSearch] = useState('')
  const [allParts, setAllParts] = useState(false)
  const [lines, setLines] = useState<OrderLine[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const candidates = useQuery({
    queryKey: [...queryKeys.sales, 'candidates', customerId, partSearch, allParts],
    queryFn: ({ signal }) => api<PartCandidate[]>(`/api/sales-orders/part-candidates?customerId=${customerId}&all=${allParts}&search=${encodeURIComponent(partSearch)}`, { signal }),
    enabled: !!customerId,
  })
  const setLine = (key: number, patch: Partial<OrderLine>) => setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...patch } : l)))

  const save = async () => {
    const h = await form.validateFields()
    setError(null)
    const missing = lines.findIndex((l) => !l.orderQty || l.orderQty <= 0 || (l.part.isCustomerLotRequired && !l.customerLot.trim()))
    if (lines.length === 0) { setError('품목을 담으세요.'); return }
    if (missing >= 0) { setError(`${missing + 1}행: 수량${lines[missing].part.isCustomerLotRequired ? '·고객LOT(필수 품목)' : ''}을 입력하세요.`); return }
    setSaving(true)
    try {
      const r = await api<{ salesOrderNo: string; items: { orderItemNo: string; partName: string; orderQty: number }[] }>('/api/sales-orders', {
        method: 'POST',
        body: {
          orderDate: h.orderDate.format(DATE), dueDate: h.dueDate?.format(DATE) ?? null, customerId: h.customerId, isReturn: h.isReturn, remark: h.remark,
          items: lines.map((l) => ({
            partId: l.part.partId, heatProcessId: l.heatProcessId, orderQty: l.orderQty, unitPrice: l.unitPrice, customerLot: l.customerLot,
            coilNo: l.coilNo, customerWorkOrderNo: l.customerWorkOrderNo, priority: l.priority, isSeparatelyManaged: l.isSeparatelyManaged, remark: l.remark,
          })),
        },
      })
      modal.success({
        title: `입고 ${r.items.length}건 등록 (${r.salesOrderNo})`,
        content: <ul style={{ paddingLeft: 18, margin: 0 }}>{r.items.map((i) => <li key={i.orderItemNo}><b>{i.orderItemNo}</b> {i.partName} × {qty(i.orderQty)}</li>)}</ul>,
      })
      onSaved()
    } catch (e) {
      setError(e instanceof ApiError && e.errors ? Object.entries(e.errors).map(([k, v]) => `${k.replace(/items\[(\d+)\]/, (_, i: string) => `${Number(i) + 1}행`)}: ${v.join(' ')}`).join(' / ')
        : e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <EditorWindow onClose={onClose} size={1400} title="수주 등록" extra={<Button type="primary" loading={saving} onClick={() => void save()}>등록</Button>}>
      <Form form={form} layout="vertical" initialValues={{ orderDate: dayjs(), isReturn: false }}>
        <Row gutter={12}>
          <Col span={4}><Form.Item name="orderDate" label="입고일" rules={[{ required: true }]}><DatePicker style={{ width: '100%' }} /></Form.Item></Col>
          <Col span={6}>
            <Form.Item name="customerId" label="거래처" rules={[{ required: true }]}>
              <Select showSearch optionFilterProp="label" options={customers.options}
                onChange={() => lines.length > 0 && modal.confirm({ title: '거래처를 바꾸면 담은 품목을 비웁니다.', onOk: () => setLines([]) })} />
            </Form.Item>
          </Col>
          <Col span={4}><Form.Item name="dueDate" label="납기"><DatePicker style={{ width: '100%' }} /></Form.Item></Col>
          <Col span={3}><Form.Item name="isReturn" label="반입(재입고)" valuePropName="checked"><Checkbox /></Form.Item></Col>
          <Col span={7}><Form.Item name="remark" label="비고"><Input maxLength={255} /></Form.Item></Col>
        </Row>
      </Form>

      <Space style={{ marginBottom: 8 }} wrap>
        <Select<number> style={{ width: 420 }} showSearch filterOption={false} value={null} disabled={!customerId} loading={candidates.isFetching}
          placeholder={customerId ? <><PlusOutlined /> 품목 담기 — 품명·품번·기종 검색</> : '거래처를 먼저 고르세요'}
          onSearch={setPartSearch}
          options={(candidates.data ?? []).map((p) => ({
            value: p.partId,
            label: `${p.partName}${p.partNumber ? ` · ${p.partNumber}` : ''}${p.model ? ` · ${p.model}` : ''}${p.customerPartCode ? ` [${p.customerPartCode}]` : ''}${p.isCustomerPart ? '' : ' (거래처 품목 아님)'}`,
          }))}
          onChange={(id) => { const p = candidates.data?.find((x) => x.partId === id); if (p) setLines((ls) => [...ls, lineOf(p)]) }} />
        <Space size={4}><Switch size="small" checked={allParts} onChange={setAllParts} />전체 품목에서 찾기</Space>
      </Space>

      <Table<OrderLine> size="small" bordered pagination={false} rowKey="key" dataSource={lines} scroll={{ x: 1300 }}
        locale={{ emptyText: '위에서 품목을 담으세요 (같은 품목을 LOT별로 여러 번 담을 수 있습니다)' }}
        columns={[
          { title: '#', width: 40, render: (_: unknown, __, i) => i + 1 },
          { title: '품목', width: 200, render: (_: unknown, l) => <>{l.part.partName}<br /><Typography.Text type="secondary">{[l.part.partNumber, l.part.specification].filter(Boolean).join(' · ')}</Typography.Text></> },
          {
            title: '공정', width: 130, render: (_: unknown, l) => (
              <Select size="small" style={{ width: '100%' }} value={l.heatProcessId} placeholder="미지정" allowClear status={l.heatProcessId ? undefined : 'warning'}
                options={l.part.heatProcesses.map((h) => ({ value: h.heatProcessId, label: h.heatProcessName }))} onChange={(v) => setLine(l.key, { heatProcessId: v ?? null })} />
            ),
          },
          { title: '수량', width: 100, render: (_: unknown, l) => <InputNumber size="small" min={0} value={l.orderQty} status={l.orderQty ? undefined : 'error'} onChange={(v) => setLine(l.key, { orderQty: v })} style={{ width: '100%' }} /> },
          { title: '단가', width: 100, render: (_: unknown, l) => <InputNumber size="small" min={0} value={l.unitPrice} onChange={(v) => setLine(l.key, { unitPrice: v })} style={{ width: '100%' }} /> },
          {
            title: '고객LOT', width: 120, render: (_: unknown, l) => (
              <Input size="small" value={l.customerLot} maxLength={100} placeholder={l.part.isCustomerLotRequired ? '필수' : undefined}
                status={l.part.isCustomerLotRequired && !l.customerLot.trim() ? 'error' : undefined} onChange={(e) => setLine(l.key, { customerLot: e.target.value })} />
            ),
          },
          { title: '코일번호', width: 100, render: (_: unknown, l) => <Input size="small" value={l.coilNo} maxLength={100} onChange={(e) => setLine(l.key, { coilNo: e.target.value })} /> },
          { title: '작업지시번호', width: 110, render: (_: unknown, l) => <Input size="small" value={l.customerWorkOrderNo} maxLength={100} onChange={(e) => setLine(l.key, { customerWorkOrderNo: e.target.value })} /> },
          {
            title: '우선순위', width: 90, render: (_: unknown, l) => (
              <Select size="small" style={{ width: '100%' }} value={String(l.priority)} onChange={(v) => setLine(l.key, { priority: Number(v) })}
                options={codes.options('PRIORITY').map((c) => ({ value: c.code, label: c.codeName }))} />
            ),
          },
          { title: '별도관리', width: 70, align: 'center', render: (_: unknown, l) => <Checkbox checked={l.isSeparatelyManaged} onChange={(e) => setLine(l.key, { isSeparatelyManaged: e.target.checked })} /> },
          { title: '비고', render: (_: unknown, l) => <Input size="small" value={l.remark} maxLength={255} onChange={(e) => setLine(l.key, { remark: e.target.value })} /> },
          {
            title: '', width: 70, render: (_: unknown, l) => (
              <Space size={0}>
                <Tooltip title="같은 품목 행 추가 (다른 LOT)">
                  <Button size="small" type="text" icon={<CopyOutlined />} onClick={() => setLines((ls) => { const i = ls.findIndex((x) => x.key === l.key); return [...ls.slice(0, i + 1), { ...l, key: ++lineSeq, orderQty: null, customerLot: '', coilNo: '' }, ...ls.slice(i + 1)] })} />
                </Tooltip>
                <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={() => setLines((ls) => ls.filter((x) => x.key !== l.key))} />
              </Space>
            ),
          },
        ]} />
      {error && <Alert style={{ marginTop: 12 }} type="error" showIcon title={error} />}
    </EditorWindow>
  )
}

interface ItemForm {
  heatProcessId: number | null
  orderQty: number
  unitPrice: number | null
  customerLot?: string
  coilNo?: string
  customerWorkOrderNo?: string
  priority: string
  isSeparatelyManaged: boolean
  remark?: string
}

/** 입고 행 1건 보기·수정·취소 (구 F_IncomeAddForm(inno) 수정 모드 + 목록 삭제) */
function ItemWindow({ itemId, menuKey, onClose, onChanged }: { itemId: number; menuKey: string; onClose: () => void; onChanged: () => void }) {
  const { message } = App.useApp()
  const canUpdate = useCan(menuKey, 'update')
  const canDelete = useCan(menuKey, 'delete')
  const codes = useCommonCodes()
  const [form] = Form.useForm<ItemForm>()
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const detail = useQuery({
    queryKey: [...queryKeys.sales, 'item', itemId],
    queryFn: ({ signal }) => api<OrderItem>(`/api/sales-orders/items/${itemId}`, { signal }),
  })
  const d = detail.data
  const formKey = useDataVersion(d)
  // 품목에 연결된 공정 (거래처 품목 아니어도 전체 검색으로)
  const routes = useQuery({
    queryKey: [...queryKeys.sales, 'candidates', 'routes', d?.customerId, d?.partId],
    queryFn: ({ signal }) => api<PartCandidate[]>(`/api/sales-orders/part-candidates?customerId=${d!.customerId}&all=true&search=${encodeURIComponent(d!.partCode)}`, { signal }),
    enabled: !!d,
  })
  const routeOptions = useMemo(() => {
    const own = routes.data?.find((p) => p.partId === d?.partId)?.heatProcesses ?? []
    const opts = own.map((h) => ({ value: h.heatProcessId, label: h.heatProcessName }))
    // 현재 공정이 품목 연결에서 빠졌어도 표시
    if (d?.heatProcessId && !opts.some((o) => o.value === d.heatProcessId)) opts.push({ value: d.heatProcessId, label: d.heatProcessName ?? `#${d.heatProcessId}` })
    return opts
  }, [routes.data, d])

  if (!d) return null
  const editable = canUpdate && d.status !== 'CANCELLED'

  const save = async () => {
    const v = await form.validateFields()
    setError(null)
    setSaving(true)
    try {
      await api(`/api/sales-orders/items/${itemId}`, { method: 'PUT', body: { ...v, priority: Number(v.priority), rowVersion: d.rowVersion } })
      message.success('저장했습니다.')
      await detail.refetch()
      onChanged()
    } catch (e) {
      setError(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }
  const cancel = async () => {
    try {
      await api(`/api/sales-orders/items/${itemId}/cancel`, { method: 'POST', body: { rowVersion: d.rowVersion, reason } })
      message.success(`${d.orderItemNo} 을 취소했습니다.`)
      onChanged()
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }

  return (
    <EditorWindow onClose={onClose} size={900}
      title={<Space>{d.orderItemNo}<Tag color={codes.attr<{ color?: string }>('ORDER_STATUS', d.status)?.color}>{codes.name('ORDER_STATUS', d.status)}</Tag></Space>}
      extra={(
        <Space>
          <PrintButton purposeCode="PROCESS_SHEET" sourceIds={[d.salesOrderItemId]}>공정이동표</PrintButton>
          <PrintButton purposeCode="PRODUCT_LABEL" sourceIds={[d.salesOrderItemId]}>라벨</PrintButton>
          {editable && canDelete && (
            <Popconfirm title="이 입고 행을 취소합니다" okText="취소 처리" okButtonProps={{ danger: true }} onConfirm={() => void cancel()}
              description={<Input placeholder="사유 (선택)" value={reason} onChange={(e) => setReason(e.target.value)} />}>
              <Button danger disabled={d.isScheduledOrInput || d.shipmentQty > 0}>입고 취소</Button>
            </Popconfirm>
          )}
          {editable && <Button type="primary" loading={saving} onClick={() => void save()}>저장</Button>}
        </Space>
      )}>
      <Descriptions size="small" column={3} bordered style={{ marginBottom: 16 }} items={[
        { label: '입고일', children: dayjs(d.orderDate).format(DATE) },
        { label: '묶음', children: d.salesOrderNo },
        { label: '거래처', children: d.customerName },
        { label: '품목', span: 2, children: <>{d.partName} <Typography.Text type="secondary">{[d.partNumber, d.specification, d.model].filter(Boolean).join(' · ')}</Typography.Text></> },
        { label: '재질', children: d.material },
        { label: '요구경도', children: d.hardness },
        { label: '심부경도', children: d.coreHardness },
        { label: '경화층', children: d.caseDepth },
        { label: '투입(주공정)', children: qty(d.mainInputQty) },
        { label: '출하', children: qty(d.shipmentQty) },
        { label: '출하 잔량', children: qty(d.remainingShipmentQty) },
      ]} />
      {d.isScheduledOrInput && <Alert type="info" showIcon style={{ marginBottom: 12 }} title={`계획·투입이 있어 공정은 바꿀 수 없고, 수량은 ${qty(d.usedQty)} 이상이어야 합니다.`} />}
      <Form key={formKey} form={form} layout="vertical" disabled={!editable} initialValues={{
        heatProcessId: d.heatProcessId, orderQty: d.orderQty, unitPrice: d.unitPrice, customerLot: d.customerLot, coilNo: d.coilNo,
        customerWorkOrderNo: d.customerWorkOrderNo, priority: String(d.priority), isSeparatelyManaged: d.isSeparatelyManaged, remark: d.remark,
      }}>
        <Row gutter={12}>
          <Col span={8}>
            <Form.Item name="heatProcessId" label="공정">
              <Select allowClear disabled={d.isScheduledOrInput} options={routeOptions} placeholder="미지정" />
            </Form.Item>
          </Col>
          <Col span={5}><Form.Item name="orderQty" label="수량" rules={[{ required: true }]}><InputNumber min={d.usedQty || 0} style={{ width: '100%' }} /></Form.Item></Col>
          <Col span={5}><Form.Item name="unitPrice" label={`단가 (${codes.name('PRICE_BASIS', d.priceBasis)})`}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item></Col>
          <Col span={6}>
            <Form.Item name="priority" label="우선순위">
              <Select options={codes.options('PRIORITY').map((c) => ({ value: c.code, label: c.codeName }))} />
            </Form.Item>
          </Col>
          <Col span={8}><Form.Item name="customerLot" label="고객LOT"><Input maxLength={100} /></Form.Item></Col>
          <Col span={8}><Form.Item name="coilNo" label="코일번호"><Input maxLength={100} /></Form.Item></Col>
          <Col span={8}><Form.Item name="customerWorkOrderNo" label="작업지시번호"><Input maxLength={100} /></Form.Item></Col>
          <Col span={4}><Form.Item name="isSeparatelyManaged" label="별도관리" valuePropName="checked"><Checkbox /></Form.Item></Col>
          <Col span={20}><Form.Item name="remark" label="비고"><Input maxLength={255} /></Form.Item></Col>
        </Row>
      </Form>
      {error && <Alert type="error" showIcon title={error} />}
    </EditorWindow>
  )
}
