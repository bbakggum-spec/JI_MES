import { createContext, use } from 'react'
import type { Me } from '../api/types'
import { can, type PermissionAction } from './permissions'

export interface AuthState {
  me: Me | null
  loading: boolean
  error: Error | null
  login: (loginId: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthState | null>(null)

export function useAuth(): AuthState {
  const ctx = use(AuthContext)
  if (!ctx) throw new Error('AuthProvider 밖에서 useAuth 를 사용했습니다.')
  return ctx
}

/** 메뉴 권한 확인 — 버튼 표시용 */
export function useCan(menuKey: string, action: PermissionAction): boolean {
  return can(useAuth().me, menuKey, action)
}
