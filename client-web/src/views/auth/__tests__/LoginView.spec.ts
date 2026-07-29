import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'
import { h } from 'vue'
import LoginView from '../LoginView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import type { CurrentUser, LoginRequest, TokenPair } from '@/types/auth'

const tokenPair: TokenPair = {
  accessToken: 'access-1',
  accessTokenExpiresAt: '2026-01-01T00:00:00Z',
  refreshToken: 'refresh-1',
  refreshTokenExpiresAt: '2026-02-01T00:00:00Z',
}

const viewer: CurrentUser = { id: 1, username: 'viewer', role: 'Viewer', mfaEnabled: false }

const login = vi.fn<(request: LoginRequest) => Promise<TokenPair>>()

vi.mock('@/api/auth', () => ({
  login: (request: LoginRequest) => login(request),
  logout: vi.fn<() => Promise<void>>(),
  fetchCurrentUser: () => Promise.resolve<CurrentUser>(viewer),
}))

/** APIが返す401をaxiosのエラーとして組み立てる。 */
function apiError(code: string, message: string): AxiosError {
  const config = { headers: new AxiosHeaders() } as InternalAxiosRequestConfig
  const error = new AxiosError(message, 'ERR_BAD_REQUEST', config)
  error.response = {
    data: { success: false, data: null, error: { code, message }, traceId: null },
    status: 401,
    statusText: 'Unauthorized',
    headers: new AxiosHeaders(),
    config,
  }
  return error
}

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/login', name: 'login', component: LoginView },
      { path: '/', name: 'dashboard', component: { render: () => h('div', 'dashboard') } },
      { path: '/incidents', name: 'incidents', component: { render: () => h('div', 'incidents') } },
    ],
  })
}

async function mountLogin(initialPath = '/login') {
  const router = createTestRouter()
  await router.push(initialPath)
  await router.isReady()

  const wrapper = mount(LoginView, {
    global: { plugins: [createTestI18n(), router] },
  })
  return { wrapper, router }
}

describe('LoginView', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    setActivePinia(createPinia())
    login.mockResolvedValue(tokenPair)
  })

  it('最初は認証コード欄を出さない', async () => {
    const { wrapper } = await mountLogin()

    expect(wrapper.find('[data-testid="totp-field"]').exists()).toBe(false)
  })

  it('ログインに成功したらダッシュボードへ移動する', async () => {
    const { wrapper, router } = await mountLogin()

    await wrapper.get('#username').setValue('viewer')
    await wrapper.get('#password').setValue('pw')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(login).toHaveBeenCalledWith({ username: 'viewer', password: 'pw', totpCode: undefined })
    expect(router.currentRoute.value.name).toBe('dashboard')
  })

  it('遷移元が指定されていればその画面へ戻す', async () => {
    const { wrapper, router } = await mountLogin('/login?redirect=/incidents')

    await wrapper.get('#username').setValue('viewer')
    await wrapper.get('#password').setValue('pw')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('incidents')
  })

  it('アプリ外への遷移指定は無視する', async () => {
    const { wrapper, router } = await mountLogin('/login?redirect=https://example.com/phish')

    await wrapper.get('#username').setValue('viewer')
    await wrapper.get('#password').setValue('pw')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('dashboard')
  })

  it('MFAが必要と返されたら認証コード欄を出す', async () => {
    const { wrapper } = await mountLogin()
    login.mockRejectedValue(apiError('mfa_required', 'MFAの認証コードを入力してください。'))

    await wrapper.get('#username').setValue('admin')
    await wrapper.get('#password').setValue('pw')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[data-testid="totp-field"]').isVisible()).toBe(true)
    expect(wrapper.get('[data-testid="login-error"]').text()).toContain('MFA')
  })

  it('認証コードを入れて再送信するとAPIへ渡す', async () => {
    const { wrapper } = await mountLogin()
    login.mockRejectedValueOnce(apiError('mfa_required', 'MFAの認証コードを入力してください。'))

    await wrapper.get('#username').setValue('admin')
    await wrapper.get('#password').setValue('pw')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    // 送信のたびにパスワード欄は空にするため、入力し直す
    await wrapper.get('#password').setValue('pw')
    await wrapper.get('#totpCode').setValue('123456')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(login).toHaveBeenLastCalledWith({
      username: 'admin',
      password: 'pw',
      totpCode: '123456',
    })
  })

  it('認証コードが誤っている場合はAPIの文言を出し、入力欄は残す', async () => {
    const { wrapper } = await mountLogin()
    login.mockRejectedValueOnce(apiError('mfa_required', 'MFAの認証コードを入力してください。'))

    await wrapper.get('#username').setValue('admin')
    await wrapper.get('#password').setValue('pw')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    login.mockRejectedValue(apiError('mfa_invalid_code', '認証コードが正しくありません。'))
    await wrapper.get('#password').setValue('pw')
    await wrapper.get('#totpCode').setValue('000000')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[data-testid="login-error"]').text()).toBe('認証コードが正しくありません。')
    expect(wrapper.find('[data-testid="totp-field"]').exists()).toBe(true)
  })

  it('失敗したらパスワード欄を空にする', async () => {
    const { wrapper } = await mountLogin()
    login.mockRejectedValue(
      apiError('invalid_credentials', 'ユーザー名またはパスワードが正しくありません。'),
    )

    await wrapper.get('#username').setValue('viewer')
    await wrapper.get('#password').setValue('wrong')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect((wrapper.get('#password').element as HTMLInputElement).value).toBe('')
    expect(wrapper.get('[data-testid="login-error"]').text()).toContain(
      'ユーザー名またはパスワードが正しくありません。',
    )
  })
})
