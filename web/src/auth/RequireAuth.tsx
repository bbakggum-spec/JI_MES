import { Button, Result, Spin } from 'antd'
import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { useAuth } from './useAuth'

/** 비로그인이면 로그인 화면으로 (돌아올 경로를 state.from 에 담는다). */
export default function RequireAuth({ children }: { children: ReactNode }) {
  const { me, loading, error } = useAuth()
  const location = useLocation()

  if (loading) return <Spin fullscreen />
  if (error) {
    return <Result status="warning" title="서버에 연결할 수 없습니다" subTitle={error.message}
      extra={<Button type="primary" onClick={() => window.location.reload()}>다시 시도</Button>} />
  }
  if (!me) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  return children
}
