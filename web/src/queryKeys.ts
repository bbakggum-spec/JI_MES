/** react-query 캐시 키 (실시간 알림이 무효화할 대상과 공유) */
export const queryKeys = {
  me: ['auth', 'me'] as const,
  settings: ['settings'] as const,
  clientSettings: ['client-settings'] as const,
  commonCodes: ['common-codes'] as const,
  auditLogs: ['audit-logs'] as const,
  health: ['health'] as const,
  schedule: ['schedule'] as const,
  print: ['print'] as const,
  master: ['master'] as const,
  sales: ['sales'] as const,
  works: ['works'] as const,
  admin: ['admin'] as const,
}
