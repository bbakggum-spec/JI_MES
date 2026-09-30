import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { ClientSettings } from '../api/types'
import { queryKeys } from '../queryKeys'

/** 웹 동작용 설정 (시각·주기 하드코딩 금지 — 설계 §15.4). 변경은 SignalR settingChanged 로 무효화. */
export function useClientSettings() {
  const query = useQuery({
    queryKey: queryKeys.clientSettings,
    queryFn: ({ signal }) => api<ClientSettings>('/api/client-settings', { signal }),
    staleTime: Infinity,
  })
  const data = query.data
  return {
    ...query,
    dayStartTime: data?.['schedule.day_start_time'],
    refreshIntervalMs: data ? Number(data['schedule.refresh_interval_sec']) * 1000 : undefined,
    boardDays: data ? Number(data['schedule.board_days']) : undefined,
  }
}
