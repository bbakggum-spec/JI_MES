import { LockOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Checkbox, Col, Form, Input, Menu, Modal, Row, Space, Spin, Switch, Table, Tag, Typography } from 'antd'
import { useMemo, useState } from 'react'
import { api, fieldErrors } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { queryKeys } from '../../queryKeys'

const MENU_KEY = 'system.role'
type Action = 'read' | 'create' | 'update' | 'delete'
const ACTIONS: { key: Action; label: string }[] = [
  { key: 'read', label: '조회' }, { key: 'create', label: '등록' }, { key: 'update', label: '수정' }, { key: 'delete', label: '삭제' },
]

interface Role {
  roleId: number
  roleCode: string
  roleName: string
  description: string | null
  isActive: boolean
  userCount: number
  isLocked: boolean
}

interface RoleMenu {
  menuId: number
  menuKey: string
  menuName: string
  parentMenuId: number | null
  read: boolean
  create: boolean
  update: boolean
  delete: boolean
}

/** 역할·메뉴 권한 — 역할 × 메뉴 × (조회/등록/수정/삭제). 관리자 역할은 DDL 에서 전체 권한 고정 */
export default function RolesPage() {
  const canCreate = useCan(MENU_KEY, 'create')
  const canUpdate = useCan(MENU_KEY, 'update')
  const queryClient = useQueryClient()
  const [selected, setSelected] = useState<number | null>(null)
  const [creating, setCreating] = useState(false)

  const key = [...queryKeys.admin, 'roles']
  const roles = useQuery({ queryKey: key, queryFn: ({ signal }) => api<Role[]>('/api/roles', { signal }) })
  const role = roles.data?.find((r) => r.roleId === selected) ?? roles.data?.[0]

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }}>
        <Typography.Title level={4} style={{ margin: 0 }}>역할·권한</Typography.Title>
        {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>역할 추가</Button>}
      </Space>
      <Row gutter={16}>
        <Col xs={24} md={7} lg={6}>
          <Card size="small" title="역할" styles={{ body: { padding: 0 } }}>
            <Spin spinning={roles.isPending}>
              <Menu mode="inline" style={{ borderInlineEnd: 0 }} selectedKeys={role ? [String(role.roleId)] : []}
                onClick={(e) => setSelected(Number(e.key))}
                items={(roles.data ?? []).map((r) => ({
                  key: String(r.roleId),
                  label: (
                    <Space size={4} style={{ opacity: r.isActive ? 1 : 0.5 }}>
                      {r.isLocked && <LockOutlined />}{r.roleName}
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>{r.userCount}명</Typography.Text>
                    </Space>
                  ),
                }))} />
            </Spin>
          </Card>
        </Col>
        <Col xs={24} md={17} lg={18}>
          {role && <RoleDetail key={role.roleId} role={role} canUpdate={canUpdate} onChanged={() => void queryClient.invalidateQueries({ queryKey: key })} />}
        </Col>
      </Row>
      {creating && <CreateRoleModal onClose={() => setCreating(false)} onDone={(id) => {
        setCreating(false)
        setSelected(id)
        void queryClient.invalidateQueries({ queryKey: key })
      }} />}
    </>
  )
}

function RoleDetail({ role, canUpdate, onChanged }: { role: Role; canUpdate: boolean; onChanged: () => void }) {
  const { message } = App.useApp()
  const [form] = Form.useForm<{ roleName: string; description?: string; isActive: boolean }>()
  const menus = useQuery({
    queryKey: [...queryKeys.admin, 'roles', role.roleId, 'menus'],
    queryFn: ({ signal }) => api<RoleMenu[]>(`/api/roles/${role.roleId}/menus`, { signal }),
  })
  const [edits, setEdits] = useState<Record<number, RoleMenu>>({})
  const [saving, setSaving] = useState(false)
  const editable = canUpdate && !role.isLocked
  const rows = useMemo(() => (menus.data ?? []).map((m) => edits[m.menuId] ?? m), [menus.data, edits])
  const dirty = Object.keys(edits).length > 0

  const toggle = (m: RoleMenu, action: Action, value: boolean) => {
    const next = { ...m, [action]: value }
    if (action !== 'read' && value) next.read = true                               // 쓰기 권한이면 조회도
    if (action === 'read' && !value) Object.assign(next, { create: false, update: false, delete: false })
    setEdits((e) => ({ ...e, [m.menuId]: next }))
  }

  const saveInfo = async () => {
    const v = await form.validateFields()
    try {
      await api(`/api/roles/${role.roleId}`, { method: 'PUT', body: v })
      message.success('저장했습니다.')
      onChanged()
    } catch (e) {
      message.error(e instanceof Error ? e.message : String(e))
    }
  }

  const saveMenus = async () => {
    setSaving(true)
    try {
      await api(`/api/roles/${role.roleId}/menus`, { method: 'PUT', body: { grants: rows } })
      message.success('메뉴 권한을 저장했습니다. 해당 사용자에게 바로 적용됩니다.')
      setEdits({})
      await menus.refetch()
    } catch (e) {
      message.error(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Space orientation="vertical" style={{ width: '100%' }}>
      <Card size="small" title={<Space>{role.roleName}<Tag>{role.roleCode}</Tag></Space>}
        extra={editable && <Button onClick={() => void saveInfo()}>저장</Button>}>
        {role.isLocked && <Alert type="info" showIcon style={{ marginBottom: 12 }} title="관리자 역할은 모든 메뉴 전체 권한으로 고정입니다 (DDL). 사용 중지할 수 없습니다." />}
        <Form form={form} layout="inline" disabled={!canUpdate} initialValues={{ roleName: role.roleName, description: role.description, isActive: role.isActive }}>
          <Form.Item name="roleName" label="이름" rules={[{ required: true, max: 100 }]}><Input style={{ width: 180 }} /></Form.Item>
          <Form.Item name="description" label="설명"><Input style={{ width: 260 }} maxLength={255} /></Form.Item>
          <Form.Item name="isActive" label="사용" valuePropName="checked"><Switch disabled={role.isLocked || !canUpdate} /></Form.Item>
        </Form>
      </Card>
      <Card size="small" title="메뉴 권한"
        extra={editable && <Space>{dirty && <Button onClick={() => setEdits({})}>되돌리기</Button>}<Button type="primary" disabled={!dirty} loading={saving} onClick={() => void saveMenus()}>권한 저장</Button></Space>}>
        <Table<RoleMenu> size="small" rowKey="menuId" pagination={false} loading={menus.isFetching} dataSource={rows}
          columns={[
            {
              title: '메뉴', dataIndex: 'menuName',
              render: (v: string, m) => m.parentMenuId === null
                ? <Typography.Text strong>{v}</Typography.Text>
                : <span style={{ paddingLeft: 20 }}>{v} <Typography.Text type="secondary" style={{ fontSize: 12 }}>{m.menuKey}</Typography.Text></span>,
            },
            ...ACTIONS.map((a) => ({
              title: a.label, width: 70, align: 'center' as const,
              render: (_: unknown, m: RoleMenu) => (
                <Checkbox checked={m[a.key]} disabled={!editable} onChange={(e) => toggle(m, a.key, e.target.checked)} />
              ),
            })),
          ]} />
      </Card>
    </Space>
  )
}

function CreateRoleModal({ onClose, onDone }: { onClose: () => void; onDone: (roleId: number) => void }) {
  const [form] = Form.useForm<{ roleCode: string; roleName: string; description?: string }>()
  const { message } = App.useApp()
  const save = async () => {
    const v = await form.validateFields()
    try {
      const r = await api<{ roleId: number }>('/api/roles', { method: 'POST', body: v })
      message.success('역할을 만들었습니다. 메뉴 권한을 지정하세요.')
      onDone(r.roleId)
    } catch (e) {
      const errors = fieldErrors<{ roleCode: string; roleName: string }>(e)
      if (errors.length > 0) form.setFields(errors)
      else message.error(e instanceof Error ? e.message : String(e))
    }
  }
  return (
    <Modal open title="역할 추가" onCancel={onClose} onOk={() => void save()} okText="만들기" destroyOnHidden>
      <Form form={form} layout="vertical">
        <Form.Item name="roleCode" label="역할 코드" extra="영문·숫자·_ (예: PRODUCTION, SALES) — 만든 뒤 바꿀 수 없음" rules={[{ required: true, max: 50 }]}><Input /></Form.Item>
        <Form.Item name="roleName" label="이름" rules={[{ required: true, max: 100 }]}><Input placeholder="예: 생산부" /></Form.Item>
        <Form.Item name="description" label="설명"><Input maxLength={255} /></Form.Item>
      </Form>
    </Modal>
  )
}
