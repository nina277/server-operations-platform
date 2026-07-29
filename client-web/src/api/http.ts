import axios, { AxiosError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import type { ApiResponse } from '@/types/common'

/** リフレッシュ中にトークン更新を1回へまとめるための状態。 */
let refreshPromise: Promise<string | null> | null = null

/** 認証状態へアクセスするためのフック。循環参照を避けるため関数で受け取る。 */
interface AuthBridge {
  getAccessToken: () => string | null
  getRefreshToken: () => string | null
  onRefreshed: (accessToken: string, refreshToken: string) => void
  onSessionExpired: () => void
}

let authBridge: AuthBridge | null = null

export function registerAuthBridge(bridge: AuthBridge): void {
  authBridge = bridge
}

// 同一オリジンの /api 配下をnginx(本番)またはViteのdevプロキシ(開発)がAPIへ中継する
const http: AxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/',
  timeout: 15_000,
})

http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = authBridge?.getAccessToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

/** 認証エンドポイント自体は再試行の対象にしない(無限ループを防ぐ)。 */
function isAuthEndpoint(url: string | undefined): boolean {
  return url?.includes('/api/v1/auth/') ?? false
}

http.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ApiResponse<unknown>>) => {
    const original = error.config as
      | (InternalAxiosRequestConfig & { _retried?: boolean })
      | undefined

    if (
      error.response?.status !== 401 ||
      original === undefined ||
      original._retried === true ||
      isAuthEndpoint(original.url)
    ) {
      return Promise.reject(error)
    }

    original._retried = true

    // 複数リクエストが同時に401になっても、更新は1回だけ行う
    refreshPromise ??= refreshAccessToken()
    const newToken = await refreshPromise
    refreshPromise = null

    if (newToken === null) {
      authBridge?.onSessionExpired()
      return Promise.reject(error)
    }

    original.headers.Authorization = `Bearer ${newToken}`
    return http(original)
  },
)

async function refreshAccessToken(): Promise<string | null> {
  const refreshToken = authBridge?.getRefreshToken()
  if (!refreshToken) {
    return null
  }

  try {
    // インターセプターを経由しない生のクライアントで更新する
    const response = await axios.post<ApiResponse<{ accessToken: string; refreshToken: string }>>(
      '/api/v1/auth/refresh',
      { refreshToken },
      { baseURL: http.defaults.baseURL, timeout: 15_000 },
    )

    const data = response.data.data
    if (!data) {
      return null
    }

    authBridge?.onRefreshed(data.accessToken, data.refreshToken)
    return data.accessToken
  } catch {
    return null
  }
}

/** APIエラーから利用者向けメッセージを取り出す。 */
export function extractErrorMessage(error: unknown, fallback: string): string {
  if (axios.isAxiosError(error)) {
    const apiError = (error.response?.data as ApiResponse<unknown> | undefined)?.error
    if (apiError?.message) {
      return apiError.message
    }
  }
  return fallback
}

/** APIエラーコードを取り出す(MFA要求などの分岐に使う)。 */
export function extractErrorCode(error: unknown): string | null {
  if (axios.isAxiosError(error)) {
    return (error.response?.data as ApiResponse<unknown> | undefined)?.error?.code ?? null
  }
  return null
}

export default http
