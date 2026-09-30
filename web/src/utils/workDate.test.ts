import { describe, expect, it } from 'vitest'
import { dayjs, workDate, workDayRange } from './workDate'

describe('workDate', () => {
  it('작업일 시작 시각 이전이면 전날 작업일', () => {
    expect(workDate(dayjs('2026-09-30T07:59'), '08:00').format('YYYY-MM-DD')).toBe('2026-09-29')
  })

  it('시작 시각부터는 당일 작업일', () => {
    expect(workDate(dayjs('2026-09-30T08:00'), '08:00').format('YYYY-MM-DD')).toBe('2026-09-30')
    expect(workDate(dayjs('2026-09-30T23:59'), '08:00').format('YYYY-MM-DD')).toBe('2026-09-30')
  })

  it('설정 시각을 따른다 (하드코딩 08:00 아님)', () => {
    expect(workDate(dayjs('2026-09-30T06:30'), '06:00').format('YYYY-MM-DD')).toBe('2026-09-30')
  })

  it('작업일 범위 = 시작 시각 ~ 다음날 시작 시각', () => {
    const [start, end] = workDayRange(dayjs('2026-09-30'), '08:30')
    expect(start.format('YYYY-MM-DD HH:mm')).toBe('2026-09-30 08:30')
    expect(end.format('YYYY-MM-DD HH:mm')).toBe('2026-10-01 08:30')
  })
})
