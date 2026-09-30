import { lazy, type ComponentType, type LazyExoticComponent } from 'react'

/**
 * menu_key → 화면. 경로는 DB menu.route 를 따른다 (서버가 읽기 권한 있는 메뉴만 내려주므로
 * 권한 없는 화면은 라우트 자체가 생기지 않는다). 등록되지 않은 메뉴는 "준비 중" 화면.
 * 새 화면: DDL §9.8 메뉴 추가 → 여기 등록.
 */
export const pageRegistry: Record<string, LazyExoticComponent<ComponentType>> = {
  'production.schedule': lazy(() => import('./production/SchedulePage')),
  'system.setting': lazy(() => import('./system/SettingsPage')),
  'system.code': lazy(() => import('./system/CommonCodesPage')),
  'system.audit': lazy(() => import('./system/AuditLogsPage')),
  'system.print': lazy(() => import('./system/PrintTemplatesPage')),
}
