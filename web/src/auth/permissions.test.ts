import { describe, expect, it } from 'vitest'
import type { Me, MenuNode } from '../api/types'
import { can, findMenuPath, flattenRoutes } from './permissions'

const menus: MenuNode[] = [
  { menuKey: 'master', menuName: '기준정보', route: null, children: [] },
  {
    menuKey: 'system', menuName: '시스템', route: null, children: [
      { menuKey: 'system.setting', menuName: '관리자 설정', route: '/system/settings', children: [] },
      { menuKey: 'system.audit', menuName: '변경 이력', route: '/system/audit-logs', children: [] },
    ],
  },
]

const me: Me = {
  appUserId: 1, loginId: 'u', userName: 'u', roles: ['R'], menus,
  permissions: { 'system.setting': { read: true, create: false, update: false, delete: false } },
}

describe('permissions', () => {
  it('can: 서버가 준 권한만 true, 없는 메뉴·비로그인은 false', () => {
    expect(can(me, 'system.setting', 'read')).toBe(true)
    expect(can(me, 'system.setting', 'update')).toBe(false)
    expect(can(me, 'system.code', 'read')).toBe(false)
    expect(can(null, 'system.setting', 'read')).toBe(false)
  })

  it('flattenRoutes: route 있는 메뉴만', () => {
    expect(flattenRoutes(menus).map((m) => m.menuKey)).toEqual(['system.setting', 'system.audit'])
  })

  it('findMenuPath: 상위 → 해당 메뉴, 하위 경로도 매칭', () => {
    expect(findMenuPath(menus, '/system/settings').map((m) => m.menuKey)).toEqual(['system', 'system.setting'])
    expect(findMenuPath(menus, '/system/audit-logs/12').map((m) => m.menuKey)).toEqual(['system', 'system.audit'])
    expect(findMenuPath(menus, '/system/settingsX')).toEqual([])
  })
})
