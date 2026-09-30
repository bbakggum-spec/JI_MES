import dayjs, { type Dayjs } from 'dayjs'
import type { BoardBlock } from './scheduleTypes'

/** 시각 → 가로 위치(px). 범위 밖도 그대로 계산 (잘림은 화면에서) */
export function xOf(at: string | Dayjs, from: Dayjs, pxPerHour: number): number {
  return (dayjs(at).diff(from, 'minute') / 60) * pxPerHour
}

/** 가로 위치(px) → 시각 */
export function timeAt(x: number, from: Dayjs, pxPerHour: number): Dayjs {
  return from.add(Math.round((x / pxPerHour) * 60), 'minute')
}

/**
 * 드롭 위치에 해당하는 삽입 기준 블록 (그 블록 "앞"에 넣는다). null 이면 체인 맨 뒤.
 * 체인 = 계획·확정 상태 블록 (고정 포함 — 서버 체인 순서와 같다). 끌고 있는 블록 자신은 제외.
 * 기준: 드롭 시각보다 중간점이 뒤에 있는 첫 블록.
 */
export function insertionTarget(blocks: BoardBlock[], equipmentId: number, at: Dayjs, draggedId?: number): number | null {
  const chain = blocks
    .filter((b) => b.equipmentId === equipmentId && b.productionScheduleId !== draggedId
      && (b.status === 'PLANNED' || b.status === 'CONFIRMED'))
    .sort((a, b) => dayjs(a.plannedStartAt).valueOf() - dayjs(b.plannedStartAt).valueOf())
  const target = chain.find((b) => {
    const mid = dayjs(b.plannedStartAt).valueOf() + (dayjs(b.plannedEndAt).valueOf() - dayjs(b.plannedStartAt).valueOf()) / 2
    return mid > at.valueOf()
  })
  return target?.productionScheduleId ?? null
}

/** 블록 바로 다음 체인 블록 ("뒤에 삽입" = 다음 블록 앞) */
export function nextInChain(blocks: BoardBlock[], block: BoardBlock): number | null {
  const chain = blocks
    .filter((b) => b.equipmentId === block.equipmentId && (b.status === 'PLANNED' || b.status === 'CONFIRMED'))
    .sort((a, b) => dayjs(a.plannedStartAt).valueOf() - dayjs(b.plannedStartAt).valueOf())
  const i = chain.findIndex((b) => b.productionScheduleId === block.productionScheduleId)
  return i >= 0 && i + 1 < chain.length ? chain[i + 1].productionScheduleId : null
}

export interface DayColumn {
  workDate: Dayjs
  start: Dayjs
  end: Dayjs
}

/** 표시 범위의 작업일 칸 (작업일 시작 ~ 다음 작업일 시작) */
export function workDayColumns(from: Dayjs, to: Dayjs): DayColumn[] {
  const days: DayColumn[] = []
  for (let s = from; s.isBefore(to); s = s.add(1, 'day')) {
    days.push({ workDate: s.startOf('day'), start: s, end: s.add(1, 'day') })
  }
  return days
}

/** 시간 눈금 (간격 = 확대 비율에 따라 1·2·4·6시간) */
export function hourTicks(from: Dayjs, to: Dayjs, pxPerHour: number): Dayjs[] {
  const step = pxPerHour >= 60 ? 1 : pxPerHour >= 30 ? 2 : pxPerHour >= 15 ? 4 : 6
  const ticks: Dayjs[] = []
  for (let t = from; t.isBefore(to); t = t.add(step, 'hour')) ticks.push(t)
  return ticks
}
