import { CopyOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Col, Drawer, Empty, Form, Input, InputNumber, Row, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useMemo, useState } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'
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

interface Condition {
  stepTemplateItemId: number | null
  conditionItemId: number
  conditionValue: string | null
}

interface Detail {
  header: Standard
  versions: Version[]
  version: Version | null
  template: StepTemplateDetail | null
  conditions: Condition[]
}

type Values = Record<string, string>
const cellKey = (step: number | null, item: number) => `${step ?? 0}:${item}`

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
        <StandardDrawer id={editing === 'new' ? null : editing} canEdit={editing === 'new' ? canCreate : canUpdate} onClose={() => setEditing(null)}
          onSaved={(id) => { setEditing(id); void queryClient.invalidateQueries({ queryKey: key }) }} />
      )}
    </>
  )
}

function StandardDrawer({ id, canEdit, onClose, onSaved }: { id: number | null; canEdit: boolean; onClose: () => void; onSaved: (id: number) => void }) {
  const [viewVersionId, setViewVersionId] = useState<number | null>(null)
  const detail = useQuery({
    queryKey: [...queryKeys.master, 'standard', 'detail', id],
    queryFn: ({ signal }) => api<Detail>(`/api/standards/${id}`, { signal }),
    enabled: id !== null,
  })
  if (id !== null && !detail.data) return null
  const d = detail.data
  return (
    <Drawer open onClose={onClose} size={1200} title={d ? `${d.header.standardName} (${d.header.standardCode})` : '작업표준 추가'} destroyOnHidden>
      {d ? (
        <Tabs items={[
          { key: 'edit', label: `조건 (현재 v${d.version?.versionNo ?? '-'})`, children: <Editor key={detail.dataUpdatedAt} standard={d} canEdit={canEdit} onSaved={() => { void detail.refetch(); onSaved(id!) }} /> },
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
                {viewVersionId && <OldVersion standardId={d.header.standardId} versionId={viewVersionId} />}
              </>
            ),
          },
        ]} />
      ) : <Editor canEdit={canEdit} onSaved={(newId) => onSaved(newId!)} />}
    </Drawer>
  )
}

function OldVersion({ standardId, versionId }: { standardId: number; versionId: number }) {
  const v = useQuery({
    queryKey: [...queryKeys.master, 'standard', 'version', versionId],
    queryFn: ({ signal }) => api<Detail>(`/api/standards/${standardId}?versionId=${versionId}`, { signal }),
  })
  if (!v.data) return null
  const values = Object.fromEntries(v.data.conditions.map((c) => [cellKey(c.stepTemplateItemId, c.conditionItemId), c.conditionValue ?? '']))
  return (
    <div style={{ marginTop: 16 }}>
      <Typography.Text strong>v{v.data.version?.versionNo} 조건 (보기 전용)</Typography.Text>
      <Matrix template={v.data.template} values={values} readOnly />
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
  stepTemplateId?: number | null
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
  const [values, setValues] = useState<Values>(() =>
    Object.fromEntries((standard?.conditions ?? []).map((c) => [cellKey(c.stepTemplateItemId, c.conditionItemId), c.conditionValue ?? ''])))
  const [unitProcessId, setUnitProcessId] = useState<number | undefined>(standard?.header.unitProcessId)
  const [templateId, setTemplateId] = useState<number | null>(standard?.version?.stepTemplateId ?? null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const templates = useQuery({
    queryKey: [...queryKeys.master, 'step_template', 'byUnit', unitProcessId],
    queryFn: ({ signal }) => api<StepTemplate[]>(`/api/step-templates?unitProcessId=${unitProcessId}`, { signal }),
    enabled: unitProcessId !== undefined,
  })
  const template = useQuery({
    queryKey: [...queryKeys.master, 'step_template', 'detail', templateId],
    queryFn: ({ signal }) => api<StepTemplateDetail>(`/api/step-templates/${templateId}`, { signal }),
    enabled: templateId !== null,
  })
  const copySources = useQuery({
    queryKey: [...queryKeys.master, 'standard', 'byUnit', unitProcessId],
    queryFn: ({ signal }) => api<Standard[]>(`/api/standards?unitProcessId=${unitProcessId}`, { signal }),
    enabled: unitProcessId !== undefined && canEdit,
  })

  /** 다른 표준(같은 단위공정)의 현재 버전을 불러와 수정 (구 F_StandardCopy) */
  const copyFrom = async (sourceId: number) => {
    const src = await api<Detail>(`/api/standards/${sourceId}`)
    if (!src.version) return
    setTemplateId(src.version.stepTemplateId)
    versionForm.setFieldsValue({
      stepTemplateId: src.version.stepTemplateId, chargeQty: src.version.chargeQty, chargeUnit: src.version.chargeUnit,
      runningTimeMin: src.version.runningTimeMin, remark: `${src.header.standardCode} v${src.version.versionNo} 에서 복사`,
    })
    setValues(Object.fromEntries(src.conditions.map((c) => [cellKey(c.stepTemplateItemId, c.conditionItemId), c.conditionValue ?? ''])))
    message.info(`${src.header.standardName} v${src.version.versionNo} 조건을 불러왔습니다. 저장하면 새 버전입니다.`)
  }

  const save = async () => {
    setError(null)
    const v = await versionForm.validateFields()
    const conditions = Object.entries(values)
      .filter(([, value]) => value.trim() !== '')
      .map(([k, value]) => {
        const [step, item] = k.split(':').map(Number)
        return { stepTemplateItemId: step === 0 ? null : step, conditionItemId: item, conditionValue: value }
      })
      // 현재 템플릿에 없는 단계 값은 버린다 (템플릿을 바꾼 경우)
      .filter((c) => c.stepTemplateItemId === null || (template.data?.steps ?? []).some((s) => s.stepTemplateItemId === c.stepTemplateItemId))
    const version = { ...v, stepTemplateId: templateId, conditions }
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
                <Select options={units.options} onChange={(v: number) => { setUnitProcessId(v); setTemplateId(null); setValues({}) }} />
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
        <Form.Item label="단계 템플릿">
          <Select style={{ width: 220 }} value={templateId} onChange={setTemplateId} placeholder={unitProcessId ? '템플릿 선택' : '단위공정 먼저'}
            options={(templates.data ?? []).map((t) => ({ value: t.stepTemplateId, label: `${t.stepTemplateName}${t.equipmentTypeName ? ` · ${t.equipmentTypeName}` : ''}` }))} />
        </Form.Item>
        <Form.Item name="chargeQty" label="charge 수량" rules={[{ required: true }]}><InputNumber min={0} /></Form.Item>
        <Form.Item name="chargeUnit" label="단위"><Input style={{ width: 90 }} maxLength={20} /></Form.Item>
        <Form.Item name="runningTimeMin" label="작업시간(분)" tooltip="스케줄 작업시간 ① (설계 §7)"><InputNumber min={1} /></Form.Item>
        <Form.Item name="remark" label="변경 내용"><Input style={{ width: 220 }} maxLength={255} /></Form.Item>
      </Form>

      <Matrix template={template.data ?? null} values={values} readOnly={!canEdit} onChange={(k, v) => setValues((old) => ({ ...old, [k]: v }))} />

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

/** 관리항목(행) × [공통 + 단계](열) — 숫자 항목은 숫자만 (서버도 검증) */
function Matrix({ template, values, readOnly, onChange }: {
  template: StepTemplateDetail | null; values: Values; readOnly?: boolean; onChange?: (key: string, value: string) => void
}) {
  const conditionItems = useOptions('/api/master/condition_item/options')
  const columns = useMemo(() => template ? [{ id: null as number | null, name: '공통' }, ...template.steps.map((s) => ({ id: s.stepTemplateItemId as number | null, name: s.stepName }))] : [], [template])
  // 템플릿 관리항목 + 값은 있는데 템플릿에 없는 항목 (템플릿 변경·이관 자료) — 숨기면 저장 시 값이 사라진 것처럼 보인다
  const rows = useMemo(() => {
    if (!template) return []
    const inTemplate = new Set(template.conditions.map((c) => c.conditionItemId))
    const extra = [...new Set(Object.entries(values).filter(([, v]) => v !== '').map(([k]) => Number(k.split(':')[1])))]
      .filter((id) => !inTemplate.has(id))
      .map((id) => ({ conditionItemId: id, conditionItemName: conditionItems.labelOf(id) ?? `#${id}`, unitCode: null, valueType: 'TEXT', sequenceNo: 9999, extra: true }))
    return [...template.conditions.map((c) => ({ ...c, extra: false })), ...extra]
  }, [template, values, conditionItems])
  if (!template) return <Empty description="단계 템플릿을 선택하면 조건 입력표가 나옵니다." />
  return (
    <Table size="small" pagination={false} bordered rowKey="conditionItemId" dataSource={rows} scroll={{ x: 'max-content' }}
      columns={[
        {
          title: '관리항목', fixed: 'left' as const, width: 130,
          render: (_: unknown, c) => (
            <>
              {c.conditionItemName}{c.unitCode && <Typography.Text type="secondary"> ({c.unitCode})</Typography.Text>}
              {c.extra && <div><Tag color="warning">템플릿 외</Tag></div>}
            </>
          ),
        },
        ...columns.map((col) => ({
          title: col.name, width: 100,
          render: (_: unknown, c: StepTemplateDetail['conditions'][number]) => {
            const k = cellKey(col.id, c.conditionItemId)
            const value = values[k] ?? ''
            const invalid = c.valueType === 'NUMBER' && value.trim() !== '' && Number.isNaN(Number(value))
            return readOnly
              ? value
              : <Input size="small" value={value} status={invalid ? 'error' : undefined} inputMode={c.valueType === 'NUMBER' ? 'decimal' : undefined}
                  onChange={(e) => onChange?.(k, e.target.value)} />
          },
        })),
      ]} />
  )
}
