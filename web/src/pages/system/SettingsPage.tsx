import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Form, Input, InputNumber, Modal, Popconfirm, Space, Switch, Table, Tabs, Tag, TimePicker, Tooltip, Typography } from 'antd'
import dayjs from 'dayjs'
import { useMemo, useState } from 'react'
import { api, fieldErrors } from '../../api/client'
import type { Setting } from '../../api/types'
import { useCan } from '../../auth/useAuth'
import { queryKeys } from '../../queryKeys'

const MENU_KEY = 'system.setting'

export default function SettingsPage() {
  const canUpdate = useCan(MENU_KEY, 'update')
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<Setting | null>(null)

  const { data, isPending } = useQuery({
    queryKey: queryKeys.settings,
    queryFn: ({ signal }) => api<Setting[]>('/api/settings', { signal }),
  })

  const reset = useMutation({
    mutationFn: (key: string) => api<Setting>(`/api/settings/${encodeURIComponent(key)}/reset`, { method: 'POST', body: {} }),
    onSuccess: (s) => {
      message.success(`'${s.settingName}' 을 기본값으로 되돌렸습니다.`)
      void queryClient.invalidateQueries({ queryKey: queryKeys.settings })
    },
    onError: (e) => message.error(e.message),
  })

  const categories = useMemo(() => [...new Set((data ?? []).map((s) => s.category))], [data])

  const columns = [
    {
      title: '항목', dataIndex: 'settingName', width: 260,
      render: (_: string, s: Setting) => (
        <>
          <div>{s.settingName}</div>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>{s.settingKey}</Typography.Text>
        </>
      ),
    },
    {
      title: '현재 값', dataIndex: 'effectiveValue', width: 260,
      render: (_: string, s: Setting) => (
        <Space wrap>
          <Typography.Text code>{s.effectiveValue}</Typography.Text>
          {s.unitLabel && <span>{s.unitLabel}</span>}
          {s.settingValue === null && <Tag>기본값</Tag>}
          {s.isFallback && <Tooltip title={`저장값 '${s.settingValue}' 이 올바르지 않아 기본값으로 동작 중`}><Tag color="error">저장값 오류</Tag></Tooltip>}
          {s.requiresRestart && <Tag color="warning">재시작 필요</Tag>}
        </Space>
      ),
    },
    { title: '기본값', dataIndex: 'defaultValue', width: 160, render: (v: string) => <Typography.Text type="secondary">{v}</Typography.Text> },
    { title: '범위', width: 120, render: (_: unknown, s: Setting) => rangeText(s) },
    { title: '설명', dataIndex: 'description', ellipsis: { showTitle: true }, width: 260 },
    ...(canUpdate ? [{
      title: '', width: 150, fixed: 'right' as const, render: (_: unknown, s: Setting) => s.isEditable && (
        <Space>
          <Button size="small" onClick={() => setEditing(s)}>수정</Button>
          <Popconfirm title="기본값으로 되돌릴까요?" disabled={s.settingValue === null} onConfirm={() => reset.mutate(s.settingKey)}>
            <Button size="small" disabled={s.settingValue === null}>초기화</Button>
          </Popconfirm>
        </Space>
      ),
    }] : []),
  ]

  return (
    <>
      <Typography.Title level={4} style={{ marginTop: 0 }}>관리자 설정</Typography.Title>
      <Tabs items={categories.map((c) => ({
        key: c,
        label: c,
        children: <Table<Setting> rowKey="settingKey" size="middle" pagination={false} loading={isPending} scroll={{ x: 1150 }}
          columns={columns} dataSource={(data ?? []).filter((s) => s.category === c)} />,
      }))} />
      {editing && <EditSettingModal setting={editing} onClose={() => setEditing(null)} />}
    </>
  )
}

function rangeText(s: Setting) {
  if (s.minValue === null && s.maxValue === null) return '-'
  return `${s.minValue ?? ''} ~ ${s.maxValue ?? ''}`
}

interface EditValues {
  value: unknown
  reason?: string
}

function EditSettingModal({ setting, onClose }: { setting: Setting; onClose: () => void }) {
  const [form] = Form.useForm<EditValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()

  const save = useMutation({
    mutationFn: (v: EditValues) => api<Setting>(`/api/settings/${encodeURIComponent(setting.settingKey)}`, {
      method: 'PUT', body: { value: toRaw(setting, v.value), reason: v.reason },
    }),
    onSuccess: (s) => {
      message.success(s.requiresRestart ? '저장했습니다. 서버를 재시작해야 적용됩니다.' : '저장했습니다.')
      void queryClient.invalidateQueries({ queryKey: queryKeys.settings })
      onClose()
    },
    onError: (e) => {
      const errors = fieldErrors<EditValues>(e)
      if (errors.length > 0) form.setFields(errors)
      else message.error(e.message)
    },
  })

  return (
    <Modal title={setting.settingName} open onCancel={onClose} okText="저장" confirmLoading={save.isPending}
      onOk={() => void form.validateFields().then((v) => save.mutate(v))} destroyOnHidden>
      <Typography.Paragraph type="secondary">{setting.description}</Typography.Paragraph>
      <Form form={form} layout="vertical" initialValues={{ value: fromRaw(setting) }}>
        <Form.Item name="value" label={`값${setting.unitLabel ? ` (${setting.unitLabel})` : ''}`}
          valuePropName={setting.valueType === 'BOOL' ? 'checked' : 'value'}
          extra={`기본값: ${setting.defaultValue}${setting.minValue !== null || setting.maxValue !== null ? ` · 범위: ${rangeText(setting)}` : ''}`}>
          {valueInput(setting)}
        </Form.Item>
        <Form.Item name="reason" label="변경 사유">
          <Input maxLength={255} placeholder="변경 이력에 남습니다" />
        </Form.Item>
      </Form>
    </Modal>
  )
}

function valueInput(s: Setting) {
  switch (s.valueType) {
    case 'INT':
      return <InputNumber style={{ width: '100%' }} precision={0} min={s.minValue ?? undefined} max={s.maxValue ?? undefined} />
    case 'DECIMAL':
      return <InputNumber style={{ width: '100%' }} min={s.minValue ?? undefined} max={s.maxValue ?? undefined} stringMode />
    case 'BOOL':
      return <Switch />
    case 'TIME':
      return <TimePicker format="HH:mm" minuteStep={5} style={{ width: '100%' }} />
    case 'JSON':
      return <Input.TextArea rows={4} style={{ fontFamily: 'monospace' }} />
    default:
      return <Input />
  }
}

function fromRaw(s: Setting): unknown {
  const v = s.effectiveValue
  switch (s.valueType) {
    case 'INT': return Number(v)
    case 'BOOL': return v === 'true'
    case 'TIME': return dayjs(v, 'HH:mm')
    default: return v
  }
}

function toRaw(s: Setting, value: unknown): string {
  if (value === null || value === undefined) return ''
  if (s.valueType === 'TIME') return (value as dayjs.Dayjs).format('HH:mm')
  return String(value)
}
