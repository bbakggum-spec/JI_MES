import { DeleteOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  App, Button, DatePicker, Form, Image, Input, InputNumber, Popconfirm, Result, Select, Space, Spin, Switch,
  Table, Tag, TimePicker, Typography, Upload,
} from 'antd'
import EditorWindow from '../../components/EditorWindow'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useState } from 'react'
import { ApiError, api, fieldErrors } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'
import { entityOf, type MasterField, type MasterMeta, type MasterOption, type MasterRow } from './masterTypes'

const PAGE_SIZE = 50

/**
 * 단순 기준정보 범용 화면 — API 정의(MasterCatalog)로 목록·편집 폼을 만든다 (설계 §22).
 * 코드 표시명은 공통코드, 참조 표시명은 서버가 함께 내려준 {필드}__label.
 */
export default function MasterPage({ menuKey }: PageProps) {
  const entity = entityOf(menuKey)
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const canDelete = useCan(menuKey, 'delete')
  const codes = useCommonCodes()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<MasterRow | 'new' | null>(null)

  const baseKey = [...queryKeys.master, entity]
  const meta = useQuery({
    queryKey: [...baseKey, 'meta'],
    queryFn: ({ signal }) => api<MasterMeta>(`/api/master/${entity}/meta`, { signal }),
    staleTime: Infinity,
  })
  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
  if (search) params.set('search', search)
  if (includeInactive) params.set('includeInactive', 'true')
  const list = useQuery({
    queryKey: [...baseKey, 'list', params.toString()],
    queryFn: ({ signal }) => api<{ items: MasterRow[]; total: number }>(`/api/master/${entity}?${params}`, { signal }),
    enabled: meta.isSuccess,
    placeholderData: (prev) => prev,
  })

  if (meta.isError) return <Result status="404" title="기준정보 정의가 없습니다" subTitle={menuKey} />
  if (!meta.data) return <Spin style={{ display: 'block', marginTop: 80 }} />
  const m = meta.data

  const remove = async (row: MasterRow) => {
    try {
      await api(`/api/master/${entity}/${row.id}`, { method: 'DELETE' })
      message.success('삭제했습니다.')
      await queryClient.invalidateQueries({ queryKey: baseKey })
    } catch (e) {
      message.error(e instanceof Error ? e.message : String(e))
    }
  }

  const columns: ColumnsType<MasterRow> = [
    ...m.fields.filter((f) => f.inList).map((f) => ({
      title: f.label,
      dataIndex: f.name,
      width: f.width ?? undefined,
      ellipsis: f.type === 'Text' && !f.width,
      align: (f.type === 'Integer' || f.type === 'Decimal' ? 'right' : undefined) as 'right' | undefined,
      render: (_: unknown, row: MasterRow) => display(f, row, codes),
    })),
    ...(m.images.length > 0 ? [{
      title: m.images.map((i) => i.label).join('·'), width: 70,
      render: (_: unknown, row: MasterRow) => m.images.map((i) => row[`has_${i.name}`] ? <Tag key={i.name}>있음</Tag> : null),
    }] : []),
    ...(canDelete && m.allowDelete ? [{
      title: '', width: 60, fixed: 'right' as const,
      render: (_: unknown, row: MasterRow) => (
        <Popconfirm title="삭제할까요?" onConfirm={() => void remove(row)}>
          <Button size="small" danger icon={<DeleteOutlined />} onClick={(e) => e.stopPropagation()} />
        </Popconfirm>
      ),
    }] : []),
  ]

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <div>
          <Typography.Title level={4} style={{ margin: 0 }}>{m.label}</Typography.Title>
          {m.description && <Typography.Text type="secondary">{m.description}</Typography.Text>}
        </div>
        <Space wrap>
          <Input.Search allowClear placeholder="검색" style={{ width: 220 }} onSearch={(v) => { setSearch(v.trim()); setPage(1) }} />
          {m.hasActive && (
            <Space size={4}><Switch size="small" checked={includeInactive} onChange={(v) => { setIncludeInactive(v); setPage(1) }} />사용 중지 포함</Space>
          )}
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>추가</Button>}
        </Space>
      </Space>
      <Table<MasterRow> rowKey="id" size="middle" loading={list.isFetching} dataSource={list.data?.items ?? []} columns={columns}
        scroll={{ x: 'max-content' }}
        rowClassName={(r) => (m.hasActive && r.is_active === false ? 'master-inactive' : '')}
        onRow={(row) => ({ onClick: () => setEditing(row), style: { cursor: 'pointer', opacity: m.hasActive && row.is_active === false ? 0.5 : 1 } })}
        pagination={{
          current: page, pageSize: PAGE_SIZE, total: list.data?.total ?? 0, showSizeChanger: false,
          showTotal: (t) => `${t.toLocaleString()}건`, onChange: setPage,
        }} />
      {editing && (
        <EditWindow meta={m} entity={entity} row={editing === 'new' ? null : editing} readOnly={editing === 'new' ? !canCreate : !canUpdate}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); void queryClient.invalidateQueries({ queryKey: baseKey }) }} />
      )}
    </>
  )
}

function display(f: MasterField, row: MasterRow, codes: ReturnType<typeof useCommonCodes>) {
  const v = row[f.name]
  if (v === null || v === undefined || v === '') return null
  switch (f.type) {
    case 'Bool': return v ? <Tag color="green">예</Tag> : <Tag>아니오</Tag>
    case 'Code': return codes.name(f.codeGroup!, String(v))
    case 'Lookup': return String(row[`${f.name}__label`] ?? v)
    case 'Decimal': return Number(v).toLocaleString()
    default: return String(v)
  }
}

function EditWindow({ meta, entity, row, readOnly, onClose, onSaved }: {
  meta: MasterMeta
  entity: string
  row: MasterRow | null
  readOnly: boolean
  onClose: () => void
  onSaved: () => void
}) {
  const [form] = Form.useForm()
  const { message } = App.useApp()
  const [saving, setSaving] = useState(false)
  const isNew = row === null

  const initial = Object.fromEntries(meta.fields.map((f) => {
    const v = isNew ? f.default : row[f.name]
    if (v === null || v === undefined) return [f.name, undefined]
    if (f.type === 'Date') return [f.name, dayjs(String(v))]
    if (f.type === 'Time') return [f.name, dayjs(String(v), 'HH:mm')]
    return [f.name, v]
  }))

  const save = async () => {
    const values = await form.validateFields()
    const body: Record<string, unknown> = {}
    for (const f of meta.fields) {
      const v = values[f.name]
      body[f.name] = v === undefined || v === '' ? null
        : f.type === 'Date' ? (v as dayjs.Dayjs).format('YYYY-MM-DD')
          : f.type === 'Time' ? (v as dayjs.Dayjs).format('HH:mm')
            : v
    }
    if (!isNew && values.__reason) body.__reason = values.__reason
    setSaving(true)
    try {
      if (isNew) await api(`/api/master/${entity}`, { method: 'POST', body })
      else await api(`/api/master/${entity}/${row.id}`, { method: 'PUT', body })
      message.success('저장했습니다.')
      onSaved()
    } catch (e) {
      const errors = fieldErrors(e)
      if (errors.length > 0) form.setFields(errors.map((x) => ({ name: x.name, errors: x.errors })))
      else message.error(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <EditorWindow onClose={onClose} size="large" title={isNew ? `${meta.label} 추가` : `${meta.label} — ${String(row[meta.fields[1]?.name] ?? row.id)}`}
      extra={!readOnly && <Button type="primary" loading={saving} onClick={() => void save()}>저장</Button>}>
      <Form form={form} layout="vertical" initialValues={initial} disabled={readOnly}>
        {meta.fields.map((f) => (
          <Form.Item key={f.name} name={f.name} label={f.label} extra={f.help} valuePropName={f.type === 'Bool' ? 'checked' : 'value'}
            rules={[
              ...(f.required ? [{ required: true, message: `${f.label}은(는) 필수입니다.` }] : []),
              ...(f.maxLength ? [{ max: f.maxLength, message: `${f.maxLength}자 이하` }] : []),
            ]}>
            <FieldInput field={f} />
          </Form.Item>
        ))}
        {!isNew && !readOnly && (
          <Form.Item name="__reason" label="변경 사유"><Input maxLength={255} placeholder="변경 이력에 남습니다" /></Form.Item>
        )}
      </Form>
      {!isNew && meta.images.map((img) => (
        <ImageField key={img.name} entity={entity} id={row.id} image={img} readOnly={readOnly} hasImage={Boolean(row[`has_${img.name}`])} />
      ))}
    </EditorWindow>
  )
}

function FieldInput({ field: f, ...rest }: { field: MasterField; value?: unknown; onChange?: (v: unknown) => void }) {
  const codes = useCommonCodes()
  const options = useQuery({
    queryKey: [...queryKeys.master, f.lookup, 'options'],
    queryFn: ({ signal }) => api<MasterOption[]>(`/api/master/${f.lookup}/options`, { signal }),
    enabled: f.type === 'Lookup',
  })
  const props = rest as Record<string, never>
  switch (f.type) {
    case 'TextArea': return <Input.TextArea rows={3} maxLength={f.maxLength ?? undefined} {...props} />
    case 'Integer': return <InputNumber precision={0} min={f.min ?? undefined} max={f.max ?? undefined} style={{ width: 200 }} {...props} />
    case 'Decimal': return <InputNumber min={f.min ?? undefined} max={f.max ?? undefined} style={{ width: 200 }} {...props} />
    case 'Bool': return <Switch {...props} />
    case 'Date': return <DatePicker {...props} />
    case 'Time': return <TimePicker format="HH:mm" minuteStep={5} {...props} />
    case 'Code':
      return <Select allowClear={!f.required} style={{ width: 240 }} {...props}
        options={codes.options(f.codeGroup!).map((c) => ({ value: c.code, label: c.codeName }))} />
    case 'Lookup':
      return <Select allowClear={!f.required} showSearch optionFilterProp="label" loading={options.isPending} style={{ width: 300 }} {...props}
        options={(options.data ?? []).map((o) => ({ value: o.value, label: o.active ? o.label : `${o.label} (중지)`, disabled: !o.active }))} />
    default: return <Input maxLength={f.maxLength ?? undefined} {...props} />
  }
}

function ImageField({ entity, id, image, readOnly, hasImage }: {
  entity: string; id: number; image: { name: string; label: string }; readOnly: boolean; hasImage: boolean
}) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [version, setVersion] = useState(0)
  const [present, setPresent] = useState(hasImage)
  const url = `/api/master/${entity}/${id}/image/${image.name}?v=${version}`

  const changed = (has: boolean) => {
    setPresent(has)
    setVersion((v) => v + 1)
    void queryClient.invalidateQueries({ queryKey: [...queryKeys.master, entity] })
  }

  return (
    <div style={{ marginTop: 16 }}>
      <Typography.Text strong>{image.label}</Typography.Text>
      <div style={{ margin: '8px 0' }}>
        {present ? <Image src={url} width={120} alt={image.label} /> : <Typography.Text type="secondary">등록된 이미지가 없습니다.</Typography.Text>}
      </div>
      {!readOnly && (
        <Space>
          <Upload accept="image/png,image/jpeg" showUploadList={false}
            customRequest={async ({ file }) => {
              const form = new FormData()
              form.append('file', file as Blob)
              try {
                await api(`/api/master/${entity}/${id}/image/${image.name}`, { method: 'PUT', body: form })
                message.success(`${image.label}을(를) 등록했습니다.`)
                changed(true)
              } catch (e) {
                message.error(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
              }
            }}>
            <Button icon={<UploadOutlined />}>이미지 등록</Button>
          </Upload>
          {present && (
            <Popconfirm title={`${image.label}을(를) 지울까요?`} onConfirm={async () => {
              await api(`/api/master/${entity}/${id}/image/${image.name}`, { method: 'DELETE' })
              changed(false)
            }}>
              <Button danger>삭제</Button>
            </Popconfirm>
          )}
        </Space>
      )}
    </div>
  )
}
