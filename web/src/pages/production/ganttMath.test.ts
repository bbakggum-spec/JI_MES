import dayjs from 'dayjs'
import { describe, expect, it } from 'vitest'
import { hourTicks, insertionTarget, nextInChain, timeAt, workDayColumns, xOf } from './ganttMath'
import type { BoardBlock } from './scheduleTypes'

const from = dayjs('2026-10-05T08:00:00')

function block(id: number, start: string, end: string, extra: Partial<BoardBlock> = {}): BoardBlock {
  return {
    productionScheduleId: id, equipmentId: 1, unitProcessId: 1, unitProcessName: '침탄', workDate: '2026-10-05', sequenceNo: id,
    plannedLotNo: `P-${id}`, plannedQty: 1, plannedDurationMin: 60, durationSource: 'STANDARD',
    plannedStartAt: `2026-10-05T${start}:00`, plannedEndAt: `2026-10-05T${end}:00`,
    status: 'PLANNED', isTimeLocked: false, isRework: false, rowVersion: 0, items: [], ...extra,
  }
}

const blocks = [block(1, '09:00', '11:00'), block(2, '11:00', '13:00'), block(3, '13:00', '14:00', { status: 'RELEASED' }), block(4, '14:00', '16:00')]

describe('ganttMath', () => {
  it('시각 ↔ 위치 변환', () => {
    expect(xOf('2026-10-05T10:30:00', from, 30)).toBe(75)
    expect(timeAt(75, from, 30).format('HH:mm')).toBe('10:30')
  })

  it('드롭 위치 = 중간점이 뒤에 있는 첫 블록 앞', () => {
    expect(insertionTarget(blocks, 1, dayjs('2026-10-05T09:30:00'))).toBe(1)
    expect(insertionTarget(blocks, 1, dayjs('2026-10-05T10:30:00'))).toBe(2)   // 1번 중간(10:00) 지남
    expect(insertionTarget(blocks, 1, dayjs('2026-10-05T20:00:00'))).toBeNull() // 맨 뒤
  })

  it('작업지시(RELEASED) 블록은 체인 기준에서 제외, 끌고 있는 블록 자신도 제외', () => {
    expect(insertionTarget(blocks, 1, dayjs('2026-10-05T13:10:00'))).toBe(4)
    expect(insertionTarget(blocks, 1, dayjs('2026-10-05T09:30:00'), 1)).toBe(2)
    expect(insertionTarget(blocks, 2, dayjs('2026-10-05T09:30:00'))).toBeNull()  // 다른 설비
  })

  it('뒤에 삽입 = 다음 체인 블록 앞', () => {
    expect(nextInChain(blocks, blocks[1])).toBe(4)
    expect(nextInChain(blocks, blocks[3])).toBeNull()
  })

  it('작업일 칸과 눈금', () => {
    const days = workDayColumns(from, from.add(2, 'day'))
    expect(days.map((d) => d.start.format('MM-DD HH:mm'))).toEqual(['10-05 08:00', '10-06 08:00'])
    expect(hourTicks(from, from.add(1, 'day'), 30)).toHaveLength(12)   // 30px/h → 2시간 간격
  })
})
