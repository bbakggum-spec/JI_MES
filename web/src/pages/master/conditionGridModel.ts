import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { queryKeys } from '../../queryKeys'
import type { StepTemplateDetail } from './StepTemplatesPage'

/** 조건 입력표 모델 — ConditionGrid 화면과 저장·불러오기 변환 (설계 §22.5) */

export interface GridStep {
  /** 화면 안에서만 쓰는 열 식별자 (이름·순서가 바뀌어도 값이 따라가도록) */
  key: string
  name: string
}

export interface GridItem {
  conditionItemId: number
  conditionItemName: string
  unitCode: string | null
  valueType: string
  isActive?: boolean
}

export interface GridLayout {
  steps: GridStep[]
  items: GridItem[]
}

/** 셀 값 — 키는 cellKey(스텝 key 또는 COMMON, 항목 id) */
export type GridValues = Record<string, string>

export const COMMON = 'common'
export const cellKey = (stepKey: string, itemId: number) => `${stepKey}:${itemId}`
export const newStep = (name = ''): GridStep => ({ key: crypto.randomUUID(), name })
export const emptyLayout = (): GridLayout => ({ steps: [], items: [] })

export interface ConditionRow {
  stepNo: number | null
  conditionItemId: number
  conditionValue: string | null
}

/** 저장용: 입력표에 남아 있는 스텝·항목의 빈 칸 아닌 값만 (스텝 번호 = 열 순서 1..N, 공통 = null) */
export function toConditions(layout: GridLayout, values: GridValues): ConditionRow[] {
  const cols: [string, number | null][] = [[COMMON, null], ...layout.steps.map((s, i): [string, number] => [s.key, i + 1])]
  return layout.items.flatMap((item) => cols
    .map(([key, stepNo]) => ({ stepNo, conditionItemId: item.conditionItemId, conditionValue: values[cellKey(key, item.conditionItemId)]?.trim() ?? '' }))
    .filter((c) => c.conditionValue !== ''))
}

/** 불러오기: 서버 조건(스텝 번호) → 화면 값(스텝 key) */
export function toValues(steps: GridStep[], conditions: ConditionRow[]): GridValues {
  return Object.fromEntries(conditions.map((c) => [
    cellKey(c.stepNo === null ? COMMON : steps[c.stepNo - 1]?.key ?? `missing${c.stepNo}`, c.conditionItemId), c.conditionValue ?? '',
  ]))
}

/** 관리항목 후보 (이름·단위·값 형식) */
export function useConditionItems() {
  return useQuery({
    queryKey: [...queryKeys.master, 'condition_item', 'rows'],
    queryFn: ({ signal }) => api<(GridItem & { conditionItemCode: string; isActive: boolean })[]>('/api/step-templates/condition-items', { signal }),
    staleTime: 60_000,
  })
}

/** 단계 템플릿 → 입력표 구성 */
export const layoutOf = (d: StepTemplateDetail): GridLayout => ({
  steps: d.steps.map((s) => newStep(s.stepName)),
  items: d.conditions.map((c) => ({ conditionItemId: c.conditionItemId, conditionItemName: c.conditionItemName, unitCode: c.unitCode, valueType: c.valueType })),
})
