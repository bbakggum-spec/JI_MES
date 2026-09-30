import { ArrowDownOutlined, ArrowUpOutlined, CopyOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Input, InputNumber, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import EditorWindow from '../../components/EditorWindow'
import dayjs from 'dayjs'
import { useState } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useDataVersion } from '../../hooks/useDataVersion'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'

interface InspectionStandard {
  inspectionStandardId: number
  partId: number
  partCode: string
  partName: string
  partNumber: string | null
  customerId: number | null
  customerName: string | null
  isActive: boolean
  currentVersionNo: number | null
  criteriaCount: number
}

interface Criteria {
  itemType: string | null
  itemName: string
  location: string | null
  specificationValue: string | null
  toolName: string | null
  testValue: string | null
  scale: string | null
  rangeType: string | null
  lowerLimit: number | null
  upperLimit: number | null
  hardnessLimit: number | null
  unitCode: string | null
  sampleCount: number
  testCount: number
  points: (string | null)[]
}

interface Version {
  inspectionStandardVersionId: number
  versionNo: number
  effectiveFrom: string
  effectiveTo: string | null
  isCurrent: boolean
  remark: string | null
  createdByName: string | null
  usageCount: number
}

interface Detail {
  header: InspectionStandard
  versions: Version[]
  version: Version | null
  criteria: Criteria[]
}

const emptyRow = (): Criteria => ({
  itemType: 'APPEARANCE', itemName: '', location: null, specificationValue: null, toolName: null, testValue: null, scale: null,
  rangeType: 'BETWEEN', lowerLimit: null, upperLimit: null, hardnessLimit: null, unitCode: null, sampleCount: 1, testCount: 1, points: [],
})

/** 검사기준 (구 F_InspectionCriteriaForm) — 품목(+거래처) × 항목 × 측정 위치, 검사가 쓴 버전은 보존 */
export default function InspectionStandardsPage({ menuKey }: PageProps) {
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const key = [...queryKeys.master, 'inspection_standard']
  const list = useQuery({
    queryKey: [...key, search],
    queryFn: ({ signal }) => api<InspectionStandard[]>(`/api/inspection-standards?search=${encodeURIComponent(search)}`, { signal }),
  })

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <div>
          <Typography.Title level={4} style={{ margin: 0 }}>검사기준</Typography.Title>
          <Typography.Text type="secondary">검사 자동 판정·성적서 기준값 (C 치환자)의 원천</Typography.Text>
        </div>
        <Space>
          <Input.Search allowClear placeholder="품목·품번" style={{ width: 220 }} onSearch={(v) => setSearch(v.trim())} />
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>검사기준 추가</Button>}
        </Space>
      </Space>
      <Table<InspectionStandard> rowKey="inspectionStandardId" size="middle" loading={list.isFetching} dataSource={list.data ?? []} pagination={false}
        onRow={(s) => ({ onClick: () => setEditing(s.inspectionStandardId), style: { cursor: 'pointer', opacity: s.isActive ? 1 : 0.5 } })}
        columns={[
          { title: '품목', render: (_: unknown, s) => <>{s.partName} <Typography.Text type="secondary">{s.partCode}{s.partNumber ? ` / ${s.partNumber}` : ''}</Typography.Text></> },
          { title: '거래처', dataIndex: 'customerName', width: 160, render: (v: string | null) => v ?? <Tag>공통</Tag> },
          { title: '항목', dataIndex: 'criteriaCount', width: 70, align: 'right' },
          { title: '버전', dataIndex: 'currentVersionNo', width: 70, render: (v: number | null) => v && `v${v}` },
        ]} />
      {editing !== null && (
        <StandardWindow id={editing === 'new' ? null : editing} canEdit={editing === 'new' ? canCreate : canUpdate} onClose={() => setEditing(null)}
          onSaved={(id) => { setEditing(id); void queryClient.invalidateQueries({ queryKey: key }) }} />
      )}
    </>
  )
}

function StandardWindow({ id, canEdit, onClose, onSaved }: { id: number | null; canEdit: boolean; onClose: () => void; onSaved: (id: number) => void }) {
  const [viewVersion, setViewVersion] = useState<number | null>(null)
  const detail = useQuery({
    queryKey: [...queryKeys.master, 'inspection_standard', 'detail', id],
    queryFn: ({ signal }) => api<Detail>(`/api/inspection-standards/${id}`, { signal }),
    enabled: id !== null,
  })
  const old = useQuery({
    queryKey: [...queryKeys.master, 'inspection_standard', 'version', viewVersion],
    queryFn: ({ signal }) => api<Detail>(`/api/inspection-standards/${id}?versionId=${viewVersion}`, { signal }),
    enabled: viewVersion !== null,
  })
  const dataVersion = useDataVersion(detail.data)
  if (id !== null && !detail.data) return null
  const d = detail.data
  return (
    <EditorWindow onClose={onClose} size={1300} destroyOnHidden
      title={d ? `${d.header.partName} — ${d.header.customerName ?? '거래처 공통'}` : '검사기준 추가'}>
      {d ? (
        <Tabs items={[
          { key: 'edit', label: `검사 항목 (현재 v${d.version?.versionNo ?? '-'})`, children: <Editor key={dataVersion} detail={d} canEdit={canEdit} onSaved={() => { void detail.refetch(); onSaved(id!) }} /> },
          {
            key: 'versions', label: `버전 이력 (${d.versions.length})`, children: (
              <>
                <Table<Version> size="small" rowKey="inspectionStandardVersionId" pagination={false} dataSource={d.versions}
                  onRow={(v) => ({ onClick: () => setViewVersion(v.inspectionStandardVersionId), style: { cursor: 'pointer' } })}
                  columns={[
                    { title: '버전', dataIndex: 'versionNo', width: 80, render: (v: number, r) => <Space>v{v}{r.isCurrent && <Tag color="blue">현재</Tag>}</Space> },
                    { title: '적용', width: 200, render: (_: unknown, r) => `${dayjs(r.effectiveFrom).format('YYYY-MM-DD HH:mm')} ~ ${r.effectiveTo ? dayjs(r.effectiveTo).format('MM-DD HH:mm') : ''}` },
                    { title: '검사 사용', dataIndex: 'usageCount', width: 90, align: 'right' },
                    { title: '작성', dataIndex: 'createdByName', width: 100 },
                    { title: '비고', dataIndex: 'remark' },
                  ]} />
                {old.data && (
                  <div style={{ marginTop: 16 }}>
                    <Typography.Text strong>v{old.data.version?.versionNo} (보기 전용)</Typography.Text>
                    <CriteriaTable rows={old.data.criteria} readOnly />
                  </div>
                )}
              </>
            ),
          },
        ]} />
      ) : <Editor canEdit={canEdit} onSaved={onSaved} />}
    </EditorWindow>
  )
}

function Editor({ detail, canEdit, onSaved }: { detail?: Detail; canEdit: boolean; onSaved: (id: number) => void }) {
  const { message } = App.useApp()
  const parts = useOptions('/api/parts/options')
  const customers = useOptions('/api/master/customer/options')
  const [rows, setRows] = useState<Criteria[]>(detail?.criteria.length ? detail.criteria : [emptyRow()])
  const [partId, setPartId] = useState<number | undefined>()
  const [customerId, setCustomerId] = useState<number | null>(null)
  const [remark, setRemark] = useState('')
  const [active, setActive] = useState(detail?.header.isActive ?? true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const sources = useQuery({
    queryKey: [...queryKeys.master, 'inspection_standard', 'sources'],
    queryFn: ({ signal }) => api<InspectionStandard[]>('/api/inspection-standards', { signal }),
    enabled: canEdit,
  })

  const copyFrom = async (sourceId: number) => {
    const src = await api<Detail>(`/api/inspection-standards/${sourceId}`)
    setRows(src.criteria)
    setRemark(`${src.header.partName} v${src.version?.versionNo ?? ''} 에서 복사`)
    message.info('검사 항목을 불러왔습니다. 확인 후 저장하세요.')
  }

  const save = async () => {
    setSaving(true)
    setError(null)
    try {
      if (detail) {
        if (active !== detail.header.isActive)
          await api(`/api/inspection-standards/${detail.header.inspectionStandardId}`, { method: 'PUT', body: { isActive: active } })
        const r = await api<{ versionNo: number; newVersion: boolean }>(`/api/inspection-standards/${detail.header.inspectionStandardId}/criteria`, {
          method: 'PUT', body: { criteria: rows, remark: remark || null },
        })
        message.success(r.newVersion ? `검사에서 쓰는 기준이라 새 버전 v${r.versionNo} 을 만들었습니다.` : `현재 버전 v${r.versionNo} 을 수정했습니다.`)
        onSaved(detail.header.inspectionStandardId)
      } else {
        if (!partId) { setError('품목을 선택하세요.'); return }
        const r = await api<{ inspectionStandardId: number }>('/api/inspection-standards', { method: 'POST', body: { partId, customerId, criteria: rows, remark: remark || null } })
        message.success('검사기준을 등록했습니다.')
        onSaved(r.inspectionStandardId)
      }
    } catch (e) {
      setError(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' / ') : e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      {!detail && (
        <Space wrap style={{ marginBottom: 12 }}>
          <Select showSearch optionFilterProp="label" placeholder="품목" style={{ width: 320 }} value={partId} onChange={setPartId} options={parts.options} />
          <Select allowClear showSearch optionFilterProp="label" placeholder="거래처 (비우면 공통)" style={{ width: 200 }}
            value={customerId ?? undefined} onChange={(v) => setCustomerId(v ?? null)} options={customers.options} />
        </Space>
      )}
      {detail && (
        <Space style={{ marginBottom: 12 }}>
          <Space size={4}><Switch size="small" checked={active} disabled={!canEdit} onChange={setActive} />사용</Space>
          {detail.version && detail.version.usageCount > 0 && <Tag color="warning">v{detail.version.versionNo} 은 검사 {detail.version.usageCount}건에서 사용 → 저장하면 새 버전</Tag>}
        </Space>
      )}
      <CriteriaTable rows={rows} readOnly={!canEdit} onChange={setRows} />
      {error && <Alert style={{ marginTop: 12 }} type="error" showIcon title={error} />}
      {canEdit && (
        <Space style={{ marginTop: 12 }} wrap>
          <Button icon={<PlusOutlined />} onClick={() => setRows([...rows, emptyRow()])}>항목 추가</Button>
          <Input placeholder="변경 내용 (버전 비고)" value={remark} onChange={(e) => setRemark(e.target.value)} style={{ width: 260 }} maxLength={255} />
          <Button type="primary" loading={saving} onClick={() => void save()}>{detail ? '저장' : '등록'}</Button>
          <Select style={{ width: 300 }} placeholder={<><CopyOutlined /> 다른 검사기준에서 복사</>} value={null} showSearch optionFilterProp="label"
            options={(sources.data ?? []).filter((s) => s.inspectionStandardId !== detail?.header.inspectionStandardId)
              .map((s) => ({ value: s.inspectionStandardId, label: `${s.partName} · ${s.customerName ?? '공통'} v${s.currentVersionNo ?? ''}` }))}
            onChange={(v: number) => void copyFrom(v)} />
        </Space>
      )}
    </>
  )
}

function CriteriaTable({ rows, readOnly, onChange }: { rows: Criteria[]; readOnly?: boolean; onChange?: (rows: Criteria[]) => void }) {
  const codes = useCommonCodes()
  const set = (i: number, patch: Partial<Criteria>) => onChange?.(rows.map((r, j) => (j === i ? { ...r, ...patch } : r)))
  const move = (i: number, d: number) => {
    const next = [...rows]
    ;[next[i], next[i + d]] = [next[i + d], next[i]]
    onChange?.(next)
  }
  const text = (i: number, field: keyof Criteria, width: number) => (_: unknown, r: Criteria) => readOnly
    ? (r[field] as string | null)
    : <Input size="small" style={{ width }} value={(r[field] as string | null) ?? ''} onChange={(e) => set(i, { [field]: e.target.value || null })} />
  const needs = (range: string | null, bound: 'lower' | 'upper') => codes.attr<Record<string, boolean>>('RANGE_TYPE', range ?? 'NONE')?.[bound] === true

  return (
    <Table<Criteria> size="small" bordered pagination={false} rowKey={(_, i) => String(i)} dataSource={rows} scroll={{ x: 'max-content' }}
      columns={[
        { title: '#', width: 40, render: (_: unknown, __, i) => i + 1 },
        {
          title: '유형', width: 140, render: (_: unknown, r, i) => {
            if (readOnly) return codes.name('INSPECTION_ITEM_TYPE', r.itemType)
            const options = codes.options('INSPECTION_ITEM_TYPE')
            // 구 자료(이관 전·스모크)는 코드 대신 이름('경도')이 들어 있을 수 있다 → 다시 고르도록 표시
            const valid = r.itemType === null || options.some((c) => c.code === r.itemType)
            return (
              <Select size="small" style={{ width: 130 }} value={valid ? r.itemType : undefined} status={valid ? undefined : 'error'}
                placeholder={valid ? undefined : `'${r.itemType}' → 다시 선택`} onChange={(v) => set(i, { itemType: v })}
                options={options.map((c) => ({ value: c.code, label: c.codeName }))} />
            )
          },
        },
        { title: '항목', width: 130, render: (a: unknown, r, i) => text(i, 'itemName', 120)(a, r) },
        { title: '위치', width: 100, render: (a: unknown, r, i) => text(i, 'location', 90)(a, r) },
        { title: '요구사항 (성적서 표기)', width: 160, render: (a: unknown, r, i) => text(i, 'specificationValue', 150)(a, r) },
        {
          title: '판정', width: 150, render: (_: unknown, r, i) => readOnly ? codes.name('RANGE_TYPE', r.rangeType)
            : <Select size="small" style={{ width: 140 }} value={r.rangeType ?? 'NONE'} onChange={(v) => set(i, { rangeType: v })}
                options={codes.options('RANGE_TYPE').map((c) => ({ value: c.code, label: c.codeName }))} />,
        },
        {
          title: '하한', width: 90, render: (_: unknown, r, i) => readOnly ? r.lowerLimit
            : <InputNumber size="small" style={{ width: 80 }} value={r.lowerLimit} status={needs(r.rangeType, 'lower') && r.lowerLimit == null ? 'error' : undefined}
                onChange={(v) => set(i, { lowerLimit: v })} />,
        },
        {
          title: '상한', width: 90, render: (_: unknown, r, i) => readOnly ? r.upperLimit
            : <InputNumber size="small" style={{ width: 80 }} value={r.upperLimit} status={needs(r.rangeType, 'upper') && r.upperLimit == null ? 'error' : undefined}
                onChange={(v) => set(i, { upperLimit: v })} />,
        },
        { title: '스케일', width: 80, render: (a: unknown, r, i) => text(i, 'scale', 70)(a, r) },
        { title: '측정기', width: 100, render: (a: unknown, r, i) => text(i, 'toolName', 90)(a, r) },
        { title: '시험값', width: 90, render: (a: unknown, r, i) => text(i, 'testValue', 80)(a, r) },
        {
          title: '시료', width: 70, render: (_: unknown, r, i) => readOnly ? r.sampleCount
            : <InputNumber size="small" min={1} precision={0} style={{ width: 60 }} value={r.sampleCount} onChange={(v) => set(i, { sampleCount: v ?? 1 })} />,
        },
        {
          title: '측정 위치 (P1…)', width: 240, render: (_: unknown, r, i) => readOnly ? r.points.filter(Boolean).map((p, k) => <Tag key={k}>{p}</Tag>)
            : <Select size="small" mode="tags" style={{ width: 230 }} value={r.points.filter((p): p is string => !!p)} placeholder="입력 후 Enter"
                onChange={(v: string[]) => set(i, { points: v })} tokenSeparators={[',']} />,
        },
        ...(readOnly ? [] : [{
          title: '', width: 110, fixed: 'right' as const, render: (_: unknown, __: Criteria, i: number) => (
            <Space size={2}>
              <Button size="small" icon={<ArrowUpOutlined />} disabled={i === 0} onClick={() => move(i, -1)} />
              <Button size="small" icon={<ArrowDownOutlined />} disabled={i === rows.length - 1} onClick={() => move(i, 1)} />
              <Button size="small" danger icon={<DeleteOutlined />} onClick={() => onChange?.(rows.filter((_, j) => j !== i))} />
            </Space>
          ),
        }]),
      ]} />
  )
}
