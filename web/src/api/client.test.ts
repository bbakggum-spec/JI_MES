import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api, fieldErrors, setUnauthorizedListener } from './client'

function mockFetch(status: number, body?: unknown) {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(
    body === undefined ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })))
}

afterEach(() => {
  vi.unstubAllGlobals()
  setUnauthorizedListener(null)
})

describe('api', () => {
  it('ProblemDetails 를 ApiError(code, message, errors)로 바꾼다', async () => {
    mockFetch(400, { title: '입력값이 올바르지 않습니다.', code: 'VALIDATION', errors: { CodeName: ['필수'] } })
    const error = await api('/api/x').catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 400, code: 'VALIDATION', message: '입력값이 올바르지 않습니다.' })
    expect(fieldErrors(error)).toEqual([{ name: 'codeName', errors: ['필수'] }])
  })

  it('본문 없는 403 은 기본 메시지', async () => {
    mockFetch(403)
    await expect(api('/api/x')).rejects.toMatchObject({ status: 403, message: '권한이 없습니다.' })
  })

  it('401 이면 세션 만료 처리 — 로그인 요청의 401 은 제외', async () => {
    const listener = vi.fn()
    setUnauthorizedListener(listener)
    mockFetch(401)
    await expect(api('/api/settings')).rejects.toBeInstanceOf(ApiError)
    expect(listener).toHaveBeenCalledTimes(1)
    await expect(api('/api/auth/login', { method: 'POST', body: {} })).rejects.toBeInstanceOf(ApiError)
    expect(listener).toHaveBeenCalledTimes(1)
  })

  it('204 는 undefined', async () => {
    mockFetch(204)
    await expect(api('/api/auth/logout', { method: 'POST' })).resolves.toBeUndefined()
  })
})
