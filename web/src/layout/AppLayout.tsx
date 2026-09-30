import { DownOutlined, KeyOutlined, LogoutOutlined, UserOutlined } from '@ant-design/icons'
import { Badge, Breadcrumb, Button, Dropdown, Layout, Menu, Result, Space, Spin, Tooltip, Typography, theme } from 'antd'
import { Suspense, useMemo, useState } from 'react'
import { useLocation, useNavigate, useRoutes, type RouteObject } from 'react-router'
import { useAuth } from '../auth/useAuth'
import { findMenuPath, flattenRoutes } from '../auth/permissions'
import ChangePasswordModal from '../pages/ChangePasswordModal'
import DashboardPage from '../pages/DashboardPage'
import PlaceholderPage from '../pages/PlaceholderPage'
import { pageRegistry } from '../pages/registry'
import { useRealtimeStatus, type RealtimeStatus } from '../realtime/realtime'
import { HOME_KEY, homeItem, toMenuItems } from './menuItems'

const { Header, Sider, Content } = Layout

const HOME_LABEL = '대시보드'

const realtimeBadge: Record<RealtimeStatus, { status: 'success' | 'processing' | 'warning' | 'default'; text: string }> = {
  connected: { status: 'success', text: '실시간 연결됨' },
  connecting: { status: 'processing', text: '실시간 연결 중' },
  reconnecting: { status: 'warning', text: '실시간 재연결 중' },
  disconnected: { status: 'default', text: '실시간 끊김 — 주기 조회로 동작' },
}

export default function AppLayout() {
  const { me, logout } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const realtime = useRealtimeStatus()
  const { token } = theme.useToken()
  const [collapsed, setCollapsed] = useState(false)
  const [changingPassword, setChangingPassword] = useState(false)

  const menus = useMemo(() => me?.menus ?? [], [me])
  const menuPath = findMenuPath(menus, location.pathname)
  const selectedKey = location.pathname === '/' ? HOME_KEY : menuPath.at(-1)?.menuKey
  const [openKeys, setOpenKeys] = useState<string[]>(() => menuPath.slice(0, -1).map((m) => m.menuKey))

  const items = useMemo(() => [homeItem(HOME_LABEL), ...toMenuItems(menus)], [menus])

  const routes = useMemo<RouteObject[]>(() => [
    { index: true, element: <DashboardPage /> },
    ...flattenRoutes(menus).map((m) => {
      const Page = pageRegistry[m.menuKey]
      return { path: m.route!, element: Page ? <Page /> : <PlaceholderPage title={m.menuName} /> }
    }),
    {
      path: '*',
      element: <Result status="404" title="페이지를 찾을 수 없습니다"
        subTitle="주소가 잘못되었거나 이 화면을 볼 권한이 없습니다."
        extra={<Button type="primary" onClick={() => navigate('/')}>{HOME_LABEL}로</Button>} />,
    },
  ], [menus, navigate])
  const page = useRoutes(routes)

  if (!me) return null

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider collapsible collapsed={collapsed} onCollapse={setCollapsed} width={220} theme="light"
        style={{ borderRight: `1px solid ${token.colorBorderSecondary}` }}>
        <div style={{ height: 56, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 8 }}>
          <img src="/favicon.svg" alt="" width={24} height={24} />
          {!collapsed && <Typography.Text strong style={{ fontSize: 16 }}>JI MES</Typography.Text>}
        </div>
        <Menu mode="inline" items={items} selectedKeys={selectedKey ? [selectedKey] : []}
          openKeys={collapsed ? undefined : openKeys} onOpenChange={setOpenKeys} style={{ borderInlineEnd: 0 }} />
      </Sider>
      <Layout>
        <Header style={{
          background: token.colorBgContainer, padding: '0 24px', display: 'flex', alignItems: 'center',
          justifyContent: 'space-between', borderBottom: `1px solid ${token.colorBorderSecondary}`,
        }}>
          <Breadcrumb items={[{ title: HOME_LABEL }, ...menuPath.map((m) => ({ title: m.menuName }))]} />
          <Space size="large">
            <Tooltip title={realtimeBadge[realtime].text}>
              <Badge status={realtimeBadge[realtime].status} text={realtime === 'connected' ? '실시간' : '주기 조회'} />
            </Tooltip>
            <Dropdown menu={{
              items: [
                { key: 'password', icon: <KeyOutlined />, label: '비밀번호 변경', onClick: () => setChangingPassword(true) },
                { type: 'divider' },
                { key: 'logout', icon: <LogoutOutlined />, label: '로그아웃', onClick: () => void logout() },
              ],
            }}>
              <Button type="text" icon={<UserOutlined />}>
                {me.userName} <DownOutlined />
              </Button>
            </Dropdown>
          </Space>
        </Header>
        <Content style={{ margin: 24 }}>
          <Suspense fallback={<Spin style={{ display: 'block', marginTop: 80 }} />}>{page}</Suspense>
        </Content>
      </Layout>
      <ChangePasswordModal open={changingPassword} onClose={() => setChangingPassword(false)} />
    </Layout>
  )
}
