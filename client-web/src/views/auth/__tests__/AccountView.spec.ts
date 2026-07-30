import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'
import AccountView from '../AccountView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { nthCall } from '@/test-utils/mock'
import type {
  ChangePasswordRequest,
  ChangePasswordResult,
  CurrentUser,
  MfaSetupResult,
  MfaVerifyResult,
} from '@/types/auth'

const viewerWithoutMfa: CurrentUser = {
  id: 1,
  username: 'admin',
  role: 'OperatorAdmin',
  mfaEnabled: false,
}

let currentUser: CurrentUser = viewerWithoutMfa

const setupMfa = vi.fn<() => Promise<MfaSetupResult>>()
const verifyMfa = vi.fn<(code: string) => Promise<MfaVerifyResult>>()
const changePassword = vi.fn<(r: ChangePasswordRequest) => Promise<ChangePasswordResult>>()

vi.mock('@/api/auth', () => ({
  login: vi.fn<() => Promise<never>>(),
  logout: vi.fn<() => Promise<void>>(),
  fetchCurrentUser: () => Promise.resolve(currentUser),
  setupMfa: () => setupMfa(),
  verifyMfa: (code: string) => verifyMfa(code),
  changePassword: (r: ChangePasswordRequest) => changePassword(r),
}))

// QRコードの描画はjsdomのcanvasに依存するため、呼び出しの検証だけを行う
const toCanvas = vi.fn<() => Promise<void>>()
vi.mock('qrcode', () => ({ toCanvas: () => toCanvas() }))

function apiError(code: string, message: string, status = 400): AxiosError {
  const config = { headers: new AxiosHeaders() } as InternalAxiosRequestConfig
  const error = new AxiosError(message, 'ERR_BAD_REQUEST', config)
  error.response = {
    data: { success: false, data: null, error: { code, message }, traceId: null },
    status,
    statusText: 'Error',
    headers: new AxiosHeaders(),
    config,
  }
  return error
}

async function mountView() {
  const wrapper = mount(AccountView, {
    global: { plugins: [createTestI18n()] },
  })
  await flushPromises()
  return wrapper
}

describe('AccountView', () => {
  beforeEach(() => {
    localStorage.setItem('sop.accessToken', 'access-1')
    localStorage.setItem('sop.refreshToken', 'refresh-1')
    vi.clearAllMocks()
    setActivePinia(createPinia())
    currentUser = viewerWithoutMfa

    setupMfa.mockResolvedValue({
      secret: 'JBSWY3DPEHPK3PXP',
      otpAuthUri: 'otpauth://totp/ServerOps:admin?secret=JBSWY3DPEHPK3PXP&issuer=ServerOps',
    })
    verifyMfa.mockResolvedValue({ mfaEnabled: true, verifiedAt: '2026-07-10T12:00:00Z' })
    changePassword.mockResolvedValue({
      changedAt: '2026-07-10T12:00:00Z',
      otherSessionsRevoked: true,
    })
    toCanvas.mockResolvedValue()
  })

  // --- MFA ---

  it('MFAが未設定であることを示す', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('未設定')
  })

  it('管理操作にMFAが必要であることを画面で伝える', async () => {
    // これを知らせないと、管理者が403の理由に気づけない
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('MFAを設定して直近で認証していることが必要')
  })

  it('設定を始めるとシークレットとQRを出す', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="mfa-setup"]').trigger('click')
    await flushPromises()

    expect(setupMfa).toHaveBeenCalledTimes(1)
    expect((wrapper.get('#mfa-secret').element as HTMLInputElement).value).toBe('JBSWY3DPEHPK3PXP')
    expect(toCanvas).toHaveBeenCalledTimes(1)
  })

  it('コードを確認するとMFAが有効になる', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="mfa-setup"]').trigger('click')
    await flushPromises()

    currentUser = { ...viewerWithoutMfa, mfaEnabled: true }
    await wrapper.get('#mfa-code').setValue('123456')
    await wrapper.get('[data-testid="mfa-verify"]').trigger('click')
    await flushPromises()

    expect(verifyMfa).toHaveBeenCalledWith('123456')
    expect(wrapper.get('[data-testid="mfa-message"]').text()).toContain('有効にしました')
  })

  it('有効にしたらシークレットを画面から消す', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="mfa-setup"]').trigger('click')
    await flushPromises()
    currentUser = { ...viewerWithoutMfa, mfaEnabled: true }
    await wrapper.get('#mfa-code').setValue('123456')
    await wrapper.get('[data-testid="mfa-verify"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('#mfa-secret').exists()).toBe(false)
  })

  it('コードが誤っていれば理由を出す', async () => {
    verifyMfa.mockRejectedValue(apiError('mfa_invalid_code', '認証コードが正しくありません。', 401))

    const wrapper = await mountView()
    await wrapper.get('[data-testid="mfa-setup"]').trigger('click')
    await flushPromises()
    await wrapper.get('#mfa-code').setValue('000000')
    await wrapper.get('[data-testid="mfa-verify"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="mfa-error"]').text()).toBe('認証コードが正しくありません。')
  })

  it('設定済みなら再設定として案内する', async () => {
    currentUser = { ...viewerWithoutMfa, mfaEnabled: true }

    const wrapper = await mountView()

    expect(wrapper.text()).toContain('設定済み')
    expect(wrapper.get('[data-testid="mfa-setup"]').text()).toBe('MFAを再設定する')
  })

  // --- パスワード ---

  it('12文字未満では保存できない', async () => {
    const wrapper = await mountView()

    await wrapper.get('#current-password').setValue('current-password')
    await wrapper.get('#new-password').setValue('short')
    await wrapper.get('#confirm-password').setValue('short')

    const submit = wrapper.findAll('button').find((b) => b.text() === '保存')
    expect(submit?.attributes('disabled')).toBeDefined()
  })

  it('確認用が一致しないと保存できない', async () => {
    const wrapper = await mountView()

    await wrapper.get('#current-password').setValue('current-password')
    await wrapper.get('#new-password').setValue('brand-new-password')
    await wrapper.get('#confirm-password').setValue('different-password')

    const submit = wrapper.findAll('button').find((b) => b.text() === '保存')
    expect(submit?.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('一致していません')
  })

  it('入力が揃えばパスワードを変更できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('#current-password').setValue('current-password')
    await wrapper.get('#new-password').setValue('brand-new-password')
    await wrapper.get('#confirm-password').setValue('brand-new-password')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(nthCall(changePassword.mock.calls, 0)[0]).toEqual({
      currentPassword: 'current-password',
      newPassword: 'brand-new-password',
    })
  })

  it('変更後は入力欄に値を残さない', async () => {
    const wrapper = await mountView()

    await wrapper.get('#current-password').setValue('current-password')
    await wrapper.get('#new-password').setValue('brand-new-password')
    await wrapper.get('#confirm-password').setValue('brand-new-password')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect((wrapper.get('#current-password').element as HTMLInputElement).value).toBe('')
    expect((wrapper.get('#new-password').element as HTMLInputElement).value).toBe('')
    expect((wrapper.get('#confirm-password').element as HTMLInputElement).value).toBe('')
  })

  it('他の端末のログインが解除されることを事前に伝える', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('他の端末のログインは解除されます')
  })

  it('現在のパスワードが違えば理由を出し、その欄を空にする', async () => {
    changePassword.mockRejectedValue(
      apiError('invalid_current_password', '現在のパスワードが正しくありません。', 401),
    )

    const wrapper = await mountView()
    await wrapper.get('#current-password').setValue('wrong-password')
    await wrapper.get('#new-password').setValue('brand-new-password')
    await wrapper.get('#confirm-password').setValue('brand-new-password')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[data-testid="password-error"]').text()).toBe(
      '現在のパスワードが正しくありません。',
    )
    expect((wrapper.get('#current-password').element as HTMLInputElement).value).toBe('')
  })
})
