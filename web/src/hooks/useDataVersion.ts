import { useState } from 'react'

/**
 * 서버 자료의 "내용이 바뀐 횟수" — 편집 폼의 key 로 쓴다.
 * react-query 는 다시 조회해도 내용이 같으면 같은 객체를 돌려주므로(구조 공유), 실시간 재접속·재조회로
 * 입력 중인 폼이 초기화되지 않고, 저장 등으로 내용이 실제로 바뀔 때만 폼을 새로 만든다.
 */
export function useDataVersion(data: unknown): number {
  const [state, setState] = useState({ data, version: 0 })
  if (state.data !== data) {
    const next = { data, version: state.version + 1 }
    setState(next)
    return next.version
  }
  return state.version
}
