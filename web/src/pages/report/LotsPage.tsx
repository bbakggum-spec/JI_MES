import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Col, DatePicker, Descriptions, Empty, Input, Row, Segmented, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState, type ReactNode } from 'react'
import { ApiError, api } from '../../api/client'
import { useClientSettings } from '../../hooks/useClientSettings'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'

interface LotStatus {
  productionWorkId: number
  lotNo: string
  workDate: string
  equipmentId: number | null
  equipmentName: string | null
  unitProcessName: string | null
  heatProcessName: string | null
  isMainProcess: boolean
  isRework: boolean
  status: string
  actualStartAt: string | null
  actualEndAt: string | null
  submitLotNo: string | null
  inputQty: number
  goodQty: number
  postLotCount: number
  postDoneCount: number
  inspectionCount: number
  inspectionDoneCount: number
  openDefectCount: number
  shipmentQty: number
  orderSummary: string | null
  partSummary: string | null
  customerSummary: string | null
}

interface TraceLot { productionWorkId: number; lotNo: string; unitProcessName: string | null; equipmentName: string | null; status: string; isRework: boolean; workDate: string; actualStartAt: string | null; actualEndAt: string | null; inputQty: number; goodQty: number; orderSummary: string | null }
interface Trace {
  main: LotStatus
  inputs: { productionWorkInputId: number; orderItemNo: string; customerName: string | null; customerLot: string | null; partName: string | null; partNumber: string | null; inputQty: number; defectQty: number; goodQty: number }[]
  pre: TraceLot[]
  post: TraceLot[]
  rework: TraceLot[]
  inspections: { inspectionId: number; inspectionNo: string; inspectionType: string; inspectionDate: string; lotNo: string; status: string; decision: string | null; targets: string | null }[]
  defects: { defectOccurrenceId: number; defectDate: string; lotNo: string | null; unitProcessName: string | null; orderItemNo: string; defectQty: number; status: string; decision: string | null; reworkLotNo: string | null }[]
  shipments: { shipmentId: number; shipmentNo: string; shipmentDate: string; customerName: string | null; orderItemNo: string; shipmentQty: number; testSpecimenQty: number; status: string; closingStatus: string }[]
}
interface Resolved { kind: string; code: string; mains: { productionWorkId: number; lotNo: string; unitProcessName: string | null; equipmentName: string | null; workDate: string; status: string; submitLotNo: string | null }[] }

const DATE = 'YYYY-MM-DD'
const TIME = 'MM-DD HH:mm'
const qty = (n: number | null | undefined) => (n == null ? '' : n.toLocaleString(undefined, { maximumFractionDigits: 3 }))
const errorText = (e: unknown) => (e instanceof ApiError ? e.message : e instanceof Error ? e.message : String(e))

/** LOT 현황·추적 — 구 F_WorkHistoryForm · F_ProductionStatus (설계 §3.4·§3.5·§24.1) */
export default function LotsPage() {
  const [tab, setTab] = useState('status')
  const [traceCode, setTraceCode] = useState<string | null>(null)
  return (
    <>
      <Typography.Title level={4} style={{ margin: '0 0 8px' }}>LOT 현황·추적</Typography.Title>
      <Tabs activeKey={tab} onChange={setTab} items={[
        { key: 'status', label: 'LOT 현황', children: <StatusTab onTrace={(lot) => { setTraceCode(lot); setTab('trace') }} /> },
        { key: 'trace', label: 'LOT 추적 (주 LOT 기준)', children: <TraceTab key={traceCode ?? ''} initialCode={traceCode} /> },
      ]} />
    </>
  )
}

function StatusTab({ onTrace }: { onTrace: (lotNo: string) => void }) {
  const settings = useClientSettings()
  const codes = useCommonCodes()
  const equipment = useOptions('/api/master/equipment/options')
  const units = useOptions('/api/master/unit_process/options')
  const [range, setRange] = useState<[Dayjs, Dayjs] | null>(null)
  const [equipmentId, setEquipmentId] = useState<number | undefined>()
  const [unitProcessId, setUnitProcessId] = useState<number | undefined>()
  const [status, setStatus] = useState<string | undefined>()
  const [mainOnly, setMainOnly] = useState(false)
  const [search, setSearch] = useState('')
  const effectiveRange = range ?? (settings.orderListDays ? [dayjs().subtract(settings.orderListDays, 'day'), dayjs()] as [Dayjs, Dayjs] : null)
  const params = new URLSearchParams()
  if (effectiveRange) { params.set('from', effectiveRange[0].format(DATE)); params.set('to', effectiveRange[1].format(DATE)) }
  if (equipmentId) params.set('equipmentId', String(equipmentId))
  if (unitProcessId) params.set('unitProcessId', String(unitProcessId))
  if (status) params.set('status', status)
  if (mainOnly) params.set('mainOnly', 'true')
  if (search) params.set('search', search)
  const list = useQuery({
    queryKey: [...queryKeys.reports, 'lots', params.toString()],
    queryFn: ({ signal }) => api<LotStatus[]>(`/api/reports/lots?${params}`, { signal }),
    enabled: effectiveRange !== null,
    refetchInterval: settings.refreshIntervalMs,
  })
  return (
    <>
      <Space wrap style={{ marginBottom: 8 }}>
        <DatePicker.RangePicker value={effectiveRange} allowClear={false} onChange={(v) => v?.[0] && v[1] && setRange([v[0], v[1]])} />
        <Select allowClear showSearch optionFilterProp="label" placeholder="설비 전체" style={{ width: 140 }} value={equipmentId} onChange={setEquipmentId} options={equipment.options} />
        <Select allowClear showSearch optionFilterProp="label" placeholder="단위공정 전체" style={{ width: 130 }} value={unitProcessId} onChange={setUnitProcessId} options={units.options} />
        <Select allowClear placeholder="상태 전체" style={{ width: 110 }} value={status} onChange={setStatus}
          options={codes.options('WORK_STATUS').filter((c) => c.code !== 'CANCELLED').map((c) => ({ value: c.code, label: c.codeName }))} />
        <Space size={4}><Switch size="small" checked={mainOnly} onChange={setMainOnly} />주 LOT 만</Space>
        <Input.Search allowClear placeholder="LOT·제출 LOT·입고번호·품명·거래처" style={{ width: 260 }} onSearch={(v) => setSearch(v.trim())} />
      </Space>
      <Table<LotStatus> rowKey="productionWorkId" size="small" loading={list.isFetching} dataSource={list.data ?? []} scroll={{ x: 1500 }}
        pagination={{ defaultPageSize: 50, showSizeChanger: true, showTotal: (n) => `${n}건` }}
        onRow={(r) => ({ onClick: () => onTrace(r.lotNo), style: { cursor: 'pointer' } })}
        columns={[
          { title: '작업일', dataIndex: 'workDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
          {
            title: 'LOT', dataIndex: 'lotNo', width: 170, fixed: 'left', render: (lot: string, r) => (
              <Space size={2}>{lot}{r.isMainProcess && <Tag color="gold">주</Tag>}{r.isRework && <Tag color="purple">재</Tag>}</Space>
            ),
          },
          { title: '설비', dataIndex: 'equipmentName', width: 90 },
          { title: '단위공정', dataIndex: 'unitProcessName', width: 90 },
          { title: '상태', dataIndex: 'status', width: 70, render: (s: string) => <Tag color={s === 'INPUT' ? 'processing' : s === 'COMPLETED' ? 'success' : 'default'}>{codes.name('WORK_STATUS', s)}</Tag> },
          { title: '입고번호', dataIndex: 'orderSummary', width: 140, ellipsis: true },
          { title: '품명', dataIndex: 'partSummary', ellipsis: true },
          { title: '거래처', dataIndex: 'customerSummary', width: 100, ellipsis: true },
          { title: '투입', dataIndex: 'inputQty', width: 70, align: 'right', render: qty },
          { title: '양품', dataIndex: 'goodQty', width: 70, align: 'right', render: qty },
          { title: '후공정', width: 70, align: 'center', render: (_: unknown, r) => r.postLotCount > 0 && `${r.postDoneCount}/${r.postLotCount}` },
          { title: '검사', width: 60, align: 'center', render: (_: unknown, r) => r.inspectionCount > 0 && `${r.inspectionDoneCount}/${r.inspectionCount}` },
          { title: '부적합', dataIndex: 'openDefectCount', width: 65, align: 'center', render: (n: number) => n > 0 && <Tag color="red">{n}</Tag> },
          { title: '출하', dataIndex: 'shipmentQty', width: 70, align: 'right', render: (n: number) => n > 0 && qty(n) },
          { title: '실적', width: 150, render: (_: unknown, r) => r.actualStartAt && `${dayjs(r.actualStartAt).format(TIME)} ~ ${r.actualEndAt ? dayjs(r.actualEndAt).format('HH:mm') : ''}` },
        ]} />
      <Typography.Text type="secondary">행을 누르면 그 LOT 을 주 LOT 기준으로 추적합니다. 후공정·검사 = 완료/전체.</Typography.Text>
    </>
  )
}

function TraceTab({ initialCode }: { initialCode: string | null }) {
  const codes = useCommonCodes()
  const [code, setCode] = useState(initialCode ?? '')
  const [query, setQuery] = useState(initialCode)
  const [mainId, setMainId] = useState<number | null>(null)
  const resolved = useQuery({
    queryKey: [...queryKeys.reports, 'resolve', query],
    queryFn: ({ signal }) => api<Resolved>(`/api/reports/trace/resolve?code=${encodeURIComponent(query!)}`, { signal }),
    enabled: !!query,
    retry: false,
  })
  const effectiveMain = mainId ?? resolved.data?.mains[0]?.productionWorkId ?? null
  const trace = useQuery({
    queryKey: [...queryKeys.reports, 'trace', effectiveMain],
    queryFn: ({ signal }) => api<Trace>(`/api/reports/trace/${effectiveMain}`, { signal }),
    enabled: effectiveMain !== null,
  })
  const t = trace.data
  const lotColumns = [
    { title: 'LOT', dataIndex: 'lotNo', width: 160, render: (lot: string, r: TraceLot) => <Space size={2}>{lot}{r.isRework && <Tag color="purple">재</Tag>}</Space> },
    { title: '단위공정', dataIndex: 'unitProcessName', width: 100 },
    { title: '설비', dataIndex: 'equipmentName', width: 100 },
    { title: '상태', dataIndex: 'status', width: 70, render: (s: string) => codes.name('WORK_STATUS', s) },
    { title: '작업일', dataIndex: 'workDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
    { title: '실적', width: 150, render: (_: unknown, r: TraceLot) => r.actualStartAt && `${dayjs(r.actualStartAt).format(TIME)} ~ ${r.actualEndAt ? dayjs(r.actualEndAt).format('HH:mm') : ''}` },
    { title: '투입', dataIndex: 'inputQty', width: 70, align: 'right' as const, render: qty },
    { title: '양품', dataIndex: 'goodQty', width: 70, align: 'right' as const, render: qty },
    { title: '입고번호', dataIndex: 'orderSummary', ellipsis: true },
  ]
  const section = (title: string, count: number, children: ReactNode, note?: string) => (
    <Card size="small" title={<>{title} <Typography.Text type="secondary">({count})</Typography.Text>{note && <Typography.Text type="secondary" style={{ fontSize: 12, marginLeft: 8 }}>{note}</Typography.Text>}</>}
      style={{ marginBottom: 12 }}>{count === 0 ? <Typography.Text type="secondary">없음</Typography.Text> : children}</Card>
  )

  return (
    <>
      <Space wrap style={{ marginBottom: 12 }}>
        <Input.Search enterButton="추적" style={{ width: 380 }} value={code} onChange={(e) => setCode(e.target.value)} placeholder="작업 LOT번호 · 제출 LOT · 입고번호(수주번호)"
          onSearch={(v) => { setMainId(null); setQuery(v.trim() || null) }} />
        {resolved.data && resolved.data.mains.length > 1 && (
          <Segmented value={effectiveMain ?? undefined} onChange={(v) => setMainId(v as number)}
            options={resolved.data.mains.map((m) => ({ value: m.productionWorkId, label: m.lotNo }))} />
        )}
      </Space>
      {resolved.error && <Alert type="warning" showIcon title={errorText(resolved.error)} />}
      {resolved.data && resolved.data.mains.length === 0 && <Empty description="이 번호는 아직 주공정 LOT 이 없습니다 (전공정까지만 진행)." />}
      {!query && <Empty description="LOT번호·제출 LOT·입고번호를 넣으면 주 LOT 기준으로 전공정·후공정·재작업·검사·부적합·출하를 모아 봅니다." />}
      {t && (
        <>
          <Card size="small" style={{ marginBottom: 12 }} title={<Space>주 LOT <Typography.Text strong style={{ fontSize: 16 }}>{t.main.lotNo}</Typography.Text>
            <Tag color={t.main.status === 'INPUT' ? 'processing' : t.main.status === 'COMPLETED' ? 'success' : 'default'}>{codes.name('WORK_STATUS', t.main.status)}</Tag></Space>}>
            <Descriptions size="small" column={{ xs: 1, md: 4 }} items={[
              { label: '설비', children: t.main.equipmentName },
              { label: '단위공정', children: t.main.unitProcessName },
              { label: '공정', children: t.main.heatProcessName },
              { label: '제출 LOT', children: t.main.submitLotNo },
              { label: '작업일', children: dayjs(t.main.workDate).format(DATE) },
              { label: '실적', children: t.main.actualStartAt && `${dayjs(t.main.actualStartAt).format(TIME)} ~ ${t.main.actualEndAt ? dayjs(t.main.actualEndAt).format('HH:mm') : '진행 중'}` },
              { label: '투입 / 양품', children: `${qty(t.main.inputQty)} / ${qty(t.main.goodQty)}` },
              { label: '출하', children: qty(t.main.shipmentQty) },
            ]} />
            <Table size="small" style={{ marginTop: 8 }} rowKey="productionWorkInputId" pagination={false} dataSource={t.inputs} scroll={{ x: 800 }} columns={[
              { title: '입고번호', dataIndex: 'orderItemNo', width: 130 },
              { title: '거래처', dataIndex: 'customerName', width: 110 },
              { title: '품명', render: (_: unknown, r) => <>{r.partName} <Typography.Text type="secondary">{r.partNumber}</Typography.Text></> },
              { title: '고객LOT', dataIndex: 'customerLot', width: 100 },
              { title: '투입', dataIndex: 'inputQty', width: 80, align: 'right', render: qty },
              { title: '부적합', dataIndex: 'defectQty', width: 80, align: 'right', render: qty },
              { title: '양품', dataIndex: 'goodQty', width: 80, align: 'right', render: qty },
            ]} />
          </Card>
          <Row gutter={12}>
            <Col xs={24} xl={12}>{section('전공정', t.pre.length, <Table size="small" rowKey="productionWorkId" pagination={false} dataSource={t.pre} columns={lotColumns} scroll={{ x: 900 }} />, '같은 수주의 주공정 전 LOT (수주 단위 연결)')}</Col>
            <Col xs={24} xl={12}>{section('후공정', t.post.length, <Table size="small" rowKey="productionWorkId" pagination={false} dataSource={t.post} columns={lotColumns} scroll={{ x: 900 }} />)}</Col>
          </Row>
          {section('재작업', t.rework.length, <Table size="small" rowKey="productionWorkId" pagination={false} dataSource={t.rework} columns={lotColumns} scroll={{ x: 900 }} />)}
          <Row gutter={12}>
            <Col xs={24} xl={12}>{section('검사', t.inspections.length, <Table size="small" rowKey="inspectionId" pagination={false} dataSource={t.inspections} columns={[
              { title: '검사번호', dataIndex: 'inspectionNo', width: 130 },
              { title: '구분', dataIndex: 'inspectionType', width: 80, render: (v: string) => codes.name('INSPECTION_TYPE', v) },
              { title: '일자', dataIndex: 'inspectionDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
              { title: 'LOT', dataIndex: 'lotNo', width: 140 },
              { title: '판정', dataIndex: 'decision', width: 80, render: (v: string | null) => v && <Tag color={codes.attr<{ color?: string }>('DECISION', v)?.color}>{codes.name('DECISION', v)}</Tag> },
              { title: '상태', dataIndex: 'status', width: 100, render: (v: string) => codes.name('INSPECTION_STATUS', v) },
              { title: '대상', dataIndex: 'targets', ellipsis: true },
            ]} />)}</Col>
            <Col xs={24} xl={12}>{section('부적합', t.defects.length, <Table size="small" rowKey="defectOccurrenceId" pagination={false} dataSource={t.defects} columns={[
              { title: '일자', dataIndex: 'defectDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
              { title: '발생 LOT', width: 160, render: (_: unknown, r) => `${r.lotNo ?? ''} ${r.unitProcessName ?? ''}` },
              { title: '입고번호', dataIndex: 'orderItemNo', width: 125 },
              { title: '수량', dataIndex: 'defectQty', width: 70, align: 'right', render: qty },
              { title: '상태', dataIndex: 'status', width: 90, render: (v: string) => <Tag color={codes.attr<{ color?: string }>('DEFECT_STATUS', v)?.color}>{codes.name('DEFECT_STATUS', v)}</Tag> },
              { title: '처리', dataIndex: 'decision', width: 70, render: (v: string | null) => v && codes.name('DEFECT_ACTION', v) },
              { title: '재작업 LOT', dataIndex: 'reworkLotNo', width: 140 },
            ]} />)}</Col>
          </Row>
          {section('출하', t.shipments.length, <Table size="small" rowKey={(r) => `${r.shipmentId}:${r.orderItemNo}`} pagination={false} dataSource={t.shipments} columns={[
            { title: '전표번호', dataIndex: 'shipmentNo', width: 130 },
            { title: '출하일', dataIndex: 'shipmentDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
            { title: '거래처', dataIndex: 'customerName', width: 120 },
            { title: '입고번호', dataIndex: 'orderItemNo', width: 130 },
            { title: '출하', dataIndex: 'shipmentQty', width: 80, align: 'right', render: qty },
            { title: '시험편', dataIndex: 'testSpecimenQty', width: 70, align: 'right', render: qty },
            { title: '마감', dataIndex: 'closingStatus', width: 90, render: (v: string) => <Tag color={codes.attr<{ color?: string }>('CLOSING_STATUS', v)?.color}>{codes.name('CLOSING_STATUS', v)}</Tag> },
          ]} />)}
        </>
      )}
    </>
  )
}
