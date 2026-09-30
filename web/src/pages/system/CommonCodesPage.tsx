import { LockOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { App, Button, Card, Col, Form, Input, InputNumber, List, Modal, Row, Switch, Table, Tag, Tooltip, Typography } from 'antd'
import { useState } from 'react'
import { api, fieldErrors } from '../../api/client'
import type { CommonCode } from '../../api/types'
import { useCan } from '../../auth/useAuth'
import { useCommonCodeGroups } from '../../hooks/useCommonCodes'
import { queryKeys } from '../../queryKeys'

const MENU_KEY = 'system.code'

export default function CommonCodesPage() {
  const canUpdate = useCan(MENU_KEY, 'update')
  const { data, isPending } = useCommonCodeGroups()
  const [selected, setSelected] = useState<string | null>(null)
  const [editing, setEditing] = useState<CommonCode | null>(null)

  const group = data?.find((g) => g.groupCode === selected) ?? data?.[0]

  return (
    <>
      <Typography.Title level={4} style={{ marginTop: 0 }}>공통코드</Typography.Title>
      <Row gutter={16}>
        <Col xs={24} md={7} lg={6}>
          <Card size="small" title="그룹" styles={{ body: { padding: 0 } }}>
            <List loading={isPending} dataSource={data ?? []} renderItem={(g) => (
              <List.Item onClick={() => setSelected(g.groupCode)} style={{
                cursor: 'pointer', paddingInline: 16,
                background: g.groupCode === group?.groupCode ? 'var(--ant-color-primary-bg)' : undefined,
              }}>
                <List.Item.Meta title={g.groupName} description={g.groupCode} />
              </List.Item>
            )} />
          </Card>
        </Col>
        <Col xs={24} md={17} lg={18}>
          <Card size="small" title={group ? `${group.groupName} (${group.groupCode})` : ''}>
            {group?.description && <Typography.Paragraph type="secondary">{group.description}</Typography.Paragraph>}
            <Table<CommonCode> rowKey="commonCodeId" size="middle" pagination={false} loading={isPending} scroll={{ x: 700 }}
              dataSource={group?.codes ?? []}
              columns={[
                {
                  title: '코드', dataIndex: 'code', width: 160,
                  render: (v: string, c) => (
                    <>
                      <Typography.Text code>{v}</Typography.Text>
                      {c.isSystem && <Tooltip title="로직에서 참조하는 코드 — 코드값·사용 여부는 바꿀 수 없고 표시명·순서·속성만 수정"><LockOutlined /></Tooltip>}
                    </>
                  ),
                },
                { title: '표시명', dataIndex: 'codeName' },
                { title: '순서', dataIndex: 'sortOrder', width: 70 },
                { title: '속성', dataIndex: 'attrJson', render: (v: string | null) => v && <Typography.Text code style={{ fontSize: 12 }}>{v}</Typography.Text> },
                { title: '사용', dataIndex: 'isActive', width: 70, render: (v: boolean) => (v ? <Tag color="green">사용</Tag> : <Tag>중지</Tag>) },
                ...(canUpdate ? [{
                  title: '', width: 80, render: (_: unknown, c: CommonCode) => <Button size="small" onClick={() => setEditing(c)}>수정</Button>,
                }] : []),
              ]} />
          </Card>
        </Col>
      </Row>
      {editing && <EditCodeModal code={editing} onClose={() => setEditing(null)} />}
    </>
  )
}

interface EditValues {
  codeName: string
  sortOrder: number
  attrJson?: string
  isActive: boolean
  remark?: string
  reason?: string
}

function EditCodeModal({ code, onClose }: { code: CommonCode; onClose: () => void }) {
  const [form] = Form.useForm<EditValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()

  const save = useMutation({
    mutationFn: (v: EditValues) => api<CommonCode>(`/api/common-codes/${code.commonCodeId}`, { method: 'PUT', body: v }),
    onSuccess: () => {
      message.success('저장했습니다.')
      void queryClient.invalidateQueries({ queryKey: queryKeys.commonCodes })
      onClose()
    },
    onError: (e) => {
      const errors = fieldErrors<EditValues>(e)
      if (errors.length > 0) form.setFields(errors)
      else message.error(e.message)
    },
  })

  return (
    <Modal title={`코드 수정 — ${code.code}`} open onCancel={onClose} okText="저장" confirmLoading={save.isPending}
      onOk={() => void form.validateFields().then((v) => save.mutate(v))} destroyOnHidden>
      <Form form={form} layout="vertical" initialValues={{
        codeName: code.codeName, sortOrder: code.sortOrder, attrJson: code.attrJson ?? '',
        isActive: code.isActive, remark: code.remark ?? '',
      }}>
        <Form.Item name="codeName" label="표시명" rules={[{ required: true, max: 100 }]}><Input /></Form.Item>
        <Form.Item name="sortOrder" label="순서" rules={[{ required: true }]}><InputNumber precision={0} /></Form.Item>
        <Form.Item name="attrJson" label="속성 (JSON)"><Input.TextArea rows={3} style={{ fontFamily: 'monospace' }} /></Form.Item>
        <Form.Item name="isActive" label="사용" valuePropName="checked"
          extra={code.isSystem ? '시스템 코드는 사용 여부를 바꿀 수 없습니다.' : undefined}>
          <Switch disabled={code.isSystem} />
        </Form.Item>
        <Form.Item name="remark" label="비고"><Input maxLength={255} /></Form.Item>
        <Form.Item name="reason" label="변경 사유"><Input maxLength={255} placeholder="변경 이력에 남습니다" /></Form.Item>
      </Form>
    </Modal>
  )
}
