import { ArrowDownOutlined, ArrowUpOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Col, Drawer, Form, Input, Row, Select, Space, Switch, Table, Tag, Tooltip, Typography } from 'antd'
import { useState } from 'react'
import { ApiError, api, fieldErrors } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'

export interface StepTemplate {
  stepTemplateId: number
  stepTemplateCode: string
  stepTemplateName: string
  unitProcessId: number
  unitProcessName: string | null
  equipmentTypeId: number | null
  equipmentTypeName: string | null
  equipmentId: number | null
  equipmentName: string | null
  isActive: boolean
  stepCount: number
  conditionCount: number
  usageCount: number
}

export interface StepTemplateDetail {
  header: StepTemplate
  steps: { stepTemplateItemId: number; sequenceNo: number; stepName: string; usageCount: number }[]
  conditions: { conditionItemId: number; conditionItemName: string; unitCode: string | null; valueType: string; sequenceNo: number }[]
}

interface StepRow {
  stepTemplateItemId: number | null
  stepName: string
  usageCount: number
}

/** 단계 템플릿 (구 F_StandardTemplateAdd) — 설비(유형)·단위공정의 단계(열)와 관리항목(행) */
export default function StepTemplatesPage({ menuKey }: PageProps) {
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const [includeInactive, setIncludeInactive] = useState(false)
  const key = [...queryKeys.master, 'step_template']
  const list = useQuery({
    queryKey: [...key, includeInactive],
    queryFn: ({ signal }) => api<StepTemplate[]>(`/api/step-templates?includeInactive=${includeInactive}`, { signal }),
  })

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <div>
          <Typography.Title level={4} style={{ margin: 0 }}>단계 템플릿</Typography.Title>
          <Typography.Text type="secondary">작업표준·작업조건 행렬의 열(단계)과 행(관리항목)</Typography.Text>
        </div>
        <Space>
          <Space size={4}><Switch size="small" checked={includeInactive} onChange={setIncludeInactive} />사용 중지 포함</Space>
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>템플릿 추가</Button>}
        </Space>
      </Space>
      <Table<StepTemplate> rowKey="stepTemplateId" size="middle" loading={list.isFetching} dataSource={list.data ?? []} pagination={false}
        onRow={(t) => ({ onClick: () => setEditing(t.stepTemplateId), style: { cursor: 'pointer', opacity: t.isActive ? 1 : 0.5 } })}
        columns={[
          { title: '코드', dataIndex: 'stepTemplateCode', width: 140 },
          { title: '이름', dataIndex: 'stepTemplateName' },
          { title: '단위공정', dataIndex: 'unitProcessName', width: 110 },
          { title: '설비', width: 160, render: (_: unknown, t) => t.equipmentName ?? (t.equipmentTypeName ? `${t.equipmentTypeName} 공통` : '공통') },
          { title: '단계', dataIndex: 'stepCount', width: 60, align: 'right' },
          { title: '항목', dataIndex: 'conditionCount', width: 60, align: 'right' },
          { title: '표준 사용', dataIndex: 'usageCount', width: 80, align: 'right' },
        ]} />
      {editing !== null && (
        <TemplateDrawer id={editing === 'new' ? null : editing} canEdit={editing === 'new' ? canCreate : canUpdate} onClose={() => setEditing(null)}
          onSaved={(id) => { setEditing(id); void queryClient.invalidateQueries({ queryKey: key }) }} />
      )}
    </>
  )
}

type HeaderForm = { stepTemplateCode: string; stepTemplateName: string; unitProcessId: number; equipmentTypeId?: number | null; equipmentId?: number | null; isActive: boolean; conditionItemIds: number[] }

function TemplateDrawer({ id, canEdit, onClose, onSaved }: { id: number | null; canEdit: boolean; onClose: () => void; onSaved: (id: number) => void }) {
  const { message } = App.useApp()
  const [form] = Form.useForm<HeaderForm>()
  const units = useOptions('/api/master/unit_process/options')
  const types = useOptions('/api/master/equipment_type/options')
  const equipment = useOptions('/api/master/equipment/options')
  const conditionItems = useOptions('/api/master/condition_item/options')
  const detail = useQuery({
    queryKey: [...queryKeys.master, 'step_template', 'detail', id],
    queryFn: ({ signal }) => api<StepTemplateDetail>(`/api/step-templates/${id}`, { signal }),
    enabled: id !== null,
  })
  const [steps, setSteps] = useState<StepRow[] | null>(null)
  const rows: StepRow[] = steps ?? detail.data?.steps.map((s) => ({ stepTemplateItemId: s.stepTemplateItemId, stepName: s.stepName, usageCount: s.usageCount })) ?? []
  const [saving, setSaving] = useState(false)

  const move = (i: number, d: number) => {
    const next = [...rows]
    ;[next[i], next[i + d]] = [next[i + d], next[i]]
    setSteps(next)
  }

  const save = async () => {
    const v = await form.validateFields()
    setSaving(true)
    try {
      const body = { ...v, steps: rows.map((s) => ({ stepTemplateItemId: s.stepTemplateItemId, stepName: s.stepName })) }
      if (id === null) {
        const r = await api<{ stepTemplateId: number }>('/api/step-templates', { method: 'POST', body })
        message.success('템플릿을 등록했습니다.')
        onSaved(r.stepTemplateId)
      } else {
        await api(`/api/step-templates/${id}`, { method: 'PUT', body })
        message.success('저장했습니다.')
        setSteps(null)
        await detail.refetch()
        onSaved(id)
      }
    } catch (e) {
      const errors = fieldErrors<HeaderForm>(e)
      if (errors.length > 0) form.setFields(errors)
      else message.error(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  if (id !== null && !detail.data) return null
  const d = detail.data
  return (
    <Drawer open onClose={onClose} size="large" title={d ? `${d.header.stepTemplateName} (${d.header.stepTemplateCode})` : '템플릿 추가'}
      extra={canEdit && <Button type="primary" loading={saving} onClick={() => void save()}>저장</Button>}>
      <Form form={form} layout="vertical" disabled={!canEdit} initialValues={d ? {
        ...d.header, conditionItemIds: d.conditions.map((c) => c.conditionItemId),
      } : { isActive: true, conditionItemIds: [] }}>
        <Row gutter={12}>
          <Col span={8}><Form.Item name="stepTemplateCode" label="코드" rules={[{ required: true, max: 50 }]}><Input /></Form.Item></Col>
          <Col span={16}><Form.Item name="stepTemplateName" label="이름" rules={[{ required: true, max: 100 }]}><Input placeholder="예: 가스로 침탄" /></Form.Item></Col>
          <Col span={8}>
            <Form.Item name="unitProcessId" label="단위공정" rules={[{ required: true }]}
              extra={d && d.header.usageCount > 0 ? '작업표준에서 쓰는 템플릿은 단위공정을 바꿀 수 없습니다' : undefined}>
              <Select options={units.options} showSearch optionFilterProp="label" disabled={!!d && d.header.usageCount > 0} />
            </Form.Item>
          </Col>
          <Col span={8}><Form.Item name="equipmentTypeId" label="설비 유형"><Select allowClear options={types.options} /></Form.Item></Col>
          <Col span={8}><Form.Item name="equipmentId" label="설비 (비우면 유형 공통)"><Select allowClear showSearch optionFilterProp="label" options={equipment.options} /></Form.Item></Col>
        </Row>
        {d && <Form.Item name="isActive" label="사용" valuePropName="checked"><Switch /></Form.Item>}
        <Form.Item name="conditionItemIds" label="관리항목 (행, 선택 순서 = 표시 순서)" rules={[{ required: true, message: '관리항목을 1개 이상' }]}>
          <Select mode="multiple" options={conditionItems.options} optionFilterProp="label" />
        </Form.Item>
      </Form>

      <Typography.Text strong>단계 (열)</Typography.Text>
      <Table<StepRow> style={{ marginTop: 8 }} size="small" pagination={false} rowKey={(_, i) => String(i)} dataSource={rows}
        columns={[
          { title: '순서', width: 60, render: (_: unknown, __, i) => i + 1 },
          {
            title: '단계명', render: (_: unknown, s, i) => (
              <Input value={s.stepName} disabled={!canEdit} maxLength={100}
                onChange={(e) => setSteps(rows.map((r, j) => (j === i ? { ...r, stepName: e.target.value } : r)))} />
            ),
          },
          { title: '사용', dataIndex: 'usageCount', width: 70, align: 'right', render: (n: number) => (n > 0 ? <Tag>{n}</Tag> : null) },
          ...(canEdit ? [{
            title: '', width: 120, render: (_: unknown, s: StepRow, i: number) => (
              <Space size={2}>
                <Button size="small" icon={<ArrowUpOutlined />} disabled={i === 0} onClick={() => move(i, -1)} />
                <Button size="small" icon={<ArrowDownOutlined />} disabled={i === rows.length - 1} onClick={() => move(i, 1)} />
                <Tooltip title={s.usageCount > 0 ? '작업표준·작업 조건에서 쓰는 단계는 삭제할 수 없습니다 (이름만 변경)' : undefined}>
                  <Button size="small" danger icon={<DeleteOutlined />} disabled={s.usageCount > 0} onClick={() => setSteps(rows.filter((_, j) => j !== i))} />
                </Tooltip>
              </Space>
            ),
          }] : []),
        ]} />
      {canEdit && (
        <Button style={{ marginTop: 8 }} type="dashed" icon={<PlusOutlined />}
          onClick={() => setSteps([...rows, { stepTemplateItemId: null, stepName: '', usageCount: 0 }])}>단계 추가</Button>
      )}
    </Drawer>
  )
}
