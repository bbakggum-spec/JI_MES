import {
  AppstoreOutlined, BarChartOutlined, DashboardOutlined, DatabaseOutlined, ExperimentOutlined,
  FireOutlined, SettingOutlined, ShoppingCartOutlined, ToolOutlined,
} from '@ant-design/icons'
import type { MenuProps } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import type { MenuNode } from '../api/types'

/** 최상위 메뉴 아이콘 (화면 표현만 — 메뉴 구성·이름은 DB menu 테이블) */
const topIcons: Record<string, ReactNode> = {
  master: <DatabaseOutlined />,
  sales: <ShoppingCartOutlined />,
  production: <FireOutlined />,
  quality: <ExperimentOutlined />,
  equipment: <ToolOutlined />,
  report: <BarChartOutlined />,
  system: <SettingOutlined />,
}

export const HOME_KEY = '__home'

type Items = NonNullable<MenuProps['items']>

export function toMenuItems(menus: MenuNode[], depth = 0): Items {
  return menus.map((m) => {
    const icon = depth === 0 ? topIcons[m.menuKey] ?? <AppstoreOutlined /> : undefined
    if (m.children.length > 0) {
      return { key: m.menuKey, icon, label: m.menuName, children: toMenuItems(m.children, depth + 1) }
    }
    return {
      key: m.menuKey,
      icon,
      label: m.route ? <Link to={m.route}>{m.menuName}</Link> : m.menuName,
      disabled: !m.route,   // 하위 화면이 아직 없는 메뉴 그룹
    }
  })
}

export function homeItem(label: string): Items[number] {
  return { key: HOME_KEY, icon: <DashboardOutlined />, label: <Link to="/">{label}</Link> }
}
