import { lazy, type ComponentType, type LazyExoticComponent } from 'react'

export interface PageProps {
  /** 이 화면을 연 메뉴 (권한 키) */
  menuKey: string
}

type Page = LazyExoticComponent<ComponentType<PageProps>>

/**
 * menu_key → 화면. 경로는 DB menu.route 를 따른다 (서버가 읽기 권한 있는 메뉴만 내려주므로
 * 권한 없는 화면은 라우트 자체가 생기지 않는다). 등록되지 않은 메뉴는 "준비 중" 화면.
 * 새 화면: DDL §9.8 메뉴 추가 → 여기 등록. 단순 기준정보(master.*)는 등록 없이 범용 화면 (API 정의 MasterCatalog).
 */
export const pageRegistry: Record<string, Page> = {
  'sales.order': lazy(() => import('./sales/SalesOrdersPage')),
  'sales.shipment': lazy(() => import('./sales/ShipmentsPage')),
  'sales.closing': lazy(() => import('./sales/ClosingsPage')),
  'report.lot': lazy(() => import('./report/LotsPage')),
  'report.order': lazy(() => import('./report/OrdersReportPage')),
  'equipment.downtime': lazy(() => import('./equipment/DowntimePage')),
  'equipment.maintenance': lazy(() => import('./equipment/MaintenancePage')),
  'quality.calibration': lazy(() => import('./quality/CalibrationsPage')),
  'production.schedule': lazy(() => import('./production/SchedulePage')),
  'production.work': lazy(() => import('./production/WorksPage')),
  'quality.inspection': lazy(() => import('./quality/InspectionsPage')),
  'quality.defect': lazy(() => import('./quality/DefectsPage')),
  'master.part': lazy(() => import('./master/PartsPage')),
  'master.heat_process': lazy(() => import('./master/HeatProcessesPage')),
  'master.step_template': lazy(() => import('./master/StepTemplatesPage')),
  'master.standard': lazy(() => import('./master/StandardsPage')),
  'master.inspection_standard': lazy(() => import('./master/InspectionStandardsPage')),
  'system.user': lazy(() => import('./system/UsersPage')),
  'system.role': lazy(() => import('./system/RolesPage')),
  'system.setting': lazy(() => import('./system/SettingsPage')),
  'system.code': lazy(() => import('./system/CommonCodesPage')),
  'system.audit': lazy(() => import('./system/AuditLogsPage')),
  'system.print': lazy(() => import('./system/PrintTemplatesPage')),
}

const MasterPage: Page = lazy(() => import('./master/MasterPage'))

export function pageFor(menuKey: string): Page | undefined {
  return pageRegistry[menuKey] ?? (menuKey.startsWith('master.') ? MasterPage : undefined)
}
