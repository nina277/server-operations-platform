import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'
import UsersView from '../UsersView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { lastCall } from '@/test-utils/mock'
import type { CreateUserRequest, CurrentUser, ManagedUser, UserRole } from '@/types/auth'

const admin: CurrentUser = { id: 1, username: 'admin', role: 'OperatorAdmin', mfaEnabled: true }

const users: ManagedUser[] = [
  {
    id: 1,
    username: 'admin',
    role: 'OperatorAdmin',
    isActive: true,
    mfaEnabled: true,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
  },
  {
    id: 2,
    username: 'viewer',
    role: 'Viewer',
    isActive: true,
    mfaEnabled: true,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
  },
]

const fetchUsers = vi.fn<() => Promise<ManagedUser[]>>()
const createUser = vi.fn<(r: CreateUserRequest) => Promise<ManagedUser>>()
const updateUserRole = vi.fn<(id: number, role: UserRole) => Promise<ManagedUser>>()
const updateUserActive = vi.fn<(id: number, isActive: boolean) => Promise<ManagedUser>>()
const resetUserMfa = vi.fn<(id: number) => Promise<ManagedUser>>()

vi.mock('@/api/auth', () => ({
  login: vi.fn<() => Promise<never>>(),
  logout: vi.fn<() => Promise<void>>(),
  fetchCurrentUser: () => Promise.resolve(admin),
  fetchUsers: () => fetchUsers(),
  createUser: (r: CreateUserRequest) => createUser(r),
  updateUserRole: (id: number, role: UserRole) => updateUserRole(id, role),
  updateUserActive: (id: number, isActive: boolean) => updateUserActive(id, isActive),
  resetUserMfa: (id: number) => resetUserMfa(id),
}))

function apiError(code: string, message: string): AxiosError {
  const config = { headers: new AxiosHeaders() } as InternalAxiosRequestConfig
  const error = new AxiosError(message, 'ERR_BAD_REQUEST', config)
  error.response = {
    data: { success: false, data: null, error: { code, message }, traceId: null },
    status: 400,
    statusText: 'Error',
    headers: new AxiosHeaders(),
    config,
  }
  return error
}

async function mountView() {
  localStorage.setItem('sop.accessToken', 'access-1')
  localStorage.setItem('sop.refreshToken', 'refresh-1')
  setActivePinia(createPinia())

  const { useAuthStore } = await import('@/stores/auth')
  const auth = useAuthStore()
  await auth.loadCurrentUser()

  const wrapper = mount(UsersView, { global: { plugins: [createTestI18n()] } })
  await flushPromises()
  return wrapper
}

describe('UsersView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    fetchUsers.mockResolvedValue(users.map((u) => ({ ...u })))
    createUser.mockResolvedValue({ ...users[1]!, id: 3, username: 'new-user' })
    updateUserRole.mockImplementation((id, role) =>
      Promise.resolve({ ...users.find((u) => u.id === id)!, role }),
    )
    updateUserActive.mockImplementation((id, isActive) =>
      Promise.resolve({ ...users.find((u) => u.id === id)!, isActive }),
    )
    resetUserMfa.mockImplementation((id) =>
      Promise.resolve({ ...users.find((u) => u.id === id)!, mfaEnabled: false }),
    )
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)
  })

  it('利用者の一覧を出す', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('admin')
    expect(wrapper.text()).toContain('viewer')
  })

  it('削除できないことを画面で伝える', async () => {
    // 「消せない」と分かっていないと、無効化で止まっている理由が伝わらない
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('利用者は削除できません')
  })

  it('利用者を追加できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('#new-username').setValue('operator')
    await wrapper.get('#new-password').setValue('initial-password-1')
    await wrapper.get('#new-role').setValue('OperatorAdmin')
    await wrapper.get('[data-testid="user-form"]').trigger('submit')
    await flushPromises()

    expect(lastCall(createUser.mock.calls)[0]).toEqual({
      username: 'operator',
      password: 'initial-password-1',
      role: 'OperatorAdmin',
    })
  })

  it('追加後は初期パスワードを画面に残さない', async () => {
    const wrapper = await mountView()

    await wrapper.get('#new-username').setValue('operator')
    await wrapper.get('#new-password').setValue('initial-password-1')
    await wrapper.get('[data-testid="user-form"]').trigger('submit')
    await flushPromises()

    expect((wrapper.get('#new-password').element as HTMLInputElement).value).toBe('')
  })

  it('パスワードが短ければ追加できない', async () => {
    const wrapper = await mountView()

    await wrapper.get('#new-username').setValue('operator')
    await wrapper.get('#new-password').setValue('short')

    expect(wrapper.get('[data-testid="create-user"]').attributes('disabled')).toBeDefined()
  })

  it('役割を変更できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('#role-2').setValue('OperatorAdmin')
    await flushPromises()

    expect(updateUserRole).toHaveBeenCalledWith(2, 'OperatorAdmin')
  })

  it('自分自身の役割は画面でも変えられない', async () => {
    // APIでも拒否されるが、押せてしまうと理由が分かりにくい
    const wrapper = await mountView()

    expect(wrapper.get('#role-1').attributes('disabled')).toBeDefined()
  })

  it('自分自身は画面でも無効にできない', async () => {
    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="toggle-active-1"]').attributes('disabled')).toBeDefined()
  })

  it('他の利用者は無効にできる', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="toggle-active-2"]').trigger('click')
    await flushPromises()

    expect(updateUserActive).toHaveBeenCalledWith(2, false)
  })

  it('MFAのリセットは確認してから行う', async () => {
    // 対象のセッションがすべて切れるため、押し間違いで締め出さない
    vi.mocked(globalThis.confirm).mockReturnValue(false)

    const wrapper = await mountView()
    await wrapper.get('[data-testid="reset-mfa-2"]').trigger('click')
    await flushPromises()

    expect(resetUserMfa).not.toHaveBeenCalled()
  })

  it('確認すればMFAをリセットできる', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="reset-mfa-2"]').trigger('click')
    await flushPromises()

    expect(resetUserMfa).toHaveBeenCalledWith(2)
  })

  it('自分のMFAはこの画面からリセットできない', async () => {
    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="reset-mfa-1"]').exists()).toBe(false)
  })

  it('最後の管理者を降格しようとしたら理由を出す', async () => {
    updateUserRole.mockRejectedValue(
      apiError('last_admin', '有効な運用管理者が居なくなるため、この操作はできません。'),
    )

    const wrapper = await mountView()
    await wrapper.get('#role-2').setValue('Viewer')
    await flushPromises()

    expect(wrapper.text()).toContain('有効な運用管理者が居なくなるため')
  })
})
