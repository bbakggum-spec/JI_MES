import dayjs, { type Dayjs } from 'dayjs'

/**
 * 작업일: 작업일 시작 시각 이전이면 전날 작업일로 본다 (구 `Hour < 8` 규칙, 설계 §15.4 H1).
 * 시작 시각은 설정 schedule.day_start_time (HH:mm) — 교대(work_shift) 반영은 API 스케줄 서비스에서.
 */
export function workDate(now: Dayjs, dayStartTime: string): Dayjs {
  const [h, m] = dayStartTime.split(':').map(Number)
  const start = now.startOf('day').hour(h).minute(m)
  return (now.isBefore(start) ? now.subtract(1, 'day') : now).startOf('day')
}

/** 작업일의 시작~끝 (끝 = 다음 작업일 시작) */
export function workDayRange(date: Dayjs, dayStartTime: string): [Dayjs, Dayjs] {
  const [h, m] = dayStartTime.split(':').map(Number)
  const start = date.startOf('day').hour(h).minute(m)
  return [start, start.add(1, 'day')]
}

export { dayjs }
