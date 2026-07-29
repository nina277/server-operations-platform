import { describe, it, expect, vi } from 'vitest'
import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'
import { useAsyncData } from '../useAsyncData'

function httpError(status: number, message: string): AxiosError {
  const config = { headers: new AxiosHeaders() } as InternalAxiosRequestConfig
  const error = new AxiosError(message, 'ERR_BAD_REQUEST', config)
  error.response = {
    data: { success: false, data: null, error: { code: 'x', message }, traceId: null },
    status,
    statusText: 'Error',
    headers: new AxiosHeaders(),
    config,
  }
  return error
}

describe('useAsyncData', () => {
  it('取得できたら読み込み状態を解除する', async () => {
    const state = useAsyncData(() => Promise.resolve(['a']), '既定')
    await state.load()

    expect(state.data.value).toEqual(['a'])
    expect(state.loading.value).toBe(false)
    expect(state.error.value).toBeNull()
    expect(state.forbidden.value).toBe(false)
  })

  it('403は取得失敗ではなく権限不足として扱う', async () => {
    const state = useAsyncData(() => Promise.reject(httpError(403, '権限がありません。')), '既定')
    await state.load()

    expect(state.forbidden.value).toBe(true)
    expect(state.error.value).toBeNull()
  })

  it('その他の失敗はAPIの文言を出す', async () => {
    const state = useAsyncData(() => Promise.reject(httpError(500, '内部エラー')), '既定')
    await state.load()

    expect(state.forbidden.value).toBe(false)
    expect(state.error.value).toBe('内部エラー')
  })

  it('API以外の例外では既定の文言を出す', async () => {
    const state = useAsyncData(() => Promise.reject(new Error('boom')), '既定')
    await state.load()

    expect(state.error.value).toBe('既定')
  })

  it('再取得すると前回の失敗を消す', async () => {
    const fetcher = vi
      .fn<() => Promise<string>>()
      .mockRejectedValueOnce(httpError(500, '内部エラー'))
      .mockResolvedValueOnce('ok')

    const state = useAsyncData(fetcher, '既定')
    await state.load()
    expect(state.error.value).toBe('内部エラー')

    await state.load()
    expect(state.error.value).toBeNull()
    expect(state.data.value).toBe('ok')
  })
})
