import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '../index'
import type { CurrentUser, TokenPair } from '@/types/auth'

const tokenPair: TokenPair = {
  accessToken: 'access-1',
  accessTokenExpiresAt: '2026-01-01T00:00:00Z',
  refreshToken: 'refresh-1',
  refreshTokenExpiresAt: '2026-02-01T00:00:00Z',
}

const viewer: CurrentUser = { id: 1, username: 'viewer', role: 'Viewer', mfaEnabled: false }
const admin: CurrentUser = { id: 2, username: 'admin', role: 'OperatorAdmin', mfaEnabled: true }

const login = vi.fn<() => Promise<TokenPair>>()
const logout = vi.fn<() => Promise<void>>()
const fetchCurrentUser = vi.fn<() => Promise<CurrentUser>>()

vi.mock('@/api/auth', () => ({
  login: (...args: unknown[]) => login(...(args as [])),
  logout: (...args: unknown[]) => logout(...(args as [])),
  fetchCurrentUser: () => fetchCurrentUser(),
}))

describe('認証ストア', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    setActivePinia(createPinia())
    login.mockResolvedValue(tokenPair)
    logout.mockResolvedValue()
    fetchCurrentUser.mockResolvedValue(viewer)
  })

  it('ログインするとトークンを保存し利用者情報を読み込む', async () => {
    const auth = useAuthStore()
    await auth.login({ username: 'viewer', password: 'pw' })

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.currentUser).toEqual(viewer)
    expect(localStorage.getItem('sop.accessToken')).toBe('access-1')
    expect(localStorage.getItem('sop.refreshToken')).toBe('refresh-1')
  })

  it('役割から管理者かどうかを判定する', async () => {
    const auth = useAuthStore()
    fetchCurrentUser.mockResolvedValue(admin)
    await auth.login({ username: 'admin', password: 'pw' })

    expect(auth.isAdmin).toBe(true)
    expect(auth.mfaEnabled).toBe(true)
  })

  it('ログアウトでトークンと利用者情報を消す', async () => {
    const auth = useAuthStore()
    await auth.login({ username: 'viewer', password: 'pw' })
    await auth.logout()

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.currentUser).toBeNull()
    expect(localStorage.getItem('sop.accessToken')).toBeNull()
  })

  it('ログアウトAPIが失敗しても画面側のログアウトは完了する', async () => {
    const auth = useAuthStore()
    await auth.login({ username: 'viewer', password: 'pw' })
    logout.mockRejectedValue(new Error('network'))

    await expect(auth.logout()).resolves.toBeUndefined()
    expect(auth.isAuthenticated).toBe(false)
  })

  it('保存済みトークンから利用者情報を復元する', async () => {
    localStorage.setItem('sop.accessToken', 'access-1')
    localStorage.setItem('sop.refreshToken', 'refresh-1')

    const auth = useAuthStore()
    await auth.restore()

    expect(auth.currentUser).toEqual(viewer)
  })

  it('復元に失敗したトークンは破棄する', async () => {
    localStorage.setItem('sop.accessToken', 'stale')
    localStorage.setItem('sop.refreshToken', 'stale')
    fetchCurrentUser.mockRejectedValue(new Error('401'))

    const auth = useAuthStore()
    await auth.restore()

    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('sop.accessToken')).toBeNull()
  })

  it('トークンがなければ復元でAPIを呼ばない', async () => {
    const auth = useAuthStore()
    await auth.restore()

    expect(fetchCurrentUser).not.toHaveBeenCalled()
  })
})
