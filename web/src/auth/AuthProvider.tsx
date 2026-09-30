import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, type ReactNode } from 'react'
import { ApiError, api, setUnauthorizedListener } from '../api/client'
import type { Me } from '../api/types'
import { queryKeys } from '../queryKeys'
import { AuthContext, type AuthState } from './useAuth'

/** 로그인 상태 = GET /api/auth/me 결과. 401 이면 비로그인. */
export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()

  const meQuery = useQuery({
    queryKey: queryKeys.me,
    queryFn: async ({ signal }) => {
      try {
        return await api<Me>('/api/auth/me', { signal })
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null
        throw e
      }
    },
    staleTime: Infinity,
    retry: false,
  })

  // 어떤 API 든 401 이면 세션 만료 → 캐시를 비우고 비로그인 상태로
  const expire = useCallback(() => {
    queryClient.removeQueries({ predicate: (q) => q.queryKey[0] !== queryKeys.me[0] })
    queryClient.setQueryData(queryKeys.me, null)
  }, [queryClient])

  useEffect(() => {
    setUnauthorizedListener(expire)
    return () => setUnauthorizedListener(null)
  }, [expire])

  const login = useCallback(async (loginId: string, password: string) => {
    const me = await api<Me>('/api/auth/login', { method: 'POST', body: { loginId, password } })
    queryClient.setQueryData(queryKeys.me, me)
  }, [queryClient])

  const logout = useCallback(async () => {
    try {
      await api('/api/auth/logout', { method: 'POST' })
    } finally {
      queryClient.clear()
      queryClient.setQueryData(queryKeys.me, null)
    }
  }, [queryClient])

  const value = useMemo<AuthState>(() => ({
    me: meQuery.data ?? null,
    loading: meQuery.isPending,
    error: meQuery.error,
    login,
    logout,
  }), [meQuery.data, meQuery.isPending, meQuery.error, login, logout])

  return <AuthContext value={value}>{children}</AuthContext>
}
