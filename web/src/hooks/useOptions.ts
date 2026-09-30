import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { queryKeys } from '../queryKeys'

export interface OptionRow {
  value: number
  label: string
  active: boolean
}

/**
 * 선택 목록 (id·표시명). 기준정보 /api/master/{key}/options, /api/parts/options 등.
 * 사용 중지 항목은 비활성으로 표시 (기존 값 표시는 유지).
 */
export function useOptions(path: string | null) {
  const query = useQuery({
    queryKey: [...queryKeys.master, 'options', path],
    queryFn: ({ signal }) => api<OptionRow[]>(path!, { signal }),
    enabled: path !== null,
    staleTime: 60_000,
  })
  return {
    ...query,
    options: (query.data ?? []).map((o) => ({ value: o.value, label: o.active ? o.label : `${o.label} (중지)`, disabled: !o.active })),
    labelOf: (id: number | null | undefined) => query.data?.find((o) => o.value === id)?.label,
  }
}
