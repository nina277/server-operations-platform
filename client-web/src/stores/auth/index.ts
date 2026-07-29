import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import * as authApi from '@/api/auth'
import { registerAuthBridge } from '@/api/http'
import type { CurrentUser, LoginRequest, UserRole } from '@/types/auth'

const ACCESS_TOKEN_KEY = 'sop.accessToken'
const REFRESH_TOKEN_KEY = 'sop.refreshToken'

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(localStorage.getItem(ACCESS_TOKEN_KEY))
  const refreshToken = ref<string | null>(localStorage.getItem(REFRESH_TOKEN_KEY))
  const currentUser = ref<CurrentUser | null>(null)
  const isSessionExpired = ref(false)

  const isAuthenticated = computed(() => accessToken.value !== null)
  const role = computed<UserRole | null>(() => currentUser.value?.role ?? null)
  const isAdmin = computed(() => role.value === 'OperatorAdmin')
  const mfaEnabled = computed(() => currentUser.value?.mfaEnabled ?? false)

  function setTokens(newAccessToken: string, newRefreshToken: string): void {
    accessToken.value = newAccessToken
    refreshToken.value = newRefreshToken
    localStorage.setItem(ACCESS_TOKEN_KEY, newAccessToken)
    localStorage.setItem(REFRESH_TOKEN_KEY, newRefreshToken)
    isSessionExpired.value = false
  }

  function clearTokens(): void {
    accessToken.value = null
    refreshToken.value = null
    currentUser.value = null
    localStorage.removeItem(ACCESS_TOKEN_KEY)
    localStorage.removeItem(REFRESH_TOKEN_KEY)
  }

  async function login(request: LoginRequest): Promise<void> {
    const pair = await authApi.login(request)
    setTokens(pair.accessToken, pair.refreshToken)
    await loadCurrentUser()
  }

  async function logout(): Promise<void> {
    const token = refreshToken.value
    clearTokens()

    if (token) {
      // 失敗しても画面側のログアウトは完了させる
      try {
        await authApi.logout(token)
      } catch {
        // 何もしない
      }
    }
  }

  async function loadCurrentUser(): Promise<void> {
    currentUser.value = await authApi.fetchCurrentUser()
  }

  /** 起動時にトークンがあれば利用者情報を復元する。 */
  async function restore(): Promise<void> {
    if (accessToken.value === null) {
      return
    }

    try {
      await loadCurrentUser()
    } catch {
      clearTokens()
    }
  }

  // axiosインターセプターへ認証状態を接続する
  registerAuthBridge({
    getAccessToken: () => accessToken.value,
    getRefreshToken: () => refreshToken.value,
    onRefreshed: setTokens,
    onSessionExpired: () => {
      clearTokens()
      isSessionExpired.value = true
    },
  })

  return {
    accessToken,
    refreshToken,
    currentUser,
    isSessionExpired,
    isAuthenticated,
    role,
    isAdmin,
    mfaEnabled,
    login,
    logout,
    loadCurrentUser,
    restore,
    clearTokens,
  }
})
