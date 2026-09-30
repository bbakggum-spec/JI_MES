import { PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Col, Form, Input, Row, Select, Space, Switch, Table, Typography } from 'antd'
import EditorWindow from '../../components/EditorWindow'
import { useMemo, useState } from 'react'
import ConditionGrid from './ConditionGrid'
import { layoutOf, newStep, type GridLayout } from './conditionGridModel'
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
  steps: { stepTemplateItemId: number; sequenceNo: number; stepName: string }[]
  conditions: { conditionItemId: number; conditionItemName: string; unitCode: string | null; valueType: string; sequenceNo: number }[]
}

/** 단계 템플릿 (구 F_StandardTemplateAdd) — 설비(유형)·단위공정별 입력표 초기값: 스텝(열)과 관리항목(행) */
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
          <Typography.Text type="secondary">작업표준 입력표의 초기값 — 스텝(열)과 관리항목(행). 작업표준에서 불러와 자유롭게 고칠 수 있습니다</Typography.Text>
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
          { title: '스텝', dataIndex: 'stepCount', width: 60, align: 'right' },
          { title: '항목', dataIndex: 'conditionCount', width: 60, align: 'right' },
          { title: '표준 사용', dataIndex: 'usageCount', width: 80, align: 'right' },
        ]} />
      {editing !== null && (
        <TemplateWindow id={editing === 'new' ? null : editing} canEdit={editing === 'new' ? canCreate : canUpdate} onClose={() => setEditing(null)}
          onSaved={(id) => { setEditing(id); void queryClient.invalidateQueries({ queryKey: key }) }} />
      )}
    </>
  )
}

type HeaderForm = { stepTemplateCode: string; stepTemplateName: string; unitProcessId: number; equipmentTypeId?: number | null; equipmentId?: number | null; isActive: boolean }

function TemplateWindow({ id, canEdit, onClose, onSaved }: { id: number | null; canEdit: boolean; onClose: () => void; onSaved: (id: number) => void }) {
  const { message } = App.useApp()
  const [form] = Form.useForm<HeaderForm>()
  const units = useOptions('/api/master/unit_process/options')
  const types = useOptions('/api/master/equipment_type/options')
  const equipment = useOptions('/api/master/equipment/options')
  const detail = useQuery({
    queryKey: [...queryKeys.master, 'step_template', 'detail', id],
    queryFn: ({ signal }) => api<StepTemplateDetail>(`/api/step-templates/${id}`, { signal }),
    enabled: id !== null,
  })
  const base = useMemo<GridLayout>(() => (detail.data ? layoutOf(detail.data) : { steps: [newStep()], items: [] }), [detail.data])
  const [edited, setEdited] = useState<GridLayout | null>(null)
  const layout = edited ?? base
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const save = async () => {
    const v = await form.validateFields()
    setError(null)
    setSaving(true)
    try {
      const body = { ...v, steps: layout.steps.map((s) => s.name.trim()), conditionItemIds: layout.items.map((c) => c.conditionItemId) }
      if (id === null) {
        const r = await api<{ stepTemplateId: number }>('/api/step-templates', { method: 'POST', body })
        message.success('템플릿을 등록했습니다.')
        onSaved(r.stepTemplateId)
      } else {
        await api(`/api/step-templates/${id}`, { method: 'PUT', body })
        message.success('저장했습니다.')
        await detail.refetch()
        setEdited(null)
        onSaved(id)
      }
    } catch (e) {
      const errors = fieldErrors<HeaderForm>(e).filter((f) => f.name in form.getFieldsValue())
      if (errors.length > 0) form.setFields(errors)
      else setError(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  if (id !== null && !detail.data) return null
  const d = detail.data
  return (
    <EditorWindow onClose={onClose} size={1100} title={d ? `${d.header.stepTemplateName} (${d.header.stepTemplateCode})` : '템플릿 추가'}
      extra={canEdit && <Button type="primary" loading={saving} onClick={() => void save()}>저장</Button>}>
      <Form form={form} layout="vertical" disabled={!canEdit} initialValues={d ? d.header : { isActive: true }}>
        <Row gutter={12}>
          <Col span={6}><Form.Item name="stepTemplateCode" label="코드" rules={[{ required: true, max: 50 }]}><Input /></Form.Item></Col>
          <Col span={10}><Form.Item name="stepTemplateName" label="이름" rules={[{ required: true, max: 100 }]}><Input placeholder="예: 가스로 침탄" /></Form.Item></Col>
          {d && <Col span={4}><Form.Item name="isActive" label="사용" valuePropName="checked"><Switch /></Form.Item></Col>}
        </Row>
        <Row gutter={12}>
          <Col span={8}>
            <Form.Item name="unitProcessId" label="단위공정" rules={[{ required: true }]}>
              <Select options={units.options} showSearch optionFilterProp="label" />
            </Form.Item>
          </Col>
          <Col span={8}><Form.Item name="equipmentTypeId" label="설비 유형"><Select allowClear options={types.options} /></Form.Item></Col>
          <Col span={8}><Form.Item name="equipmentId" label="설비 (비우면 유형 공통)"><Select allowClear showSearch optionFilterProp="label" options={equipment.options} /></Form.Item></Col>
        </Row>
      </Form>

      <Typography.Text strong>입력표 구성</Typography.Text>
      <Typography.Paragraph type="secondary" style={{ margin: '2px 0 8px' }}>
        맨 위 칸에 스텝(열) 이름을 적고 [+ 스텝]으로 늘립니다. 관리항목(행)은 표 아래에서 추가합니다. 작업표준에서 이 템플릿을 불러오면 이 표가 그대로 채워집니다.
      </Typography.Paragraph>
      <ConditionGrid layout={layout} onLayoutChange={setEdited} editable={canEdit} />
      {error && <Alert style={{ marginTop: 12 }} type="error" showIcon title={error} />}
    </EditorWindow>
  )
}
