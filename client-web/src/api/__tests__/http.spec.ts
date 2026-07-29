import { describe, it, expect, beforeEach, vi } from 'vitest'
import axios, {
  AxiosError,
  AxiosHeaders,
  type AxiosAdapter,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios'

/**
 * モジュール内部の状態は呼び出しごとに初期化されるため(更新中フラグは完了時にnullへ戻り、
 * 認証フックは登録のたびに置き換わる)、テスト間で読み込み直す必要はない。
 */
async function loadHttp() {
  return await import('../http')
}

function ok(config: InternalAxiosRequestConfig, data: unknown = {}): AxiosResponse {
  return {
    data,
    status: 200,
    statusText: 'OK',
    headers: new AxiosHeaders(),
    config,
  }
}

function unauthorized(config: InternalAxiosRequestConfig, code = 'unauthorized'): AxiosError {
  const error = new AxiosError('Unauthorized', 'ERR_BAD_REQUEST', config)
  error.response = {
    data: {
      success: false,
      data: null,
      error: { code, message: '権限がありません。' },
      traceId: null,
    },
    status: 401,
    statusText: 'Unauthorized',
    headers: new AxiosHeaders(),
    config,
  }
  return error
}

describe('HTTPクライアント', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('アクセストークンをAuthorizationヘッダーへ付ける', async () => {
    const { default: http, registerAuthBridge } = await loadHttp()
    const seen: string[] = []

    http.defaults.adapter = ((config: InternalAxiosRequestConfig) => {
      seen.push(String(config.headers.Authorization))
      return Promise.resolve(ok(config))
    }) as AxiosAdapter

    registerAuthBridge({
      getAccessToken: () => 'access-1',
      getRefreshToken: () => 'refresh-1',
      onRefreshed: () => {},
      onSessionExpired: () => {},
    })

    await http.get('/api/v1/targets')
    expect(seen).toEqual(['Bearer access-1'])
  })

  it('401を受けたらトークンを更新して1回だけ再試行する', async () => {
    const { default: http, registerAuthBridge } = await loadHttp()
    const onRefreshed = vi.fn<(access: string, refresh: string) => void>()
    let accessToken = 'expired'
    let protectedCalls = 0

    const adapter = ((config: InternalAxiosRequestConfig) => {
      if (config.url === '/api/v1/auth/refresh') {
        return Promise.resolve(
          ok(config, {
            success: true,
            data: { accessToken: 'access-2', refreshToken: 'refresh-2' },
            error: null,
            traceId: null,
          }),
        )
      }

      protectedCalls += 1
      if (config.headers.Authorization === 'Bearer access-2') {
        return Promise.resolve(ok(config, { success: true, data: [], error: null, traceId: null }))
      }
      return Promise.reject(unauthorized(config))
    }) as AxiosAdapter

    http.defaults.adapter = adapter
    axios.defaults.adapter = adapter

    registerAuthBridge({
      getAccessToken: () => accessToken,
      getRefreshToken: () => 'refresh-1',
      onRefreshed: (newAccess, newRefresh) => {
        accessToken = newAccess
        onRefreshed(newAccess, newRefresh)
      },
      onSessionExpired: () => {},
    })

    const response = await http.get('/api/v1/targets')

    expect(response.status).toBe(200)
    expect(protectedCalls).toBe(2)
    expect(onRefreshed).toHaveBeenCalledWith('access-2', 'refresh-2')
  })

  it('同時に401になっても更新は1回にまとめる', async () => {
    const { default: http, registerAuthBridge } = await loadHttp()
    let refreshCalls = 0
    let accessToken = 'expired'

    const adapter = ((config: InternalAxiosRequestConfig) => {
      if (config.url === '/api/v1/auth/refresh') {
        refreshCalls += 1
        return Promise.resolve(
          ok(config, {
            success: true,
            data: { accessToken: 'access-2', refreshToken: 'refresh-2' },
            error: null,
            traceId: null,
          }),
        )
      }
      if (config.headers.Authorization === 'Bearer access-2') {
        return Promise.resolve(ok(config))
      }
      return Promise.reject(unauthorized(config))
    }) as AxiosAdapter

    http.defaults.adapter = adapter
    axios.defaults.adapter = adapter

    registerAuthBridge({
      getAccessToken: () => accessToken,
      getRefreshToken: () => 'refresh-1',
      onRefreshed: (newAccess) => {
        accessToken = newAccess
      },
      onSessionExpired: () => {},
    })

    await Promise.all([
      http.get('/api/v1/targets'),
      http.get('/api/v1/incidents'),
      http.get('/api/v1/notifications'),
    ])

    expect(refreshCalls).toBe(1)
  })

  it('更新に失敗したらセッション切れとして扱い、再試行しない', async () => {
    const { default: http, registerAuthBridge } = await loadHttp()
    const onSessionExpired = vi.fn<() => void>()
    let protectedCalls = 0

    const adapter = ((config: InternalAxiosRequestConfig) => {
      if (config.url === '/api/v1/auth/refresh') {
        return Promise.reject(unauthorized(config, 'invalid_refresh_token'))
      }
      protectedCalls += 1
      return Promise.reject(unauthorized(config))
    }) as AxiosAdapter

    http.defaults.adapter = adapter
    axios.defaults.adapter = adapter

    registerAuthBridge({
      getAccessToken: () => 'expired',
      getRefreshToken: () => 'refresh-1',
      onRefreshed: () => {},
      onSessionExpired,
    })

    await expect(http.get('/api/v1/targets')).rejects.toThrow('Unauthorized')
    expect(protectedCalls).toBe(1)
    expect(onSessionExpired).toHaveBeenCalledTimes(1)
  })

  it('ログイン自体の401では更新を試みない', async () => {
    const { default: http, registerAuthBridge } = await loadHttp()
    let refreshCalls = 0

    const adapter = ((config: InternalAxiosRequestConfig) => {
      if (config.url === '/api/v1/auth/refresh') {
        refreshCalls += 1
      }
      return Promise.reject(unauthorized(config, 'mfa_required'))
    }) as AxiosAdapter

    http.defaults.adapter = adapter
    axios.defaults.adapter = adapter

    registerAuthBridge({
      getAccessToken: () => null,
      getRefreshToken: () => 'refresh-1',
      onRefreshed: () => {},
      onSessionExpired: () => {},
    })

    await expect(http.post('/api/v1/auth/login', {})).rejects.toThrow('Unauthorized')
    expect(refreshCalls).toBe(0)
  })

  it('APIエラーからコードとメッセージを取り出す', async () => {
    const { extractErrorCode, extractErrorMessage } = await loadHttp()
    const config = { headers: new AxiosHeaders() } as InternalAxiosRequestConfig
    const error = unauthorized(config, 'mfa_required')

    expect(extractErrorCode(error)).toBe('mfa_required')
    expect(extractErrorMessage(error, '既定')).toBe('権限がありません。')
    expect(extractErrorCode(new Error('plain'))).toBeNull()
    expect(extractErrorMessage(new Error('plain'), '既定')).toBe('既定')
  })
})
