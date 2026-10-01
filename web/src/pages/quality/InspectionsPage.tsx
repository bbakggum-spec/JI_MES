import { DeleteOutlined, FilePdfOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert, App, Button, Col, DatePicker, Form, Input, Popconfirm, Row, Select, Space, Table, Tag, Tooltip, Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { ApiError, api, apiFile, saveFile } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import ExportButton from '../../components/ExportButton'
import EditorWindow from '../../components/EditorWindow'
import { useClientSettings } from '../../hooks/useClientSettings'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'
import { previewJudge, type Criteria, type Inspection, type InspectionDetail, type ItemRow, type LotLookup, type TargetCandidate } from './inspectionTypes'

const DATE = 'YYYY-MM-DD'
const qty = (n: number | null | undefined) => (n == null ? '' : n.toLocaleString(undefined, { maximumFractionDigits: 3 }))
const errorText = (e: unknown) => (e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))

/** 검사 — 구 F_InspectionForm(목록) + F_InspectionAddForm (설계 §5.1·§23.5) */
export default function InspectionsPage({ menuKey }: PageProps) {
  const canCreate = useCan(menuKey, 'create')
  const codes = useCommonCodes()
  const settings = useClientSettings()
  const queryClient = useQueryClient()
  const [range, setRange] = useState<[Dayjs, Dayjs] | null>(null)
  const [type, setType] = useState<string | undefined>()
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const effectiveRange = range ?? (settings.orderListDays ? [dayjs().subtract(settings.orderListDays, 'day'), dayjs()] as [Dayjs, Dayjs] : null)
  const params = new URLSearchParams()
  if (effectiveRange) { params.set('from', effectiveRange[0].format(DATE)); params.set('to', effectiveRange[1].format(DATE)) }
  if (type) params.set('type', type)
  if (search) params.set('search', search)
  const list = useQuery({
    queryKey: [...queryKeys.inspections, 'list', params.toString()],
    queryFn: ({ signal }) => api<Inspection[]>(`/api/inspections?${params}`, { signal }),
    enabled: effectiveRange !== null,
  })
  const colorOf = (group: string, code: string | null) => (code ? codes.attr<{ color?: string }>(group, code)?.color : undefined)

  // 표·내보내기 공용 열 (내보내기 = 화면 표시 글자 그대로)
  const listColumns: ColumnsType<Inspection> = [
      { title: '검사일', dataIndex: 'inspectionDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
      { title: '검사번호', dataIndex: 'inspectionNo', width: 130, render: (no: string, r) => <>{no}{r.reinspectionOfNo && <Tooltip title={`${r.reinspectionOfNo} 재검사`}><Tag style={{ marginLeft: 4 }}>재</Tag></Tooltip>}</> },
      { title: '구분', dataIndex: 'inspectionType', width: 80, render: (t: string) => codes.name('INSPECTION_TYPE', t) },
      { title: 'LOT', dataIndex: 'lotNo', width: 130 },
      { title: '대상', render: (_: unknown, r) => <>{r.targetSummary}<Typography.Text type="secondary"> ({r.targetCount})</Typography.Text></> },
      { title: '판정', dataIndex: 'decision', width: 80, render: (d: string | null) => d && <Tag color={colorOf('DECISION', d)}>{codes.name('DECISION', d)}</Tag> },
      { title: '상태', dataIndex: 'status', width: 100, render: (s: string) => <Tag color={colorOf('INSPECTION_STATUS', s)}>{codes.name('INSPECTION_STATUS', s)}</Tag> },
      { title: '검사자', dataIndex: 'inspectorName', width: 90 },
    ]

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>검사</Typography.Title>
        <Space wrap>
          <DatePicker.RangePicker value={effectiveRange} allowClear={false} onChange={(v) => v?.[0] && v[1] && setRange([v[0], v[1]])} />
          <Select allowClear placeholder="검사구분 전체" style={{ width: 130 }} value={type} onChange={setType}
            options={codes.options('INSPECTION_TYPE').map((c) => ({ value: c.code, label: c.codeName }))} />
          <Input.Search allowClear placeholder="검사번호·LOT·품명·입고번호" style={{ width: 230 }} onSearch={(v) => setSearch(v.trim())} />
          <ExportButton title="검사" columns={listColumns} rows={list.data ?? []} />
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>검사 등록</Button>}
        </Space>
      </Space>
      <Table<Inspection> rowKey="inspectionId" size="small" loading={list.isFetching} dataSource={list.data ?? []} scroll={{ x: 1100 }}
        pagination={{ defaultPageSize: 50, showSizeChanger: true, showTotal: (n) => `${n}건` }}
        onRow={(r) => ({ onClick: () => setEditing(r.inspectionId), style: { cursor: 'pointer', opacity: r.status === 'CANCELLED' ? 0.5 : 1 } })}
        columns={listColumns} />
      {editing !== null && (
        <InspectionWindow id={editing === 'new' ? null : editing} menuKey={menuKey} onClose={() => setEditing(null)}
          onChanged={(id) => { setEditing(id); void queryClient.invalidateQueries({ queryKey: queryKeys.inspections }) }} />
      )}
    </>
  )
}

interface HeaderForm {
  inspectionType: string
  inspectionDate: Dayjs
  inspectorEmployeeId?: number
  remark?: string
}

let rowSeq = 0
const rowsFromCriteria = (criteria: Criteria[]): ItemRow[] => criteria.map((c) => ({
  key: ++rowSeq, inspectionCriteriaId: c.inspectionCriteriaId, itemType: c.itemType, itemName: c.itemName, location: c.location,
  decision: null, remark: null, values: Array.from({ length: Math.max(1, c.sampleCount) }, () => ''),
}))

function InspectionWindow({ id, menuKey, onClose, onChanged }: { id: number | null; menuKey: string; onClose: () => void; onChanged: (id: number) => void }) {
  const { message, modal } = App.useApp()
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const canDelete = useCan(menuKey, 'delete')
  const codes = useCommonCodes()
  const employees = useOptions('/api/master/employee/options')
  const [form] = Form.useForm<HeaderForm>()
  const detail = useQuery({
    queryKey: [...queryKeys.inspections, 'detail', id],
    queryFn: ({ signal }) => api<InspectionDetail>(`/api/inspections/${id}`, { signal }),
    enabled: id !== null,
  })
  const d = detail.data
  const [lotNo, setLotNo] = useState('')
  const [lot, setLot] = useState<LotLookup | null>(null)
  const [selected, setSelected] = useState<number[] | null>(null)
  const [versionId, setVersionId] = useState<number | null | undefined>(undefined)
  const [rows, setRows] = useState<ItemRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // 기존 검사를 열면 그 LOT 의 대상 후보를 불러온다
  const lotQuery = useQuery({
    queryKey: [...queryKeys.inspections, 'lot', d?.inspection.lotNo],
    queryFn: ({ signal }) => api<LotLookup>(`/api/inspections/lot?lotNo=${encodeURIComponent(d!.inspection.lotNo)}`, { signal }),
    enabled: !!d,
  })
  const lotData = lot ?? lotQuery.data ?? null
  const effectiveVersion = versionId !== undefined ? versionId : d ? d.inspection.inspectionStandardVersionId : null
  const criteriaQuery = useQuery({
    queryKey: [...queryKeys.inspections, 'criteria', effectiveVersion],
    queryFn: ({ signal }) => api<Criteria[]>(`/api/inspections/criteria/${effectiveVersion}`, { signal }),
    enabled: !!effectiveVersion,
  })
  const criteriaById = useMemo(() => new Map((criteriaQuery.data ?? []).map((c) => [c.inspectionCriteriaId, c])), [criteriaQuery.data])
  const effectiveSelected = selected ?? d?.targets.map((t) => t.productionWorkInputId) ?? []
  const effectiveRows: ItemRow[] = rows ?? (d ? d.items.map((i) => ({
    key: ++rowSeq, inspectionCriteriaId: i.inspectionCriteriaId, itemType: i.itemType, itemName: i.itemName, location: i.location,
    decision: i.decision, remark: i.remark, values: i.values.map((v) => v ?? ''),
  })) : [])

  if (id !== null && !d) return null
  const status = d?.inspection.status
  const editable = d ? (status === 'WAITING' || status === 'IN_PROGRESS') && canUpdate : canCreate
  const setRow = (key: number, patch: Partial<ItemRow>) => setRows(effectiveRows.map((r) => (r.key === key ? { ...r, ...patch } : r)))

  const lookup = async () => {
    setError(null)
    try {
      const r = await api<LotLookup>(`/api/inspections/lot?lotNo=${encodeURIComponent(lotNo.trim())}`)
      setLot(r)
      const first = r.candidates.filter((c) => c.inspectionCount === 0)
      setSelected(first.map((c) => c.productionWorkInputId))
      const v = (first[0] ?? r.candidates[0])?.inspectionStandardVersionId ?? null
      setVersionId(v)
      if (v) setRows(rowsFromCriteria(await api<Criteria[]>(`/api/inspections/criteria/${v}`)))
      else setRows([])
    } catch (e) {
      setLot(null)
      setError(errorText(e))
    }
  }
  const changeVersion = async (v: number | null) => {
    setVersionId(v)
    setRows(v ? rowsFromCriteria(await api<Criteria[]>(`/api/inspections/criteria/${v}`)) : [])
  }

  const body = (h: HeaderForm) => ({
    inspectionType: h.inspectionType, inspectionDate: h.inspectionDate.format(DATE), inputIds: effectiveSelected, inspectionStandardVersionId: effectiveVersion,
    inspectorEmployeeId: h.inspectorEmployeeId, remark: h.remark,
    items: effectiveRows.map((r) => ({ inspectionCriteriaId: r.inspectionCriteriaId, itemType: r.itemType, itemName: r.itemName, location: r.location, decision: r.decision, remark: r.remark, values: r.values })),
  })
  const run = async (fn: () => Promise<number | void>, success: string) => {
    setBusy(true)
    setError(null)
    try {
      const newId = await fn()
      message.success(success)
      setRows(null)
      setSelected(null)
      setVersionId(undefined)
      if (typeof newId === 'number') onChanged(newId)
      else { await detail.refetch(); onChanged(id!) }
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }
  const save = async () => {
    const h = await form.validateFields()
    if (!lotData) { setError('LOT 을 먼저 조회하세요.'); return }
    if (d) await run(async () => { await api(`/api/inspections/${id}`, { method: 'PUT', body: { ...body(h), rowVersion: d.inspection.rowVersion } }) }, '저장했습니다.')
    else await run(async () => (await api<{ inspectionId: number; inspectionNo: string }>('/api/inspections', { method: 'POST', body: { ...body(h), productionWorkId: lotData.work.productionWorkId } })).inspectionId, '검사를 등록했습니다.')
  }
  const report = async (targetId: number) => {
    try {
      saveFile(await apiFile(`/api/inspections/targets/${targetId}/report`, { method: 'POST' }))
      void detail.refetch()
    } catch (e) {
      message.error(errorText(e))
    }
  }

  const i = d?.inspection
  const maxSamples = Math.max(1, ...effectiveRows.map((r) => r.values.length))
  return (
    <EditorWindow onClose={onClose} size={1400} destroyOnHidden
      title={i ? <Space>{i.inspectionNo}<Tag color={codes.attr<{ color?: string }>('INSPECTION_STATUS', i.status)?.color}>{codes.name('INSPECTION_STATUS', i.status)}</Tag>
        {i.reinspectionOfNo && <Tag>{i.reinspectionOfNo} 재검사</Tag>}</Space> : '검사 등록'}
      extra={(
        <Space>
          {d && editable && canDelete && (
            <Popconfirm title="이 검사를 취소합니다" onConfirm={() => void run(async () => { await api(`/api/inspections/${id}/cancel`, { method: 'POST', body: { rowVersion: d.inspection.rowVersion } }) }, '검사를 취소했습니다.')}>
              <Button danger loading={busy}>검사 취소</Button>
            </Popconfirm>
          )}
          {d && status === 'COMPLETED' && canCreate && (
            <Popconfirm title="재검사" description="확정된 검사는 고칠 수 없어 새 번호로 복사한 뒤 고칩니다." onConfirm={() => void run(async () =>
              (await api<{ inspectionId: number }>(`/api/inspections/${id}/reinspect`, { method: 'POST', body: { rowVersion: d.inspection.rowVersion } })).inspectionId, '재검사를 만들었습니다.')}>
              <Button loading={busy}>재검사</Button>
            </Popconfirm>
          )}
          {d && editable && (
            <Button loading={busy} onClick={() => modal.confirm({
              title: `판정 ${codes.name('DECISION', d.inspection.decision ?? '') || '없음'} 으로 확정할까요?`,
              content: d.inspection.decision === 'FAIL' ? '불합격이면 대상마다 대상 수량 전체가 부적합으로 등록됩니다. 저장하지 않은 변경은 반영되지 않습니다.' : '확정 후에는 재검사로만 고칠 수 있습니다. 저장하지 않은 변경은 반영되지 않습니다.',
              onOk: () => run(async () => { await api(`/api/inspections/${id}/complete`, { method: 'POST', body: { rowVersion: d.inspection.rowVersion } }) }, '확정했습니다.'),
            })}>확정</Button>
          )}
          {editable && <Button type="primary" loading={busy} onClick={() => void save()}>저장</Button>}
        </Space>
      )}>
      <Form form={form} layout="vertical" disabled={!editable} initialValues={i
        ? { inspectionType: i.inspectionType, inspectionDate: dayjs(i.inspectionDate), inspectorEmployeeId: i.inspectorEmployeeId ?? undefined, remark: i.remark ?? undefined }
        : { inspectionType: 'OUTGOING', inspectionDate: dayjs() }}>
        <Row gutter={12}>
          <Col span={4}>
            <Form.Item name="inspectionType" label="검사구분" rules={[{ required: true }]}>
              <Select disabled={!!d} options={codes.options('INSPECTION_TYPE').map((c) => ({ value: c.code, label: c.codeName }))} />
            </Form.Item>
          </Col>
          <Col span={4}><Form.Item name="inspectionDate" label="검사일" rules={[{ required: true }]}><DatePicker style={{ width: '100%' }} /></Form.Item></Col>
          <Col span={5}>
            <Form.Item name="inspectorEmployeeId" label="검사자" rules={[{ required: true, message: '검사자를 고르세요' }]}>
              <Select showSearch optionFilterProp="label" options={employees.options} />
            </Form.Item>
          </Col>
          <Col span={11}><Form.Item name="remark" label="비고"><Input /></Form.Item></Col>
        </Row>
      </Form>

      {!d && (
        <Space style={{ marginBottom: 8 }}>
          <Input.Search placeholder="작업 LOT번호 (보통 주 LOT)" enterButton="조회" value={lotNo} onChange={(e) => setLotNo(e.target.value)} onSearch={() => void lookup()} style={{ width: 360 }} autoFocus />
        </Space>
      )}
      {error && <Alert type="error" showIcon style={{ marginBottom: 8 }} title={error} />}

      {lotData && (
        <>
          <Typography.Text strong>검사 대상 — {lotData.work.lotNo} ({lotData.work.unitProcessName} · {lotData.work.equipmentName})</Typography.Text>
          <Table<TargetCandidate> size="small" style={{ margin: '8px 0 16px' }} rowKey="productionWorkInputId" pagination={false} dataSource={lotData.candidates}
            rowSelection={editable ? { selectedRowKeys: effectiveSelected, onChange: (keys) => setSelected(keys as number[]) } : undefined}
            columns={[
              { title: '입고번호', dataIndex: 'orderItemNo', width: 125 },
              { title: '거래처', dataIndex: 'customerName', width: 110 },
              { title: '품명', render: (_: unknown, c) => <>{c.partName}{c.partNumber && <Typography.Text type="secondary"> {c.partNumber}</Typography.Text>}</> },
              { title: '고객LOT', dataIndex: 'customerLot', width: 100 },
              { title: '주 LOT', dataIndex: 'mainLotNo', width: 125 },
              { title: '투입', dataIndex: 'inputQty', width: 80, align: 'right', render: qty },
              { title: '양품', dataIndex: 'goodQty', width: 80, align: 'right', render: qty },
              { title: '기검사', dataIndex: 'inspectionCount', width: 70, align: 'right', render: (n: number) => (n > 0 ? <Tag color="orange">{n}</Tag> : '') },
              ...(d ? [{
                title: '성적서', width: 110, render: (_: unknown, c: TargetCandidate) => {
                  const t = d.targets.find((x) => x.productionWorkInputId === c.productionWorkInputId)
                  return t && <Tooltip title={t.reportIssuedAt ? `${t.reportIssueCount}회 발행 · 마지막 ${dayjs(t.reportIssuedAt).format('MM-DD HH:mm')}` : '미발행'}>
                    <Button size="small" icon={<FilePdfOutlined />} onClick={() => void report(t.inspectionTargetId)}>발행{t.reportIssueCount > 0 ? ` (${t.reportIssueCount})` : ''}</Button>
                  </Tooltip>
                },
              }] : []),
            ]} />
        </>
      )}

      {lotData && (
        <>
          <Space style={{ marginBottom: 8 }} wrap>
            <Typography.Text strong>측정 · 판정 (검사 공통)</Typography.Text>
            <Select size="small" style={{ width: 320 }} disabled={!editable} value={effectiveVersion ?? null} placeholder="검사기준 없음 (항목 직접 입력)" allowClear
              options={[...new Map(lotData.candidates.filter((c) => c.inspectionStandardVersionId).map((c) => [c.inspectionStandardVersionId!, `${c.partName} 검사기준`])).entries()]
                .map(([value, label]) => ({ value, label }))}
              onChange={(v) => void changeVersion(v ?? null)} />
            {i?.decision && <>종합 판정 <Tag color={codes.attr<{ color?: string }>('DECISION', i.decision)?.color}>{codes.name('DECISION', i.decision)}</Tag></>}
          </Space>
          <Table<ItemRow> size="small" bordered pagination={false} rowKey="key" dataSource={effectiveRows} scroll={{ x: 'max-content' }}
            columns={[
              { title: '항목', width: 150, fixed: 'left', render: (_: unknown, r) => editable && !r.inspectionCriteriaId
                ? <Input size="small" value={r.itemName} onChange={(e) => setRow(r.key, { itemName: e.target.value })} placeholder="항목명" />
                : <>{r.itemName}{r.itemType && <div><Typography.Text type="secondary" style={{ fontSize: 12 }}>{codes.name('INSPECTION_ITEM_TYPE', r.itemType)}</Typography.Text></div>}</> },
              {
                title: '규격', width: 140, render: (_: unknown, r) => {
                  const c = r.inspectionCriteriaId ? criteriaById.get(r.inspectionCriteriaId) : undefined
                  return c ? <>{c.specificationValue ?? [c.lowerLimit, c.upperLimit].filter((x) => x != null).join('~')}{c.scale && <Typography.Text type="secondary"> {c.scale}</Typography.Text>}</> : ''
                },
              },
              ...Array.from({ length: maxSamples }, (_, n) => ({
                title: `${n + 1}`, width: 72, key: `v${n}`,
                render: (_: unknown, r: ItemRow) => {
                  if (n >= r.values.length) return null
                  const c = r.inspectionCriteriaId ? criteriaById.get(r.inspectionCriteriaId) : undefined
                  const judged = previewJudge(c, r.values, r.decision)
                  const label = c?.points[n] ?? undefined
                  return editable
                    ? <Tooltip title={label}><Input size="small" value={r.values[n]} status={judged.ng[n] ? 'error' : undefined}
                        onChange={(e) => setRow(r.key, { values: r.values.map((v, j) => (j === n ? e.target.value : v)) })} /></Tooltip>
                    : <Typography.Text type={judged.ng[n] ? 'danger' : undefined}>{r.values[n]}</Typography.Text>
                },
              })),
              ...(editable ? [{
                title: '', width: 50, key: 'add', render: (_: unknown, r: ItemRow) => r.values.length < 50 &&
                  <Button size="small" type="text" icon={<PlusOutlined />} title="시료 추가" onClick={() => setRow(r.key, { values: [...r.values, ''] })} />,
              }] : []),
              {
                title: '판정', width: 110, render: (_: unknown, r) => {
                  const c = r.inspectionCriteriaId ? criteriaById.get(r.inspectionCriteriaId) : undefined
                  const judged = previewJudge(c, r.values, r.decision)
                  const auto = judged.decision !== r.decision || (c && ['BETWEEN', 'MIN', 'MAX'].includes(c.rangeType ?? '') && r.values.some((v) => v.trim() && !Number.isNaN(Number(v))))
                  if (auto && judged.decision) return <Tag color={codes.attr<{ color?: string }>('DECISION', judged.decision)?.color}>{codes.name('DECISION', judged.decision)}</Tag>
                  return editable
                    ? <Select size="small" style={{ width: 96 }} allowClear value={r.decision} onChange={(v) => setRow(r.key, { decision: v ?? null })}
                        options={['PASS', 'FAIL', 'NA'].map((code) => ({ value: code, label: codes.name('DECISION', code) }))} />
                    : r.decision && codes.name('DECISION', r.decision)
                },
              },
              { title: '비고', width: 140, render: (_: unknown, r) => editable ? <Input size="small" value={r.remark ?? ''} onChange={(e) => setRow(r.key, { remark: e.target.value })} /> : r.remark },
              ...(editable ? [{
                title: '', width: 40, key: 'del', render: (_: unknown, r: ItemRow) =>
                  <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={() => setRows(effectiveRows.filter((x) => x.key !== r.key))} />,
              }] : []),
            ]} />
          {editable && (
            <Button size="small" type="dashed" icon={<PlusOutlined />} style={{ marginTop: 8 }}
              onClick={() => setRows([...effectiveRows, { key: ++rowSeq, inspectionCriteriaId: null, itemType: null, itemName: '', location: null, decision: null, remark: null, values: [''] }])}>
              항목 추가 (기준 외)
            </Button>
          )}
        </>
      )}
    </EditorWindow>
  )
}
