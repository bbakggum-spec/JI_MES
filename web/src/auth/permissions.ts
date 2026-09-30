import type { Me, MenuNode } from '../api/types'

export type PermissionAction = 'read' | 'create' | 'update' | 'delete'

/** 서버가 준 메뉴별 권한으로 버튼·화면 표시 여부를 정한다. 최종 차단은 서버(403)가 한다. */
export function can(me: Me | null | undefined, menuKey: string, action: PermissionAction): boolean {
  return me?.permissions[menuKey]?.[action] ?? false
}

/** 메뉴 트리에서 route 가 있는 메뉴만 평탄화 (라우트 생성용). */
export function flattenRoutes(menus: MenuNode[]): MenuNode[] {
  return menus.flatMap((m) => [...(m.route ? [m] : []), ...flattenRoutes(m.children)])
}

/** 경로에 해당하는 메뉴와 그 상위 메뉴 key 목록 (사이드바 선택·펼침용). */
export function findMenuPath(menus: MenuNode[], pathname: string): MenuNode[] {
  for (const m of menus) {
    if (m.route && (pathname === m.route || pathname.startsWith(m.route + '/'))) return [m]
    const child = findMenuPath(m.children, pathname)
    if (child.length > 0) return [m, ...child]
  }
  return []
}
