import { createContext, use } from 'react'

/** 서버 이벤트 이름 — api Infrastructure/Realtime/EventsHub.cs RealtimeEvents 와 같아야 한다 */
export const RealtimeEvents = {
  settingChanged: 'settingChanged',
  commonCodeChanged: 'commonCodeChanged',
} as const

export type RealtimeStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

export const RealtimeContext = createContext<RealtimeStatus>('disconnected')

export function useRealtimeStatus(): RealtimeStatus {
  return use(RealtimeContext)
}
