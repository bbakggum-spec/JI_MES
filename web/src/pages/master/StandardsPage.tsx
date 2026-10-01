import { CopyOutlined, PlusOutlined, SaveOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Col, Form, Input, InputNumber, Modal, Radio, Row, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import EditorWindow from '../../components/EditorWindow'
import PrintButton from '../../components/PrintButton'
import dayjs from 'dayjs'
import { useMemo, useState } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { useDataVersion } from '../../hooks/useDataVersion'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'
import ConditionGrid from './ConditionGrid'
import { COMMON, cellKey, emptyLayout, layoutOf, newStep, toConditions, toValues, type ConditionRow, type GridItem, type GridLayout, type GridValues } from './conditionGridModel'
import type { StepTemplate, StepTemplateDetail } from './StepTemplatesPage'

interface Standard {
  standardId: number
  standardCode: string
  standardName: string
  partId: number
  partCode: string
  partName: string
  customerName: string | null
  heatProcessName: string | null
  unitProcessId: number
  unitProcessName: string
  equipmentTypeId: number | null
  equipmentTypeName: string | null
  equipmentName: string | null
  isActive: boolean
  currentVersionNo: number | null
  runningTimeMin: number | null
  chargeQty: number | null
  chargeUnit: string | null
  stepTemplateName: string | null
}

interface Version {
  standardVersionId: number
  versionNo: number
  stepTemplateId: number | null
  chargeQty: number
  chargeUnit: string
  runningTimeMin: number | null
  effectiveFrom: string
  effectiveTo: string | null
  isCurrent: boolean
  remark: string | null
  createdByName: string | null
  usageCount: number
}

interface Detail {
  header: Standard
  versions: Version[]
  version: Version | null
  steps: { sequenceNo: number; stepName: string }[]
  items: (GridItem & { sequenceNo: number })[]
  conditions: ConditionRow[]
}

/** 서버 Version → 입력표 (스텝 key 새로 부여) */
function gridOf(d: Pick<Detail, 'steps' | 'items' | 'conditions'>): { layout: GridLayout; values: GridValues } {
  const steps = d.steps.map((s) => newStep(s.stepName))
  return { layout: { steps, items: d.items }, values: toValues(steps, d.conditions) }
}

/** 작업표준 (설계 §2, 구 F_WorkStandardAddForm) — 저장 = 새 버전, 조건 = 관리항목 × [공통 + 단계] */
export default function StandardsPage({ menuKey }: PageProps) {
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const queryClient = useQueryClient()
  const units = useOptions('/api/master/unit_process/options')
  const [search, setSearch] = useState('')
  const [unitProcessId, setUnitProcessId] = useState<number | undefined>()
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const key = [...queryKeys.master, 'standard']
  const params = new URLSearchParams()
  if (search) params.set('search', search)
  if (unitProcessId) params.set('unitProcessId', String(unitProcessId))
  const list = useQuery({ queryKey: [...key, params.toString()], queryFn: ({ signal }) => api<Standard[]>(`/api/standards?${params}`, { signal }) })

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>작업표준</Typography.Title>
        <Space wrap>
          <Select allowClear placeholder="단위공정 전체" style={{ width: 150 }} value={unitProcessId} onChange={setUnitProcessId} options={units.options} />
          <Input.Search allowClear placeholder="코드·품목·품번" style={{ width: 220 }} onSearch={(v) => setSearch(v.trim())} />
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>작업표준 추가</Button>}
        </Space>
      </Space>
      <Table<Standard> rowKey="standardId" size="middle" loading={list.isFetching} dataSource={list.data ?? []} scroll={{ x: 1000 }}
        onRow={(s) => ({ onClick: () => setEditing(s.standardId), style: { cursor: 'pointer', opacity: s.isActive ? 1 : 0.5 } })}
        columns={[
          { title: '품목', render: (_: unknown, s) => <>{s.partName} <Typography.Text type="secondary">{s.partCode}</Typography.Text></> },
          { title: '단위공정', dataIndex: 'unitProcessName', width: 100 },
          { title: '설비', width: 140, render: (_: unknown, s) => s.equipmentName ?? (s.equipmentTypeName ? `${s.equipmentTypeName} 공통` : '공통') },
          { title: '거래처', dataIndex: 'customerName', width: 110 },
          { title: '공정', dataIndex: 'heatProcessName', width: 110 },
          { title: '템플릿', dataIndex: 'stepTemplateName', width: 120 },
          { title: 'charge', width: 100, align: 'right', render: (_: unknown, s) => s.chargeQty != null && `${s.chargeQty.toLocaleString()} ${s.chargeUnit ?? ''}` },
          { title: '작업시간', dataIndex: 'runningTimeMin', width: 90, align: 'right', render: (m: number | null) => m != null && `${m}분` },
          { title: '버전', dataIndex: 'currentVersionNo', width: 60, render: (v: number | null) => v && `v${v}` },
        ]} />
      {editing !== null && (
        <StandardWindow id={editing === 'new' ? null : editing} canEdit={editing === 'new' ? canCreate : canUpdate} onClose={() => setEditing(null)}
          onSaved={(id) => { setEditing(id); void queryClient.invalidateQueries({ queryKey: key }) }} />
      )}
    </>
  )
}

function StandardWindow({ id, canEdit, onClose, onSaved }: { id: number | null; canEdit: boolean; onClose: () => void; onSaved: (id: number) => void }) {
  const [viewVersionId, setViewVersionId] = useState<number | null>(null)
  const detail = useQuery({
    queryKey: [...queryKeys.master, 'standard', 'detail', id],
    queryFn: ({ signal }) => api<Detail>(`/api/standards/${id}`, { signal }),
    enabled: id !== null,
  })
  const dataVersion = useDataVersion(detail.data)
  if (id !== null && !detail.data) return null
  const d = detail.data
  return (
    <EditorWindow onClose={onClose} size={1200} title={d ? `${d.header.standardName} (${d.header.standardCode})` : '작업표준 추가'} destroyOnHidden
      extra={d?.version && (
        // 구 F_WorkStandardForm 작업표준서 — 현재 버전 (이전 버전은 버전 이력에서)
        <PrintButton purposeCode="WORK_STANDARD" sourceIds={[d.version.standardVersionId]}>작업표준서 v{d.version.versionNo}</PrintButton>
      )}>
      {d ? (
        <Tabs items={[
          { key: 'edit', label: `조건 (현재 v${d.version?.versionNo ?? '-'})`, children: <Editor key={dataVersion} standard={d} canEdit={canEdit} onSaved={() => { void detail.refetch(); onSaved(id!) }} /> },
          {
            key: 'versions', label: `버전 이력 (${d.versions.length})`, children: (
              <>
                <Table<Version> size="small" rowKey="standardVersionId" pagination={false} dataSource={d.versions}
                  onRow={(v) => ({ onClick: () => setViewVersionId(v.standardVersionId), style: { cursor: 'pointer' } })}
                  columns={[
                    { title: '버전', dataIndex: 'versionNo', width: 80, render: (v: number, r) => <Space>v{v}{r.isCurrent && <Tag color="blue">현재</Tag>}</Space> },
                    { title: '적용', width: 200, render: (_: unknown, r) => `${dayjs(r.effectiveFrom).format('YYYY-MM-DD HH:mm')} ~ ${r.effectiveTo ? dayjs(r.effectiveTo).format('MM-DD HH:mm') : ''}` },
                    { title: 'charge', width: 100, render: (_: unknown, r) => `${r.chargeQty} ${r.chargeUnit}` },
                    { title: '작업시간', dataIndex: 'runningTimeMin', width: 90, render: (m: number | null) => m != null && `${m}분` },
                    { title: '작업 LOT', dataIndex: 'usageCount', width: 80, align: 'right' },
                    { title: '작성', dataIndex: 'createdByName', width: 100 },
                    { title: '비고', dataIndex: 'remark' },
                  ]} />
                {viewVersionId && (
                  <>
                    <div style={{ margin: '8px 0' }}><PrintButton purposeCode="WORK_STANDARD" sourceIds={[viewVersionId]} size="small">이 버전 작업표준서</PrintButton></div>
                    <OldVersion standardId={d.header.standardId} versionId={viewVersionId} />
                  </>
                )}
              </>
            ),
          },
        ]} />
      ) : <Editor canEdit={canEdit} onSaved={(newId) => onSaved(newId!)} />}
    </EditorWindow>
  )
}

function OldVersion({ standardId, versionId }: { standardId: number; versionId: number }) {
  const v = useQuery({
    queryKey: [...queryKeys.master, 'standard', 'version', versionId],
    queryFn: ({ signal }) => api<Detail>(`/api/standards/${standardId}?versionId=${versionId}`, { signal }),
  })
  const grid = useMemo(() => (v.data ? gridOf(v.data) : null), [v.data])
  if (!v.data || !grid) return null
  return (
    <div style={{ marginTop: 16 }}>
      <Typography.Text strong>v{v.data.version?.versionNo} 조건 (보기 전용)</Typography.Text>
      <ConditionGrid layout={grid.layout} values={grid.values} showCommon editable={false} />
    </div>
  )
}

interface HeaderForm {
  partId: number
  unitProcessId: number
  equipmentTypeId?: number | null
  equipmentId?: number | null
  customerId?: number | null
  heatProcessId?: number | null
  standardName?: string
}

interface VersionForm {
  chargeQty: number
  chargeUnit: string
  runningTimeMin?: number | null
  remark?: string
}

function Editor({ standard, canEdit, onSaved }: { standard?: Detail; canEdit: boolean; onSaved: (id?: number) => void }) {
  const { message } = App.useApp()
  const [header] = Form.useForm<HeaderForm>()
  const [versionForm] = Form.useForm<VersionForm>()
  const parts = useOptions('/api/parts/options')
  const units = useOptions('/api/master/unit_process/options')
  const types = useOptions('/api/master/equipment_type/options')
  const equipment = useOptions('/api/master/equipment/options')
  const customers = useOptions('/api/master/customer/options')
  const heatProcesses = useQuery({
    queryKey: [...queryKeys.master, 'heat_process', 'all'],
    queryFn: ({ signal }) => api<{ heatProcessId: number; heatProcessName: string }[]>('/api/heat-processes', { signal }),
  })
  const initial = useMemo(() => (standard ? gridOf(standard) : { layout: emptyLayout(), values: {} }), [standard])
  const [layout, setLayout] = useState<GridLayout>(initial.layout)
  const [values, setValues] = useState<GridValues>(initial.values)
  const [unitProcessId, setUnitProcessId] = useState<number | undefined>(standard?.header.unitProcessId)
  /** 입력표를 불러온 템플릿 (참고로 함께 저장) */
  const [templateId, setTemplateId] = useState<number | null>(standard?.version?.stepTemplateId ?? null)
  const [savingAsTemplate, setSavingAsTemplate] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const canCreateTemplate = useCan('master.step_template', 'create')

  const templates = useQuery({
    queryKey: [...queryKeys.master, 'step_template', 'byUnit', unitProcessId],
    queryFn: ({ signal }) => api<StepTemplate[]>(`/api/step-templates?unitProcessId=${unitProcessId}`, { signal }),
    enabled: unitProcessId !== undefined,
  })
  const copySources = useQuery({
    queryKey: [...queryKeys.master, 'standard', 'byUnit', unitProcessId],
    queryFn: ({ signal }) => api<Standard[]>(`/api/standards?unitProcessId=${unitProcessId}`, { signal }),
    enabled: unitProcessId !== undefined && canEdit,
  })

  /** 템플릿 불러오기 — 스텝·관리항목을 템플릿대로, 이미 적은 값은 (스텝 이름, 항목)이 같으면 유지 (구 LoadDetailsWithLatestTemplate) */
  const loadTemplate = async (id: number) => {
    const tpl = await api<StepTemplateDetail>(`/api/step-templates/${id}`)
    const next = layoutOf(tpl)
    const byName = new Map(layout.steps.map((s) => [s.name.trim(), s.key]))
    const kept: GridValues = {}
    for (const item of next.items) {
      const common = values[cellKey(COMMON, item.conditionItemId)]
      if (common) kept[cellKey(COMMON, item.conditionItemId)] = common
      for (const s of next.steps) {
        const old = byName.get(s.name.trim())
        const v = old && values[cellKey(old, item.conditionItemId)]
        if (v) kept[cellKey(s.key, item.conditionItemId)] = v
      }
    }
    setTemplateId(id)
    setLayout(next)
    setValues(kept)
    message.info(`템플릿 '${tpl.header.stepTemplateName}' 구성을 불러왔습니다. 스텝·항목은 자유롭게 고칠 수 있습니다.`)
  }

  /** 다른 표준(같은 단위공정)의 현재 버전을 불러와 수정 (구 F_StandardCopy) */
  const copyFrom = async (sourceId: number) => {
    const src = await api<Detail>(`/api/standards/${sourceId}`)
    if (!src.version) return
    const grid = gridOf(src)
    setTemplateId(src.version.stepTemplateId)
    setLayout(grid.layout)
    setValues(grid.values)
    versionForm.setFieldsValue({
      chargeQty: src.version.chargeQty, chargeUnit: src.version.chargeUnit,
      runningTimeMin: src.version.runningTimeMin, remark: `${src.header.standardCode} v${src.version.versionNo} 에서 복사`,
    })
    message.info(`${src.header.standardName} v${src.version.versionNo} 조건을 불러왔습니다. 저장하면 새 버전입니다.`)
  }

  const save = async () => {
    setError(null)
    const v = await versionForm.validateFields()
    if (layout.steps.some((s) => s.name.trim() === '')) {
      setError('이름이 빈 스텝이 있습니다. 이름을 적거나 스텝을 지우세요.')
      return
    }
    const version = {
      ...v, stepTemplateId: templateId,
      steps: layout.steps.map((s) => s.name.trim()),
      items: layout.items.map((i) => i.conditionItemId),
      conditions: toConditions(layout, values),
    }
    setSaving(true)
    try {
      if (standard) {
        const r = await api<{ versionNo: number }>(`/api/standards/${standard.header.standardId}/versions`, { method: 'POST', body: version })
        message.success(`새 버전 v${r.versionNo} 으로 저장했습니다.`)
        onSaved()
      } else {
        const h = await header.validateFields()
        const r = await api<{ standardId: number; standardCode: string }>('/api/standards', { method: 'POST', body: { ...h, version } })
        message.success(`작업표준 ${r.standardCode} 을 등록했습니다.`)
        onSaved(r.standardId)
      }
    } catch (e) {
      if (e instanceof ApiError && e.code === 'DUPLICATE_STANDARD') setError(`${e.message} (작업표준 #${String(e.details.standardId)})`)
      else setError(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  const s = standard?.header
  return (
    <>
      {s ? (
        <HeaderInfo standard={s} canEdit={canEdit} onSaved={() => onSaved()} />
      ) : (
        <Form form={header} layout="vertical">
          <Row gutter={12}>
            <Col span={12}><Form.Item name="partId" label="품목" rules={[{ required: true }]}><Select showSearch optionFilterProp="label" options={parts.options} /></Form.Item></Col>
            <Col span={6}>
              <Form.Item name="unitProcessId" label="단위공정" rules={[{ required: true }]}>
                <Select options={units.options} onChange={(v: number) => { setUnitProcessId(v); setTemplateId(null) }} />
              </Form.Item>
            </Col>
            <Col span={6}><Form.Item name="equipmentTypeId" label="설비 유형"><Select allowClear options={types.options} /></Form.Item></Col>
            <Col span={8}><Form.Item name="equipmentId" label="설비 (비우면 유형 공통)"><Select allowClear showSearch optionFilterProp="label" options={equipment.options} /></Form.Item></Col>
            <Col span={8}><Form.Item name="customerId" label="거래처 (비우면 공통)"><Select allowClear showSearch optionFilterProp="label" options={customers.options} /></Form.Item></Col>
            <Col span={8}>
              <Form.Item name="heatProcessId" label="공정 (조건이 공정별로 다를 때)">
                <Select allowClear options={(heatProcesses.data ?? []).map((h) => ({ value: h.heatProcessId, label: h.heatProcessName }))} />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      )}

      <Form form={versionForm} layout="inline" disabled={!canEdit} style={{ margin: '16px 0', rowGap: 8 }} initialValues={standard?.version
        ? { chargeQty: standard.version.chargeQty, chargeUnit: standard.version.chargeUnit, runningTimeMin: standard.version.runningTimeMin }
        : { chargeQty: 0, chargeUnit: 'charge' }}>
        <Form.Item name="chargeQty" label="charge 수량" rules={[{ required: true }]}><InputNumber min={0} /></Form.Item>
        <Form.Item name="chargeUnit" label="단위"><Input style={{ width: 90 }} maxLength={20} /></Form.Item>
        <Form.Item name="runningTimeMin" label="작업시간(분)" tooltip="스케줄 작업시간 ① (설계 §7)"><InputNumber min={1} /></Form.Item>
        <Form.Item name="remark" label="변경 내용"><Input style={{ width: 220 }} maxLength={255} /></Form.Item>
      </Form>

      {canEdit && (
        <Space style={{ marginBottom: 8 }} wrap>
          <Select<number> style={{ width: 300 }} size="small" value={null} disabled={!unitProcessId}
            placeholder={unitProcessId ? '단계 템플릿 불러오기 (스텝·항목 채우기)' : '단위공정을 먼저 고르세요'}
            options={(templates.data ?? []).map((x) => ({ value: x.stepTemplateId, label: `${x.stepTemplateName}${x.equipmentTypeName ? ` · ${x.equipmentTypeName}` : ''}` }))}
            onChange={(id) => void loadTemplate(id)} />
          {templateId && <Typography.Text type="secondary">불러온 템플릿: {templates.data?.find((x) => x.stepTemplateId === templateId)?.stepTemplateName ?? `#${templateId}`}</Typography.Text>}
          {canCreateTemplate && (
            <Button size="small" icon={<SaveOutlined />} disabled={!unitProcessId || (layout.steps.length === 0 && layout.items.length === 0)}
              onClick={() => setSavingAsTemplate(true)}>이 구성을 템플릿으로 저장</Button>
          )}
        </Space>
      )}
      <ConditionGrid layout={layout} onLayoutChange={setLayout} values={values} onValuesChange={setValues} showCommon editable={canEdit} />
      {savingAsTemplate && unitProcessId && (
        <SaveAsTemplateModal layout={layout} unitProcessId={unitProcessId}
          equipmentTypeId={standard?.header.equipmentTypeId ?? header.getFieldValue('equipmentTypeId') ?? null}
          onClose={() => setSavingAsTemplate(false)}
          onSaved={(id) => { setSavingAsTemplate(false); setTemplateId(id); void templates.refetch() }} />
      )}

      {error && <Alert style={{ marginTop: 12 }} type="error" showIcon title={error} />}
      {canEdit && (
        <Space style={{ marginTop: 12 }} wrap>
          <Button type="primary" loading={saving} disabled={!unitProcessId} onClick={() => void save()}>{standard ? '새 버전으로 저장' : '등록'}</Button>
          <Select style={{ width: 280 }} placeholder={<><CopyOutlined /> 다른 품목 표준에서 복사</>} value={null} showSearch optionFilterProp="label"
            disabled={!unitProcessId}
            options={(copySources.data ?? []).filter((c) => c.standardId !== s?.standardId && c.currentVersionNo)
              .map((c) => ({ value: c.standardId, label: `${c.partName} · ${c.equipmentName ?? c.equipmentTypeName ?? '공통'} v${c.currentVersionNo}` }))}
            onChange={(v: number) => void copyFrom(v)} />
        </Space>
      )}
    </>
  )
}

function HeaderInfo({ standard: s, canEdit, onSaved }: { standard: Standard; canEdit: boolean; onSaved: () => void }) {
  const { message } = App.useApp()
  const [name, setName] = useState(s.standardName)
  const [active, setActive] = useState(s.isActive)
  const dirty = name !== s.standardName || active !== s.isActive
  return (
    <Space wrap size="middle">
      <span><Typography.Text type="secondary">품목</Typography.Text> {s.partName} ({s.partCode})</span>
      <span><Typography.Text type="secondary">단위공정</Typography.Text> {s.unitProcessName}</span>
      <span><Typography.Text type="secondary">설비</Typography.Text> {s.equipmentName ?? (s.equipmentTypeName ? `${s.equipmentTypeName} 공통` : '공통')}</span>
      {s.customerName && <span><Typography.Text type="secondary">거래처</Typography.Text> {s.customerName}</span>}
      {s.heatProcessName && <span><Typography.Text type="secondary">공정</Typography.Text> {s.heatProcessName}</span>}
      <Input value={name} disabled={!canEdit} onChange={(e) => setName(e.target.value)} style={{ width: 220 }} maxLength={100} />
      <Space size={4}><Switch size="small" checked={active} disabled={!canEdit} onChange={setActive} />사용</Space>
      {canEdit && dirty && (
        <Button size="small" onClick={async () => {
          await api(`/api/standards/${s.standardId}`, { method: 'PUT', body: { standardName: name, isActive: active } })
          message.success('저장했습니다.')
          onSaved()
        }}>이름·사용 저장</Button>
      )}
    </Space>
  )
}

/** 작업표준에서 만든 입력표 구성을 새 단계 템플릿으로 (구: 템플릿이 없으면 저장 시 자동 저장 → 신규는 선택) */
function SaveAsTemplateModal({ layout, unitProcessId, equipmentTypeId, onClose, onSaved }: {
  layout: GridLayout; unitProcessId: number; equipmentTypeId: number | null; onClose: () => void; onSaved: (id: number) => void
}) {
  const { message } = App.useApp()
  const [form] = Form.useForm<{ stepTemplateCode: string; stepTemplateName: string; scope: 'type' | 'all' }>()
  const [saving, setSaving] = useState(false)
  const save = async () => {
    const v = await form.validateFields()
    if (layout.steps.some((s) => s.name.trim() === '')) {
      message.error('이름이 빈 스텝이 있습니다.')
      return
    }
    setSaving(true)
    try {
      const r = await api<{ stepTemplateId: number }>('/api/step-templates', {
        method: 'POST',
        body: {
          stepTemplateCode: v.stepTemplateCode, stepTemplateName: v.stepTemplateName, unitProcessId, isActive: true,
          equipmentTypeId: v.scope === 'type' ? equipmentTypeId : null, equipmentId: null,
          steps: layout.steps.map((s) => s.name.trim()), conditionItemIds: layout.items.map((i) => i.conditionItemId),
        },
      })
      message.success(`템플릿 '${v.stepTemplateName}' 을 등록했습니다.`)
      onSaved(r.stepTemplateId)
    } catch (e) {
      message.error(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }
  return (
    <Modal open title="이 구성을 단계 템플릿으로 저장" onCancel={onClose} onOk={() => void save()} okText="저장" confirmLoading={saving} destroyOnHidden>
      <Typography.Paragraph type="secondary">
        스텝 {layout.steps.length}개 · 관리항목 {layout.items.length}개 (값은 저장하지 않음). 같은 단위공정의 다른 작업표준에서 불러올 수 있습니다.
      </Typography.Paragraph>
      <Form form={form} layout="vertical" initialValues={{ scope: equipmentTypeId ? 'type' : 'all' }}>
        <Form.Item name="stepTemplateCode" label="템플릿 코드" rules={[{ required: true, max: 50 }]}><Input autoFocus /></Form.Item>
        <Form.Item name="stepTemplateName" label="이름" rules={[{ required: true, max: 100 }]}><Input placeholder="예: 가스로 침탄" /></Form.Item>
        <Form.Item name="scope" label="적용 설비">
          <Radio.Group options={[
            { value: 'type', label: '이 설비 유형', disabled: !equipmentTypeId },
            { value: 'all', label: '설비 유형 공통' },
          ]} />
        </Form.Item>
      </Form>
    </Modal>
  )
}
