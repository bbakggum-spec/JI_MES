import { ArrowDownOutlined, ArrowUpOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Checkbox, Form, Input, Radio, Select, Space, Switch, Table, Tag, Typography } from 'antd'
import EditorWindow from '../../components/EditorWindow'
import dayjs from 'dayjs'
import { useState } from 'react'
import { api, fieldErrors } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'

interface HeatProcess {
  heatProcessId: number
  heatProcessCode: string
  heatProcessName: string
  description: string | null
  isActive: boolean
  currentVersionNo: number | null
  routeSummary: string | null
}

interface Operation {
  unitProcessId: number
  unitProcessName?: string
  isMainProcess: boolean
  isRequired: boolean
}

interface Version {
  heatProcessVersionId: number
  versionNo: number
  effectiveFrom: string
  effectiveTo: string | null
  isCurrent: boolean
  remark: string | null
  usageCount: number
  operations: Operation[]
}

/** 공정 경로 (설계 §2.1) — 단위공정 순서 + 주공정. 거래가 쓴 버전은 고치지 않고 새 버전 */
export default function HeatProcessesPage({ menuKey }: PageProps) {
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const queryClient = useQueryClient()
  const [includeInactive, setIncludeInactive] = useState(false)
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const key = [...queryKeys.master, 'heat_process']
  const list = useQuery({
    queryKey: [...key, includeInactive],
    queryFn: ({ signal }) => api<HeatProcess[]>(`/api/heat-processes?includeInactive=${includeInactive}`, { signal }),
  })

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <div>
          <Typography.Title level={4} style={{ margin: 0 }}>공정 경로</Typography.Title>
          <Typography.Text type="secondary">열처리 공정 = 단위공정 순서 (* = 주공정, 이후 공정은 주 LOT 으로 투입)</Typography.Text>
        </div>
        <Space>
          <Space size={4}><Switch size="small" checked={includeInactive} onChange={setIncludeInactive} />사용 중지 포함</Space>
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>공정 추가</Button>}
        </Space>
      </Space>
      <Table<HeatProcess> rowKey="heatProcessId" size="middle" loading={list.isFetching} dataSource={list.data ?? []} pagination={false}
        onRow={(h) => ({ onClick: () => setEditing(h.heatProcessId), style: { cursor: 'pointer', opacity: h.isActive ? 1 : 0.5 } })}
        columns={[
          { title: '코드', dataIndex: 'heatProcessCode', width: 130 },
          { title: '공정', dataIndex: 'heatProcessName', width: 180 },
          { title: '경로', dataIndex: 'routeSummary' },
          { title: '버전', dataIndex: 'currentVersionNo', width: 70, render: (v: number | null) => v && `v${v}` },
        ]} />
      {editing !== null && (
        <HeatProcessWindow id={editing === 'new' ? null : editing} canEdit={editing === 'new' ? canCreate : canUpdate}
          onClose={() => setEditing(null)}
          onSaved={(id) => { setEditing(id); void queryClient.invalidateQueries({ queryKey: key }) }} />
      )}
    </>
  )
}

function HeatProcessWindow({ id, canEdit, onClose, onSaved }: { id: number | null; canEdit: boolean; onClose: () => void; onSaved: (id: number) => void }) {
  const { message } = App.useApp()
  const [form] = Form.useForm<{ heatProcessCode?: string; heatProcessName: string; description?: string; isActive: boolean; remark?: string }>()
  const units = useOptions('/api/master/unit_process/options')
  const detail = useQuery({
    queryKey: [...queryKeys.master, 'heat_process', 'detail', id],
    queryFn: ({ signal }) => api<{ header: HeatProcess; versions: Version[] }>(`/api/heat-processes/${id}`, { signal }),
    enabled: id !== null,
  })
  const current = detail.data?.versions.find((v) => v.isCurrent)
  const [ops, setOps] = useState<Operation[] | null>(null)
  const route = ops ?? current?.operations ?? []
  const [saving, setSaving] = useState(false)

  const move = (i: number, d: number) => {
    const next = [...route]
    ;[next[i], next[i + d]] = [next[i + d], next[i]]
    setOps(next)
  }
  const update = (i: number, patch: Partial<Operation>) =>
    setOps(route.map((o, j) => (j === i ? { ...o, ...patch } : patch.isMainProcess ? { ...o, isMainProcess: false } : o)))

  const save = async () => {
    const v = await form.validateFields()
    setSaving(true)
    try {
      if (id === null) {
        const r = await api<{ heatProcessId: number }>('/api/heat-processes', { method: 'POST', body: { ...v, operations: route } })
        message.success('공정을 등록했습니다.')
        onSaved(r.heatProcessId)
      } else {
        await api(`/api/heat-processes/${id}`, { method: 'PUT', body: v })
        if (ops) {
          const r = await api<{ versionNo: number; newVersion: boolean }>(`/api/heat-processes/${id}/route`, { method: 'PUT', body: { operations: route, remark: v.remark } })
          message.success(r.newVersion ? `거래에서 쓰는 경로라 새 버전 v${r.versionNo} 을 만들었습니다.` : `현재 버전 v${r.versionNo} 을 수정했습니다.`)
          setOps(null)
        } else {
          message.success('저장했습니다.')
        }
        await detail.refetch()
        onSaved(id)
      }
    } catch (e) {
      const errors = fieldErrors<{ heatProcessCode: string; heatProcessName: string }>(e)
      if (errors.length > 0) form.setFields(errors)
      else message.error(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  if (id !== null && !detail.data) return null
  const h = detail.data?.header
  return (
    <EditorWindow onClose={onClose} size="large" title={h ? `${h.heatProcessName} (${h.heatProcessCode})` : '공정 추가'}
      extra={canEdit && <Button type="primary" loading={saving} onClick={() => void save()}>저장</Button>}>
      <Form form={form} layout="vertical" disabled={!canEdit}
        initialValues={h ? { heatProcessName: h.heatProcessName, description: h.description, isActive: h.isActive } : { isActive: true }}>
        {!h && <Form.Item name="heatProcessCode" label="공정 코드" rules={[{ required: true, max: 50 }]}><Input /></Form.Item>}
        <Form.Item name="heatProcessName" label="공정명" rules={[{ required: true, max: 100 }]}><Input placeholder="예: 침탄 소입소려" /></Form.Item>
        <Form.Item name="description" label="설명"><Input maxLength={255} /></Form.Item>
        {h && <Form.Item name="isActive" label="사용" valuePropName="checked"><Switch /></Form.Item>}

        <Typography.Text strong>경로 {current && <Tag>현재 v{current.versionNo}{current.usageCount > 0 ? ` · 거래 ${current.usageCount}건 사용 중 → 수정 시 새 버전` : ''}</Tag>}</Typography.Text>
        <Table<Operation> style={{ marginTop: 8 }} size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={route}
          columns={[
            { title: '순서', width: 60, render: (_: unknown, __, i) => i + 1 },
            {
              title: '단위공정', render: (_: unknown, o, i) => (
                <Select value={o.unitProcessId} style={{ width: 200 }} disabled={!canEdit} showSearch optionFilterProp="label" options={units.options}
                  onChange={(v) => update(i, { unitProcessId: v })} />
              ),
            },
            { title: '주공정', width: 70, align: 'center', render: (_: unknown, o, i) => <Radio checked={o.isMainProcess} disabled={!canEdit} onChange={() => update(i, { isMainProcess: true })} /> },
            { title: '필수', width: 60, align: 'center', render: (_: unknown, o, i) => <Checkbox checked={o.isRequired} disabled={!canEdit} onChange={(e) => update(i, { isRequired: e.target.checked })} /> },
            ...(canEdit ? [{
              title: '', width: 120, render: (_: unknown, __: Operation, i: number) => (
                <Space size={2}>
                  <Button size="small" icon={<ArrowUpOutlined />} disabled={i === 0} onClick={() => move(i, -1)} />
                  <Button size="small" icon={<ArrowDownOutlined />} disabled={i === route.length - 1} onClick={() => move(i, 1)} />
                  <Button size="small" danger icon={<DeleteOutlined />} onClick={() => setOps(route.filter((_, j) => j !== i))} />
                </Space>
              ),
            }] : []),
          ]} />
        {canEdit && (
          <Space style={{ marginTop: 8 }}>
            <Select placeholder="단위공정 추가" style={{ width: 200 }} value={null} showSearch optionFilterProp="label" options={units.options}
              onChange={(v: number) => setOps([...route, { unitProcessId: v, isMainProcess: false, isRequired: true }])} />
            {h && ops && <Form.Item name="remark" noStyle><Input placeholder="변경 내용 (버전 비고)" style={{ width: 260 }} /></Form.Item>}
          </Space>
        )}
      </Form>

      {detail.data && (
        <>
          <Typography.Title level={5} style={{ marginTop: 24 }}>버전 이력</Typography.Title>
          <Table<Version> size="small" rowKey="heatProcessVersionId" pagination={false} dataSource={detail.data.versions} columns={[
            { title: '버전', dataIndex: 'versionNo', width: 80, render: (v: number, r) => <Space>v{v}{r.isCurrent && <Tag color="blue">현재</Tag>}</Space> },
            { title: '경로', render: (_: unknown, r) => r.operations.map((o) => `${o.unitProcessName}${o.isMainProcess ? '*' : ''}`).join(' > ') },
            { title: '적용', width: 190, render: (_: unknown, r) => `${dayjs(r.effectiveFrom).format('YYYY-MM-DD')} ~ ${r.effectiveTo ? dayjs(r.effectiveTo).format('YYYY-MM-DD') : ''}` },
            { title: '사용', dataIndex: 'usageCount', width: 60, align: 'right' },
            { title: '비고', dataIndex: 'remark' },
          ]} />
        </>
      )}
    </EditorWindow>
  )
}
