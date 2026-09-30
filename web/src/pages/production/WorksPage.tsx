import { CheckCircleOutlined, DeleteOutlined, PlayCircleOutlined, PlusOutlined, ReloadOutlined, ScanOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert, App, Badge, Button, Card, Col, DatePicker, Descriptions, Empty, Form, Input, InputNumber, Modal, Popconfirm, Row, Select, Space,
  Table, Tag, Timeline, Typography, theme, type InputRef,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useRef, useState, type ReactNode } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { useClientSettings } from '../../hooks/useClientSettings'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useDataVersion } from '../../hooks/useDataVersion'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import ConditionGrid from '../master/ConditionGrid'
import { COMMON, cellKey, newStep, toConditions, type GridItem, type GridLayout, type GridValues } from '../master/conditionGridModel'
import type { PageProps } from '../registry'
import type { InputCandidate, ScanResult, StandardCandidate, Work, WorkBoard, WorkCondition, WorkDetail } from './workTypes'

const TIME = 'MM-DD HH:mm'
const qty = (n: number | null | undefined) => (n == null ? '' : n.toLocaleString(undefined, { maximumFractionDigits: 3 }))
const errorText = (e: unknown) => (e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))

/** 작업(투입) — 구 F_GasForm. 왼쪽 = 설비별 배정·진행 LOT, 오른쪽 = 선택 LOT 의 투입·시작·완료·표준·조건 (설계 §23.4) */
export default function WorksPage({ menuKey }: PageProps) {
  const canCreate = useCan(menuKey, 'create')
  const settings = useClientSettings()
  const types = useOptions('/api/master/equipment_type/options')
  const codes = useCommonCodes()
  const { token } = theme.useToken()
  const [equipmentTypeId, setEquipmentTypeId] = useState<number | undefined>()
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [adHocFor, setAdHocFor] = useState<{ equipmentId: number; name: string } | null>(null)
  const board = useQuery({
    queryKey: [...queryKeys.works, 'board', equipmentTypeId],
    queryFn: ({ signal }) => api<WorkBoard>(`/api/works/board${equipmentTypeId ? `?equipmentTypeId=${equipmentTypeId}` : ''}`, { signal }),
    refetchInterval: settings.refreshIntervalMs,
  })
  const byEquipment = useMemo(() => {
    const map = new Map<number, Work[]>()
    for (const w of board.data?.works ?? []) map.set(w.equipmentId ?? 0, [...(map.get(w.equipmentId ?? 0) ?? []), w])
    return map
  }, [board.data])
  const statusTag = (s: string) => <Tag color={s === 'INPUT' ? 'processing' : s === 'COMPLETED' ? 'success' : 'default'}>{codes.name('WORK_STATUS', s)}</Tag>

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>작업(투입)</Typography.Title>
        <Space wrap>
          <Select allowClear placeholder="설비유형 전체" style={{ width: 160 }} value={equipmentTypeId} onChange={setEquipmentTypeId} options={types.options} />
          <Button icon={<ReloadOutlined />} onClick={() => void board.refetch()}>새로고침</Button>
        </Space>
      </Space>
      <Row gutter={12}>
        <Col xs={24} lg={7} xxl={6}>
          <Card size="small" title={`설비별 LOT${board.data ? ` · 작업일 ${dayjs(board.data.workDate).format('MM-DD')}` : ''}`} styles={{ body: { padding: 8 } }}>
            {(board.data?.equipment ?? []).map((e) => (
              <div key={e.equipmentId} style={{ marginBottom: 10 }}>
                <Space style={{ width: '100%', justifyContent: 'space-between' }}>
                  <Typography.Text strong>{e.equipmentName}</Typography.Text>
                  {canCreate && <Button size="small" type="link" icon={<PlusOutlined />} onClick={() => setAdHocFor({ equipmentId: e.equipmentId, name: e.equipmentName })}>즉시 작업</Button>}
                </Space>
                {(byEquipment.get(e.equipmentId) ?? []).length === 0 && <Typography.Text type="secondary" style={{ fontSize: 12, paddingLeft: 8 }}>배정된 LOT 없음</Typography.Text>}
                {(byEquipment.get(e.equipmentId) ?? []).map((w) => (
                    <div key={w.productionWorkId} onClick={() => setSelectedId(w.productionWorkId)}
                      style={{ cursor: 'pointer', padding: '4px 8px', background: w.productionWorkId === selectedId ? token.colorPrimaryBg : undefined, borderRadius: 4 }}>
                      <Space orientation="vertical" size={0} style={{ width: '100%' }}>
                        <Space style={{ width: '100%', justifyContent: 'space-between' }}>
                          <Typography.Text strong>{w.lotNo}</Typography.Text>{statusTag(w.status)}
                        </Space>
                        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                          {w.unitProcessName} · {w.inputCount}건 {qty(w.inputQty)}
                          {w.status === 'INPUT' && w.actualStartAt ? ` · ${dayjs(w.actualStartAt).format('HH:mm')} 시작` : w.plannedStartAt ? ` · 계획 ${dayjs(w.plannedStartAt).format(TIME)}` : ''}
                        </Typography.Text>
                      </Space>
                    </div>
                ))}
              </div>
            ))}
            {board.data && board.data.equipment.length === 0 && <Empty description="설비가 없습니다" />}
          </Card>
        </Col>
        <Col xs={24} lg={17} xxl={18}>
          {selectedId ? <WorkPanel key={selectedId} id={selectedId} menuKey={menuKey} /> : (
            <Card><Empty description="왼쪽에서 LOT 을 고르세요. 계획에서 작업지시한 LOT 이 나오며, 계획 없이 바로 작업하려면 [즉시 작업]." /></Card>
          )}
        </Col>
      </Row>
      {adHocFor && <AdHocModal target={adHocFor} onClose={() => setAdHocFor(null)} onCreated={(id) => { setAdHocFor(null); setSelectedId(id); void board.refetch() }} />}
    </>
  )
}

function AdHocModal({ target, onClose, onCreated }: { target: { equipmentId: number; name: string }; onClose: () => void; onCreated: (id: number) => void }) {
  const { message } = App.useApp()
  const units = useOptions('/api/master/unit_process/options')
  const [unitProcessId, setUnitProcessId] = useState<number | undefined>()
  const [saving, setSaving] = useState(false)
  const create = async () => {
    if (!unitProcessId) return
    setSaving(true)
    try {
      const r = await api<{ productionWorkId: number; lotNo: string }>('/api/works', { method: 'POST', body: { equipmentId: target.equipmentId, unitProcessId } })
      message.success(`작업 LOT ${r.lotNo} 을 만들었습니다.`)
      onCreated(r.productionWorkId)
    } catch (e) {
      message.error(errorText(e))
    } finally {
      setSaving(false)
    }
  }
  return (
    <Modal open title={`즉시 작업 — ${target.name}`} onCancel={onClose} onOk={() => void create()} okText="LOT 만들기" okButtonProps={{ disabled: !unitProcessId }} confirmLoading={saving}>
      <Typography.Paragraph type="secondary">계획 없이 바로 작업합니다 (지금 시각의 작업지시 계획이 함께 만들어져 생산계획에도 표시됩니다).</Typography.Paragraph>
      <Select style={{ width: '100%' }} placeholder="단위공정" value={unitProcessId} onChange={setUnitProcessId} options={units.options} showSearch optionFilterProp="label" />
    </Modal>
  )
}

/** LOT 조건 행 → 입력표 (스텝 순서·관리항목 순서 보존) */
function gridOfConditions(rows: WorkCondition[]): { layout: GridLayout; values: GridValues } {
  const stepNames = new Map<number, string>()
  const items = new Map<number, GridItem>()
  for (const r of rows) {
    if (r.stepSequenceNo != null) stepNames.set(r.stepSequenceNo, r.stepNameSnapshot ?? '')
    if (!items.has(r.conditionItemId))
      items.set(r.conditionItemId, { conditionItemId: r.conditionItemId, conditionItemName: r.conditionItemName, unitCode: r.unitCode, valueType: r.valueType, isActive: r.isActive })
  }
  const steps = [...stepNames.entries()].sort((a, b) => a[0] - b[0]).map(([, name]) => newStep(name))
  const keyOf = new Map([...stepNames.keys()].sort((a, b) => a - b).map((no, i) => [no, steps[i].key]))
  const values: GridValues = {}
  for (const r of rows)
    if (r.setValue) values[cellKey(r.stepSequenceNo == null ? COMMON : keyOf.get(r.stepSequenceNo)!, r.conditionItemId)] = r.setValue
  return { layout: { steps, items: [...items.values()] }, values }
}

function WorkPanel({ id, menuKey }: { id: number; menuKey: string }) {
  const { message } = App.useApp()
  const canUpdate = useCan(menuKey, 'update')
  const codes = useCommonCodes()
  const queryClient = useQueryClient()
  const detail = useQuery({
    queryKey: [...queryKeys.works, 'detail', id],
    queryFn: ({ signal }) => api<WorkDetail>(`/api/works/${id}`, { signal }),
  })
  const d = detail.data
  const dataVersion = useDataVersion(d)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [standardsOpen, setStandardsOpen] = useState(false)
  const [finishing, setFinishing] = useState<'start' | 'complete' | null>(null)
  const [defectFor, setDefectFor] = useState<WorkDetail['inputs'][number] | null>(null)

  if (!d) return <Card loading />
  const w = d.work
  const editable = canUpdate && (w.status === 'ALLOCATED' || w.status === 'INPUT')

  /** 변경 호출 → 다시 읽기 (행 버전은 매번 최신 값) */
  const act = async (fn: () => Promise<unknown>, success?: string) => {
    setBusy(true)
    setError(null)
    try {
      await fn()
      if (success) message.success(success)
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
      await detail.refetch()
      void queryClient.invalidateQueries({ queryKey: [...queryKeys.works, 'board'] })
    }
  }

  return (
    <Space orientation="vertical" size={12} style={{ width: '100%' }}>
      <Card size="small"
        title={<Space><Typography.Text strong style={{ fontSize: 18 }}>{w.lotNo}</Typography.Text>
          <Tag color={w.status === 'INPUT' ? 'processing' : w.status === 'COMPLETED' ? 'success' : 'default'}>{codes.name('WORK_STATUS', w.status)}</Tag>
          {w.isMainProcess && <Tag color="gold">주공정</Tag>}</Space>}
        extra={canUpdate && (
          <Space>
            {w.status === 'ALLOCATED' && <Button type="primary" icon={<PlayCircleOutlined />} loading={busy} disabled={w.inputCount === 0} onClick={() => setFinishing('start')}>투입(시작)</Button>}
            {w.status === 'INPUT' && <Button type="primary" icon={<CheckCircleOutlined />} loading={busy} onClick={() => setFinishing('complete')}>완료</Button>}
          </Space>
        )}>
        <Descriptions size="small" column={{ xs: 1, md: 3 }} items={[
          { label: '설비', children: w.equipmentName },
          { label: '단위공정', children: w.unitProcessName },
          { label: '공정', children: w.heatProcessName ?? '-' },
          { label: '계획', children: w.plannedStartAt ? `${dayjs(w.plannedStartAt).format(TIME)} ~ ${dayjs(w.plannedEndAt).format('HH:mm')}` : '-' },
          { label: '실적', children: w.actualStartAt ? `${dayjs(w.actualStartAt).format(TIME)} ~ ${w.actualEndAt ? dayjs(w.actualEndAt).format('HH:mm') : '진행 중'}` : '-' },
          { label: '작업시간', children: w.actualDurationMin != null ? `${w.actualDurationMin}분` : w.expectedDurationMin != null ? `예상 ${w.expectedDurationMin}분` : '-' },
          { label: '작업표준', children: w.isStandardFixed ? `${w.standardName} v${w.standardVersionNo}` : <Typography.Text type="warning">미확정</Typography.Text> },
          { label: '투입', children: `${w.inputCount}건 · ${qty(w.inputQty)}` },
        ]} />
        <HeaderForm key={`h${dataVersion}`} work={w} editable={canUpdate && w.status !== 'CANCELLED'}
          onSave={(v) => act(() => api(`/api/works/${id}`, { method: 'PUT', body: { ...v, rowVersion: w.rowVersion } }), '저장했습니다.')} />
      </Card>

      {error && <Alert type="error" showIcon closable title={error} onClose={() => setError(null)} />}

      <Card size="small" title={`투입 (${d.inputs.length})`}
        extra={editable && <Button size="small" onClick={() => setStandardsOpen(true)} disabled={d.inputs.length === 0}>{w.isStandardFixed ? '표준 다시 확정' : '표준 확정'}</Button>}>
        {editable && <ScanBox workId={id} onAdd={(c, q) => act(() => api(`/api/works/${id}/inputs`, {
          method: 'POST', body: { rowVersion: w.rowVersion, salesOrderItemId: c.salesOrderItemId, mainInputId: c.mainInputId, inputQty: q },
        }), `${c.orderItemNo} ${qty(q)} 투입`)} />}
        <Table size="small" rowKey="productionWorkInputId" pagination={false} dataSource={d.inputs} scroll={{ x: 900 }}
          columns={[
            { title: '입고번호', dataIndex: 'orderItemNo', width: 125, render: (no: string, r) => <Space size={4}>{no}{r.isStandardBasis && <Tag color="blue">표준</Tag>}</Space> },
            { title: '거래처', dataIndex: 'customerName', width: 100, ellipsis: true },
            { title: '품명', render: (_: unknown, r) => <>{r.partName}{r.partNumber && <Typography.Text type="secondary"> {r.partNumber}</Typography.Text>}</> },
            { title: '고객LOT', dataIndex: 'customerLot', width: 100 },
            { title: '주 LOT', dataIndex: 'mainLotNo', width: 125, render: (lot: string | null, r) => (r.mainWorkId === id ? <Typography.Text type="secondary">자신</Typography.Text> : lot) },
            {
              title: '투입수량', dataIndex: 'inputQty', width: 120, align: 'right',
              render: (v: number, r) => editable
                ? <InputNumber size="small" min={0} defaultValue={v} style={{ width: 100 }}
                    onBlur={(e) => { const n = Number(e.target.value.replace(/,/g, '')); if (n && n !== v) void act(() => api(`/api/works/${id}/inputs/${r.productionWorkInputId}`, { method: 'PUT', body: { rowVersion: w.rowVersion, inputQty: n, trayMark: r.trayMark, remark: r.remark } })) }} />
                : qty(v),
            },
            {
              title: '불량', dataIndex: 'defectQty', width: 110, align: 'right',
              render: (v: number, r) => <Space size={4}>{qty(v)}{canUpdate && (w.status === 'INPUT' || w.status === 'COMPLETED') && <Button size="small" onClick={() => setDefectFor(r)}>등록</Button>}</Space>,
            },
            { title: '양품', dataIndex: 'goodQty', width: 80, align: 'right', render: qty },
            ...(editable ? [{
              title: '', width: 50, render: (_: unknown, r: WorkDetail['inputs'][number]) => (
                <Popconfirm title={`${r.orderItemNo} 투입을 지웁니다`} disabled={r.isReferenced}
                  onConfirm={() => void act(() => api(`/api/works/${id}/inputs/${r.productionWorkInputId}?rowVersion=${w.rowVersion}`, { method: 'DELETE' }))}>
                  <Button size="small" type="text" danger icon={<DeleteOutlined />} disabled={r.isReferenced} title={r.isReferenced ? '후공정·검사·부적합에서 쓰는 행' : undefined} />
                </Popconfirm>
              ),
            }] : []),
          ]} />
      </Card>

      <ConditionsCard key={`c${dataVersion}`} detail={d} editable={editable}
        onSave={(body) => act(() => api(`/api/works/${id}/conditions`, { method: 'PUT', body: { ...body, rowVersion: w.rowVersion } }), '조건을 저장했습니다.')} />

      <Card size="small" title="이력">
        <Timeline items={d.events.map((e) => ({
          children: <>{dayjs(e.eventAt).format(TIME)} · {e.eventType}{e.userName && <Typography.Text type="secondary"> · {e.userName}</Typography.Text>}{e.remark && ` · ${e.remark}`}</>,
        }))} />
      </Card>

      {standardsOpen && <StandardModal workId={id} onClose={() => setStandardsOpen(false)} onPick={(c) => {
        setStandardsOpen(false)
        void act(() => api(`/api/works/${id}/fix-standard`, {
          method: 'POST', body: { rowVersion: w.rowVersion, productionWorkInputId: c.productionWorkInputId, standardVersionId: c.standardVersionId },
        }), `${c.standardName} v${c.versionNo} 으로 확정 — 조건을 복사했습니다.`)
      }} />}
      {defectFor && <DefectModal input={defectFor} onClose={() => setDefectFor(null)} onSave={(v) => {
        setDefectFor(null)
        void act(() => api(`/api/works/${id}/inputs/${defectFor.productionWorkInputId}/defects`, { method: 'POST', body: { ...v, rowVersion: w.rowVersion } }),
          `${defectFor.orderItemNo} 불량 ${qty(v.defectQty)} 을 등록했습니다 (품질 > 부적합에서 처리).`)
      }} />}
      {finishing && <TimeModal kind={finishing} onClose={() => setFinishing(null)} onOk={(at) => {
        setFinishing(null)
        void act(() => api(`/api/works/${id}/${finishing}`, { method: 'POST', body: { rowVersion: w.rowVersion, [finishing === 'start' ? 'startAt' : 'endAt']: at?.format('YYYY-MM-DDTHH:mm:00') ?? null } }),
          finishing === 'start' ? '투입(시작)했습니다.' : '완료했습니다.')
      }} />}
    </Space>
  )
}

function HeaderForm({ work, editable, onSave }: { work: Work; editable: boolean; onSave: (v: { submitLotNo?: string; marking?: string; remark?: string }) => void }) {
  const [form] = Form.useForm()
  const [dirty, setDirty] = useState(false)
  return (
    <Form form={form} layout="inline" disabled={!editable} style={{ marginTop: 8, rowGap: 8 }} onValuesChange={() => setDirty(true)}
      initialValues={{ submitLotNo: work.submitLotNo, marking: work.marking, remark: work.remark }}>
      <Form.Item name="submitLotNo" label="제출 LOT" tooltip="고객 제출용 LOT번호 (구 변환 LOT)"><Input style={{ width: 150 }} maxLength={100} /></Form.Item>
      <Form.Item name="marking" label="마킹"><Input style={{ width: 120 }} maxLength={100} /></Form.Item>
      <Form.Item name="remark" label="특기사항"><Input style={{ width: 260 }} /></Form.Item>
      {editable && dirty && <Button size="small" onClick={() => { onSave(form.getFieldsValue()); setDirty(false) }}>저장</Button>}
    </Form>
  )
}

/** 스캔 입력 (구 txtIncomeNo) — 입고번호 또는 주 LOT번호. 후보가 1개면 수량만 확인해 바로 추가 */
function ScanBox({ workId, onAdd }: { workId: number; onAdd: (c: InputCandidate, qty: number) => Promise<void> }) {
  const [code, setCode] = useState('')
  const [result, setResult] = useState<ScanResult | null>(null)
  const [qtys, setQtys] = useState<Record<string, number | null>>({})
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const inputRef = useRef<InputRef>(null)
  const keyOf = (c: InputCandidate) => `${c.salesOrderItemId}:${c.mainInputId ?? 0}`

  const scan = async () => {
    if (!code.trim()) return
    setLoading(true)
    setError(null)
    try {
      const r = await api<ScanResult>(`/api/works/${workId}/scan`, { method: 'POST', body: { code } })
      setResult(r)
      setQtys(Object.fromEntries(r.candidates.map((c) => [keyOf(c), c.remainingQty > 0 ? c.remainingQty : null])))
    } catch (e) {
      setResult(null)
      setError(errorText(e))
    } finally {
      setLoading(false)
    }
  }
  const add = async (c: InputCandidate) => {
    const q = qtys[keyOf(c)]
    if (!q) return
    await onAdd(c, q)
    setResult(null)
    setCode('')
    inputRef.current?.focus()
  }

  return (
    <div style={{ marginBottom: 12 }}>
      <Input.Search ref={inputRef} autoFocus enterButton={<><ScanOutlined /> 조회</>} loading={loading} value={code} onChange={(e) => setCode(e.target.value)}
        onSearch={() => void scan()} placeholder="입고번호(수주번호) 또는 주 LOT번호 스캔" style={{ maxWidth: 480 }} />
      {error && <Alert style={{ marginTop: 8 }} type="warning" showIcon title={error} />}
      {result && (
        <Table size="small" style={{ marginTop: 8 }} rowKey={keyOf} pagination={false} dataSource={result.candidates} scroll={{ x: 900 }}
          title={() => <Typography.Text type="secondary">{result.kind === 'MAIN_LOT' ? `주 LOT ${result.code} 의 투입 품목` : `${result.code} — 투입할 주 LOT·수량을 확인하세요`}</Typography.Text>}
          columns={[
            { title: '입고번호', dataIndex: 'orderItemNo', width: 125 },
            { title: '품명', dataIndex: 'partName', width: 160, ellipsis: true },
            { title: '주 LOT', dataIndex: 'mainLotNo', width: 125, render: (v: string | null, c) => v ?? (c.phase === 'MAIN' ? '이 LOT (주공정)' : '-') },
            { title: '기준', dataIndex: 'baseQty', width: 80, align: 'right', render: qty },
            { title: '기투입', dataIndex: 'alreadyQty', width: 80, align: 'right', render: qty },
            { title: '잔량', dataIndex: 'remainingQty', width: 80, align: 'right', render: (v: number) => <Typography.Text type={v > 0 ? undefined : 'danger'}>{qty(v)}</Typography.Text> },
            {
              title: '투입수량', width: 200, render: (_: unknown, c) => c.alreadyInThisWork ? <Tag>이미 투입</Tag> : (
                <Space.Compact size="small">
                  <InputNumber min={0} max={c.remainingQty} value={qtys[keyOf(c)]} disabled={c.remainingQty <= 0}
                    onChange={(v) => setQtys((q) => ({ ...q, [keyOf(c)]: v }))} onPressEnter={() => void add(c)} />
                  <Button type="primary" disabled={!qtys[keyOf(c)] || c.remainingQty <= 0} onClick={() => void add(c)}>투입</Button>
                </Space.Compact>
              ),
            },
          ]} />
      )}
    </div>
  )
}

function ConditionsCard({ detail, editable, onSave }: { detail: WorkDetail; editable: boolean; onSave: (body: object) => void }) {
  const initial = useMemo(() => gridOfConditions(detail.conditions), [detail.conditions])
  const [layout, setLayout] = useState(initial.layout)
  const [values, setValues] = useState(initial.values)
  const [dirty, setDirty] = useState(false)
  const empty = layout.items.length === 0 && layout.steps.length === 0
  let body: ReactNode
  if (empty && !detail.work.isStandardFixed)
    body = <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="표준을 확정하면 작업표준의 조건이 복사됩니다 (수정 가능)" />
  else
    body = <ConditionGrid layout={layout} onLayoutChange={(l) => { setLayout(l); setDirty(true) }} values={values}
      onValuesChange={(v) => { setValues(v); setDirty(true) }} showCommon editable={editable} />
  return (
    <Card size="small" title="작업 조건"
      extra={editable && dirty && (
        <Button size="small" type="primary" onClick={() => {
          onSave({ steps: layout.steps.map((s) => s.name.trim()), items: layout.items.map((i) => i.conditionItemId), conditions: toConditions(layout, values) })
          setDirty(false)
        }}>조건 저장</Button>
      )}>
      {body}
    </Card>
  )
}

function StandardModal({ workId, onClose, onPick }: { workId: number; onClose: () => void; onPick: (c: StandardCandidate) => void }) {
  const list = useQuery({
    queryKey: [...queryKeys.works, 'standards', workId],
    queryFn: ({ signal }) => api<StandardCandidate[]>(`/api/works/${workId}/standards`, { signal }),
  })
  return (
    <Modal open title="표준 확정 — 투입 품목 중 기준 품목의 작업표준" onCancel={onClose} footer={null} width={820}>
      <Typography.Paragraph type="secondary">혼적 LOT 이면 투입 품목 중 하나를 골라 그 표준으로 확정합니다. 조건은 LOT 에 복사되어 이후 표준이 바뀌어도 유지됩니다.</Typography.Paragraph>
      <Table size="small" rowKey={(c) => `${c.productionWorkInputId}:${c.standardVersionId}`} loading={list.isFetching} pagination={false} dataSource={list.data ?? []}
        locale={{ emptyText: '조건(품목·단위공정·설비·거래처)이 맞는 작업표준이 없습니다. 기준정보 > 작업표준에서 등록하세요.' }}
        columns={[
          { title: '투입 품목', dataIndex: 'partName' },
          { title: '작업표준', render: (_: unknown, c) => <>{c.standardName} <Typography.Text type="secondary">{c.standardCode} v{c.versionNo}</Typography.Text></> },
          { title: '적용', width: 150, render: (_: unknown, c) => [c.equipmentName ?? (c.equipmentTypeName ? `${c.equipmentTypeName} 공통` : '공통'), c.customerName].filter(Boolean).join(' · ') },
          { title: '작업시간', dataIndex: 'runningTimeMin', width: 80, align: 'right', render: (m: number | null) => m != null && `${m}분` },
          { title: '', width: 70, render: (_: unknown, c) => <Button size="small" type="primary" onClick={() => onPick(c)}>확정</Button> },
        ]} />
    </Modal>
  )
}

/** 시작·완료 시각 (기본 = 지금, 완료는 서버가 설정 단위로 내림) */
function TimeModal({ kind, onClose, onOk }: { kind: 'start' | 'complete'; onClose: () => void; onOk: (at: Dayjs | null) => void }) {
  const [at, setAt] = useState<Dayjs | null>(null)
  return (
    <Modal open title={kind === 'start' ? '투입(작업 시작)' : '작업 완료'} onCancel={onClose} onOk={() => onOk(at)} okText={kind === 'start' ? '투입' : '완료'}>
      <Space orientation="vertical">
        <Typography.Text>{kind === 'start' ? '시작 시각' : '완료 시각'} (비우면 지금{kind === 'complete' ? ', 설정 단위로 내림' : ''})</Typography.Text>
        <DatePicker showTime={{ format: 'HH:mm' }} format="YYYY-MM-DD HH:mm" value={at} onChange={setAt} placeholder="지금" />
        <Badge status="processing" text={kind === 'start' ? '설비당 진행 중 LOT 은 1건입니다.' : '완료 후에는 투입·조건을 바꿀 수 없습니다.'} />
      </Space>
    </Modal>
  )
}

/** 투입 행 불량 등록 (구 작업 화면 불량수량) — 품질 > 부적합에서 판정·재작업 */
function DefectModal({ input, onClose, onSave }: { input: WorkDetail['inputs'][number]; onClose: () => void; onSave: (v: { defectQty: number; defectReasonId?: number; remark?: string }) => void }) {
  const reasons = useOptions('/api/master/defect_reason/options')
  const [form] = Form.useForm<{ defectQty: number; defectReasonId?: number; remark?: string }>()
  return (
    <Modal open title={`불량 등록 — ${input.orderItemNo} ${input.partName ?? ''}`} onCancel={onClose} onOk={() => void form.validateFields().then(onSave)} okText="등록">
      <Typography.Paragraph type="secondary">양품 {qty(input.goodQty)} 중 불량 수량. 등록하면 양품이 줄고, 품질 &gt; 부적합에서 처리구분을 정합니다.</Typography.Paragraph>
      <Form form={form} layout="vertical">
        <Form.Item name="defectQty" label="불량 수량" rules={[{ required: true }]}><InputNumber min={0} max={input.goodQty} style={{ width: 160 }} autoFocus /></Form.Item>
        <Form.Item name="defectReasonId" label="불량 사유"><Select allowClear showSearch optionFilterProp="label" options={reasons.options} /></Form.Item>
        <Form.Item name="remark" label="내용"><Input maxLength={255} /></Form.Item>
      </Form>
    </Modal>
  )
}
