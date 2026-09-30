import { KeyOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Form, Input, Modal, Select, Space, Switch, Table, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { api, fieldErrors } from '../../api/client'
import { useAuth, useCan } from '../../auth/useAuth'
import { queryKeys } from '../../queryKeys'
import type { MasterOption } from '../master/masterTypes'

const MENU_KEY = 'system.user'

interface User {
  appUserId: number
  loginId: string
  userName: string
  employeeId: number | null
  employeeName: string | null
  isActive: boolean
  lastLoginAt: string | null
  roleIds: number[]
}

interface RoleOption {
  roleId: number
  roleCode: string
  roleName: string
  isActive: boolean
}

interface UserForm {
  loginId?: string
  userName: string
  employeeId?: number | null
  password?: string
  roleIds: number[]
  isActive?: boolean
  reason?: string
}

/** 사용자 관리 — 로그인 계정 × 역할(복수, 메뉴 권한 합집합 — 설계 §17 B8) */
export default function UsersPage() {
  const canCreate = useCan(MENU_KEY, 'create')
  const canUpdate = useCan(MENU_KEY, 'update')
  const { me } = useAuth()
  const queryClient = useQueryClient()
  const [includeInactive, setIncludeInactive] = useState(false)
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<User | 'new' | null>(null)
  const [resetting, setResetting] = useState<User | null>(null)

  const key = [...queryKeys.admin, 'users']
  const users = useQuery({
    queryKey: [...key, search, includeInactive],
    queryFn: ({ signal }) => api<User[]>(`/api/users?includeInactive=${includeInactive}&search=${encodeURIComponent(search)}`, { signal }),
  })
  const roles = useQuery({
    queryKey: [...key, 'roles'],
    queryFn: ({ signal }) => api<RoleOption[]>('/api/users/role-options', { signal }),
  })
  const roleName = (id: number) => roles.data?.find((r) => r.roleId === id)?.roleName ?? String(id)

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>사용자 관리</Typography.Title>
        <Space wrap>
          <Input.Search allowClear placeholder="아이디·이름" style={{ width: 200 }} onSearch={setSearch} />
          <Space size={4}><Switch size="small" checked={includeInactive} onChange={setIncludeInactive} />사용 중지 포함</Space>
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>사용자 추가</Button>}
        </Space>
      </Space>
      <Table<User> rowKey="appUserId" size="middle" loading={users.isFetching} dataSource={users.data ?? []} pagination={false}
        scroll={{ x: 800 }}
        onRow={(u) => ({ onClick: () => setEditing(u), style: { cursor: 'pointer', opacity: u.isActive ? 1 : 0.5 } })}
        columns={[
          { title: '아이디', dataIndex: 'loginId', width: 140 },
          { title: '이름', dataIndex: 'userName', width: 120, render: (v: string, u) => <>{v}{u.appUserId === me?.appUserId && <Tag style={{ marginLeft: 6 }}>나</Tag>}</> },
          { title: '사원', dataIndex: 'employeeName', width: 120 },
          { title: '역할', dataIndex: 'roleIds', render: (ids: number[]) => ids.map((id) => <Tag key={id} color="blue">{roleName(id)}</Tag>) },
          { title: '사용', dataIndex: 'isActive', width: 70, render: (v: boolean) => (v ? <Tag color="green">사용</Tag> : <Tag>중지</Tag>) },
          { title: '최근 로그인', dataIndex: 'lastLoginAt', width: 150, render: (v: string | null) => v && dayjs(v).format('YYYY-MM-DD HH:mm') },
          ...(canUpdate ? [{
            title: '', width: 120, render: (_: unknown, u: User) => (
              <Button size="small" icon={<KeyOutlined />} onClick={(e) => { e.stopPropagation(); setResetting(u) }}>비밀번호</Button>
            ),
          }] : []),
        ]} />
      {editing && (
        <UserModal user={editing === 'new' ? null : editing} roles={roles.data ?? []} self={editing !== 'new' && editing.appUserId === me?.appUserId}
          readOnly={editing === 'new' ? !canCreate : !canUpdate}
          onClose={() => setEditing(null)} onSaved={() => { setEditing(null); void queryClient.invalidateQueries({ queryKey: key }) }} />
      )}
      {resetting && <ResetPasswordModal user={resetting} onClose={() => setResetting(null)} />}
    </>
  )
}

function UserModal({ user, roles, self, readOnly, onClose, onSaved }: {
  user: User | null; roles: RoleOption[]; self: boolean; readOnly: boolean; onClose: () => void; onSaved: () => void
}) {
  const [form] = Form.useForm<UserForm>()
  const { message } = App.useApp()
  const [saving, setSaving] = useState(false)
  const employees = useQuery({
    queryKey: [...queryKeys.master, 'employee', 'options'],
    queryFn: ({ signal }) => api<MasterOption[]>('/api/master/employee/options', { signal }),
  })
  const isNew = user === null

  const save = async () => {
    const v = await form.validateFields()
    setSaving(true)
    try {
      if (isNew) await api('/api/users', { method: 'POST', body: { ...v, employeeId: v.employeeId ?? null } })
      else await api(`/api/users/${user.appUserId}`, { method: 'PUT', body: { ...v, employeeId: v.employeeId ?? null } })
      message.success('저장했습니다.')
      onSaved()
    } catch (e) {
      const errors = fieldErrors<UserForm>(e)
      if (errors.length > 0) form.setFields(errors)
      else message.error(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal open title={isNew ? '사용자 추가' : `사용자 — ${user.loginId}`} onCancel={onClose} onOk={() => void save()}
      okButtonProps={{ disabled: readOnly }} confirmLoading={saving} okText="저장" destroyOnHidden>
      <Form form={form} layout="vertical" disabled={readOnly} initialValues={isNew
        ? { roleIds: [], isActive: true }
        : { userName: user.userName, employeeId: user.employeeId ?? undefined, roleIds: user.roleIds, isActive: user.isActive }}>
        {isNew && (
          <Form.Item name="loginId" label="아이디" rules={[{ required: true, max: 50 }]}><Input autoComplete="off" /></Form.Item>
        )}
        <Form.Item name="userName" label="이름" rules={[{ required: true, max: 50 }]}><Input /></Form.Item>
        <Form.Item name="employeeId" label="사원 연결" extra="작업자·검사자 기록에 쓰는 사원">
          <Select allowClear showSearch optionFilterProp="label" loading={employees.isPending}
            options={(employees.data ?? []).filter((o) => o.active).map((o) => ({ value: o.value, label: o.label }))} />
        </Form.Item>
        <Form.Item name="roleIds" label="역할" extra={self ? '자기 계정의 역할은 바꿀 수 없습니다.' : '여러 역할이면 메뉴 권한을 합칩니다 (공용 PC: 생산+영업).'}>
          <Select mode="multiple" disabled={self || readOnly}
            options={roles.map((r) => ({ value: r.roleId, label: `${r.roleName} (${r.roleCode})`, disabled: !r.isActive }))} />
        </Form.Item>
        {isNew ? (
          <Form.Item name="password" label="초기 비밀번호" extra="최소 길이는 관리자 설정 auth.password_min_length" rules={[{ required: true }]}>
            <Input.Password autoComplete="new-password" />
          </Form.Item>
        ) : (
          <>
            <Form.Item name="isActive" label="사용" valuePropName="checked" extra={self ? '자기 계정은 사용 중지할 수 없습니다.' : undefined}>
              <Switch disabled={self || readOnly} />
            </Form.Item>
            <Form.Item name="reason" label="변경 사유"><Input maxLength={255} /></Form.Item>
          </>
        )}
      </Form>
    </Modal>
  )
}

function ResetPasswordModal({ user, onClose }: { user: User; onClose: () => void }) {
  const [form] = Form.useForm<{ newPassword: string }>()
  const { message } = App.useApp()
  const reset = async () => {
    const v = await form.validateFields()
    try {
      await api(`/api/users/${user.appUserId}/reset-password`, { method: 'POST', body: v })
      message.success('비밀번호를 초기화했습니다. 사용자에게 전달 후 변경하도록 안내하세요.')
      onClose()
    } catch (e) {
      const errors = fieldErrors<{ newPassword: string }>(e)
      if (errors.length > 0) form.setFields(errors)
      else message.error(e instanceof Error ? e.message : String(e))
    }
  }
  return (
    <Modal open title={`비밀번호 초기화 — ${user.loginId}`} onCancel={onClose} onOk={() => void reset()} okText="초기화" destroyOnHidden>
      <Form form={form} layout="vertical">
        <Form.Item name="newPassword" label="새 비밀번호" rules={[{ required: true }]}><Input.Password autoComplete="new-password" /></Form.Item>
      </Form>
    </Modal>
  )
}
