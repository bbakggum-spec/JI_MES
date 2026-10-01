import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type ReactNode } from 'react'
import { useAuth } from '../auth/useAuth'
import { queryKeys } from '../queryKeys'
import { RealtimeContext, RealtimeEvents, type RealtimeStatus } from './realtime'

/**
 * 로그인 중에만 /hubs/events 에 연결한다. 알림은 "무엇이 바뀌었는지"만 알려주므로
 * 해당 조회 캐시를 무효화해 다시 읽게 한다 (설계 §18.4). 연결이 끊겨도 화면은 주기 조회로 동작한다.
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { me } = useAuth()
  const queryClient = useQueryClient()
  const userId = me?.appUserId
  // 상태를 연결(사용자)별로 기록 — 새 연결은 첫 이벤트 전까지 'connecting'
  const [state, setState] = useState<{ userId?: number; status: RealtimeStatus }>({ status: 'disconnected' })

  useEffect(() => {
    if (userId === undefined) return

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/events')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    let disposed = false
    const report = (status: RealtimeStatus) => { if (!disposed) setState({ userId, status }) }

    connection.on(RealtimeEvents.settingChanged, () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.settings })
      void queryClient.invalidateQueries({ queryKey: queryKeys.clientSettings })
    })
    connection.on(RealtimeEvents.commonCodeChanged, () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.commonCodes })
    })
    // 다른 사용자의 조작·지연 반영 재계산 → 보드·배정 대기 다시 조회 (구 60초 건수 비교 폴링 대체, 설계 §15.1 S5)
    connection.on(RealtimeEvents.scheduleChanged, () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.schedule })
    })
    // 다른 사용자의 작업자 배치 변경 → 배치 보드 다시 조회
    connection.on(RealtimeEvents.workerAssignmentChanged, () => {
      void queryClient.invalidateQueries({ queryKey: [...queryKeys.equipment, 'assignments'] })
    })
    connection.onreconnecting(() => report('reconnecting'))
    connection.onreconnected(() => {
      report('connected')
      void queryClient.invalidateQueries()   // 끊긴 동안 놓친 변경 반영
    })
    connection.onclose(() => report('disconnected'))
    connection.start().then(() => report('connected'), () => report('disconnected'))

    return () => {
      disposed = true
      void connection.stop()
    }
  }, [userId, queryClient])

  const status: RealtimeStatus = userId === undefined
    ? 'disconnected'
    : state.userId === userId ? state.status : 'connecting'

  return <RealtimeContext value={status}>{children}</RealtimeContext>
}
