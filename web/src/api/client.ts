/**
 * API 호출 공통. 서버 오류는 ProblemDetails(`title`, `code`, `errors`)로 온다 (설계 §18.1).
 * 화면은 `ApiError.code` 로 분기하고 `message`(서버 title)는 그대로 표시한다.
 */
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly errors?: Record<string, string[]>

  constructor(status: number, message: string, code?: string, errors?: Record<string, string[]>) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.errors = errors
  }
}

type UnauthorizedListener = () => void
let onUnauthorized: UnauthorizedListener | null = null

/** 세션 만료(401) 시 호출할 처리 — AuthProvider 가 등록한다. */
export function setUnauthorizedListener(listener: UnauthorizedListener | null) {
  onUnauthorized = listener
}

const LOGIN_PATH = '/api/auth/login'

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  signal?: AbortSignal
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, signal } = options
  let res: Response
  try {
    res = await fetch(path, {
      method,
      signal,
      credentials: 'same-origin',
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') throw e
    throw new ApiError(0, '서버에 연결할 수 없습니다.', 'NETWORK')
  }

  if (res.ok) {
    if (res.status === 204) return undefined as T
    const text = await res.text()
    return (text ? JSON.parse(text) : undefined) as T
  }

  const problem = await readProblem(res)
  if (res.status === 401 && path !== LOGIN_PATH) onUnauthorized?.()
  throw new ApiError(res.status, problem.title ?? defaultMessage(res.status), problem.code, problem.errors)
}

interface Problem {
  title?: string
  code?: string
  errors?: Record<string, string[]>
}

async function readProblem(res: Response): Promise<Problem> {
  try {
    const text = await res.text()
    return text ? (JSON.parse(text) as Problem) : {}
  } catch {
    return {}
  }
}

function defaultMessage(status: number): string {
  switch (status) {
    case 401: return '로그인이 필요합니다.'
    case 403: return '권한이 없습니다.'
    case 404: return '대상을 찾을 수 없습니다.'
    case 409: return '다른 사용자가 먼저 수정했습니다.'
    case 429: return '요청이 너무 많습니다. 잠시 후 다시 시도하세요.'
    default: return `요청을 처리하지 못했습니다. (${status})`
  }
}

/** 서버 검증 오류 키(PascalCase 속성명)를 폼 필드명(camelCase)으로 바꾼다. */
export function fieldErrors<TForm = Record<string, unknown>>(
  error: unknown,
): { name: Extract<keyof TForm, string>; errors: string[] }[] {
  if (!(error instanceof ApiError) || !error.errors) return []
  return Object.entries(error.errors).map(([key, errors]) => ({
    name: (key.charAt(0).toLowerCase() + key.slice(1)) as Extract<keyof TForm, string>,
    errors,
  }))
}
