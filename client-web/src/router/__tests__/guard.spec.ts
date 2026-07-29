import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import router from '../index'
import { useAuthStore } from '@/stores/auth'
import type { CurrentUser } from '@/types/auth'

const viewer: CurrentUser = { id: 1, username: 'viewer', role: 'Viewer', mfaEnabled: false }
const admin: CurrentUser = { id: 2, username: 'admin', role: 'OperatorAdmin', mfaEnabled: true }

const fetchCurrentUser = vi.fn<() => Promise<CurrentUser>>()

vi.mock('@/api/auth', () => ({
  login: vi.fn<() => Promise<never>>(),
  logout: vi.fn<() => Promise<void>>(),
  fetchCurrentUser: () => fetchCurrentUser(),
}))

/**
 * ログイン済みの状態を作る。
 * ストアは生成時にlocalStorageを読むため、トークンを置いてから作り直す。
 */
function signIn(): void {
  localStorage.setItem('sop.accessToken', 'access-1')
  localStorage.setItem('sop.refreshToken', 'refresh-1')
  setActivePinia(createPinia())
}

describe('経路ガード', () => {
  beforeEach(async () => {
    localStorage.clear()
    vi.clearAllMocks()
    fetchCurrentUser.mockResolvedValue(viewer)
    setActivePinia(createPinia())
    // 各テストを共通の地点から始める
    await router.replace('/login').catch(() => {})
  })

  it('未ログインで保護された画面を開くとログインへ送る', async () => {
    await router.push('/incidents')

    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/incidents')
  })

  it('ログイン済みなら保護された画面を開ける', async () => {
    signIn()
    await router.push('/incidents')

    expect(router.currentRoute.value.name).toBe('incidents')
  })

  it('再読み込み直後でも役割を取得してから判定する', async () => {
    signIn()
    await router.push('/settings')

    expect(fetchCurrentUser).toHaveBeenCalledTimes(1)
    expect(router.currentRoute.value.name).toBe('forbidden')
  })

  it('管理者だけの画面は管理者なら開ける', async () => {
    signIn()
    fetchCurrentUser.mockResolvedValue(admin)
    await router.push('/settings')

    expect(router.currentRoute.value.name).toBe('settings')
  })

  it('閲覧者は監査ログを開けない', async () => {
    signIn()
    await router.push('/audit-logs')

    expect(router.currentRoute.value.name).toBe('forbidden')
  })

  it('利用者情報の取得に失敗したらトークンを破棄してログインへ送る', async () => {
    signIn()
    fetchCurrentUser.mockRejectedValue(new Error('401'))

    await router.push('/incidents')

    expect(router.currentRoute.value.name).toBe('login')
    expect(useAuthStore().isAuthenticated).toBe(false)
    expect(localStorage.getItem('sop.accessToken')).toBeNull()
  })

  it('ログイン済みでログイン画面を開いたらダッシュボードへ送る', async () => {
    signIn()
    await router.push('/')
    await router.push('/login')

    expect(router.currentRoute.value.name).toBe('dashboard')
  })

  it('存在しない経路は未ログインでも404を表示する', async () => {
    await router.push('/no-such-page')

    expect(router.currentRoute.value.name).toBe('not-found')
  })
})
