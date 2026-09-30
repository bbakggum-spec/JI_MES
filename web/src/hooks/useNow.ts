import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useState } from 'react'

/** 주기적으로 갱신되는 현재 시각 (작업일 표시·현재선). 주기는 설정 schedule.refresh_interval_sec */
export function useNow(intervalMs: number | undefined): Dayjs {
  const [now, setNow] = useState(() => dayjs())
  useEffect(() => {
    if (!intervalMs) return
    const id = window.setInterval(() => setNow(dayjs()), intervalMs)
    return () => window.clearInterval(id)
  }, [intervalMs])
  return now
}
