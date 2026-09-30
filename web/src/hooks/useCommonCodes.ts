import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { api } from '../api/client'
import type { CommonCode, CommonCodeGroup } from '../api/types'
import { queryKeys } from '../queryKeys'

export function useCommonCodeGroups() {
  return useQuery({
    queryKey: queryKeys.commonCodes,
    queryFn: ({ signal }) => api<CommonCodeGroup[]>('/api/common-codes', { signal }),
    staleTime: Infinity,   // 변경은 SignalR commonCodeChanged 로 무효화
  })
}

/**
 * 코드값 → 표시명. 로직은 코드값(PASS, ALLOCATED …)으로 비교하고 화면 표시는 여기서 (설계 §15.4).
 * 없는 코드는 코드값 그대로 보여준다.
 */
export function useCommonCodes() {
  const { data } = useCommonCodeGroups()
  return useMemo(() => {
    const groups = new Map((data ?? []).map((g) => [g.groupCode, g]))
    return {
      loaded: data !== undefined,
      name: (group: string, code: string | null | undefined): string =>
        code == null ? '' : groups.get(group)?.codes.find((c) => c.code === code)?.codeName ?? code,
      /** 선택 목록 — 사용 중인 코드만, 정렬 순서대로 */
      options: (group: string): CommonCode[] =>
        (groups.get(group)?.codes ?? []).filter((c) => c.isActive).sort((a, b) => a.sortOrder - b.sortOrder),
      attr: <T = Record<string, unknown>>(group: string, code: string): T | null => {
        const json = groups.get(group)?.codes.find((c) => c.code === code)?.attrJson
        return json ? (JSON.parse(json) as T) : null
      },
    }
  }, [data])
}
