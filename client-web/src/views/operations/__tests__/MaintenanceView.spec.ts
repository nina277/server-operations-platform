import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'
import MaintenanceView from '../MaintenanceView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { lastCall } from '@/test-utils/mock'
import type {
  CreateMaintenanceWindowRequest,
  MaintenanceWindow,
  Target,
} from '@/types/operations'

const activeWindow: MaintenanceWindow = {
  id: 1,
  targetId: null,
  targetName: null,
  reason: 'ホストのカーネル更新',
  startsAt: '2026-07-10T12:00:00Z',
  endsAt: '2026-07-10T14:00:00Z',
  suppressNotifications: true,
  suppressAutoRecovery: true,
  cancelledAt: null,
  isActive: true,
  createdAt: '2026-07-10T11:00:00Z',
}

const target: Target = {
  id: 5,
  name: 'docker1',
  templateId: 'docker-host',
  description: null,
  isEnabled: true,
  autoRecoveryEnabled: false,
  allowedContainers: [],
  collectionIntervalSeconds: null,
  settings: {},
  configuredCredentials: [],
  createdAt: '2026-07-01T00:00:00Z',
  updatedAt: '2026-07-01T00:00:00Z',
}

const fetchMaintenanceWindows = vi.fn<() => Promise<MaintenanceWindow[]>>()
const createMaintenanceWindow =
  vi.fn<(r: CreateMaintenanceWindowRequest) => Promise<MaintenanceWindow>>()
const cancelMaintenanceWindow = vi.fn<(id: number) => Promise<MaintenanceWindow>>()
const fetchTargets = vi.fn<() => Promise<Target[]>>()

vi.mock('@/api/operations', () => ({
  fetchMaintenanceWindows: () => fetchMaintenanceWindows(),
  createMaintenanceWindow: (r: CreateMaintenanceWindowRequest) => createMaintenanceWindow(r),
  cancelMaintenanceWindow: (id: number) => cancelMaintenanceWindow(id),
  fetchTargets: () => fetchTargets(),
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
  const wrapper = mount(MaintenanceView, { global: { plugins: [createTestI18n()] } })
  await flushPromises()
  return wrapper
}

describe('MaintenanceView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())

    fetchMaintenanceWindows.mockResolvedValue([activeWindow])
    fetchTargets.mockResolvedValue([target])
    createMaintenanceWindow.mockResolvedValue(activeWindow)
    cancelMaintenanceWindow.mockResolvedValue({
      ...activeWindow,
      cancelledAt: '2026-07-10T13:00:00Z',
      isActive: false,
    })
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)
  })

  it('検知は止めないことを画面で伝える', async () => {
    // 抑止の範囲を誤解すると、期間中に本当の障害を見落としたと思い込む
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('検知そのものは止めない')
  })

  it('進行中の期間を進行中として示す', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('進行中')
    expect(wrapper.text()).toContain('ホストのカーネル更新')
  })

  it('対象を指定しない期間はすべての対象として示す', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('すべての監視対象')
  })

  it('期間を登録できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('#mw-reason').setValue('ディスク交換')
    await wrapper.get('[data-testid="maintenance-form"]').trigger('submit')
    await flushPromises()

    expect(lastCall(createMaintenanceWindow.mock.calls)[0].reason).toBe('ディスク交換')
  })

  it('対象を選ばなければ全対象として送る', async () => {
    // 空文字を数値に変換すると0になり、存在しない対象を指したことになる
    const wrapper = await mountView()

    await wrapper.get('#mw-reason').setValue('全体の停止')
    await wrapper.get('[data-testid="maintenance-form"]').trigger('submit')
    await flushPromises()

    expect(lastCall(createMaintenanceWindow.mock.calls)[0].targetId).toBeNull()
  })

  it('対象を選べばそのIDを送る', async () => {
    const wrapper = await mountView()

    await wrapper.get('#mw-reason').setValue('この対象だけ')
    await wrapper.get('#mw-target').setValue('5')
    await wrapper.get('[data-testid="maintenance-form"]').trigger('submit')
    await flushPromises()

    expect(lastCall(createMaintenanceWindow.mock.calls)[0].targetId).toBe(5)
  })

  it('理由が空なら保存できない', async () => {
    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="create-window"]').attributes('disabled')).toBeDefined()
  })

  it('どちらも止めない設定では保存できない', async () => {
    // 登録しても効かない設定を送らせない
    const wrapper = await mountView()

    await wrapper.get('#mw-reason').setValue('何も止めない')
    await wrapper.get('[data-testid="suppress-notifications"]').setValue(false)
    await wrapper.get('[data-testid="suppress-auto-recovery"]').setValue(false)

    expect(wrapper.get('[data-testid="create-window"]').attributes('disabled')).toBeDefined()
  })

  it('片方だけ止める設定なら保存できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('#mw-reason').setValue('通知だけ止める')
    await wrapper.get('[data-testid="suppress-auto-recovery"]').setValue(false)

    expect(wrapper.get('[data-testid="create-window"]').attributes('disabled')).toBeUndefined()
  })

  it('取り消しは確認してから行う', async () => {
    // 取り消すと通知と自動復旧が戻るため、押し間違いで解除させない
    const wrapper = await mountView()

    vi.mocked(globalThis.confirm).mockReturnValue(false)
    const cancelButton = wrapper.findAll('button').find((b) => b.text() === '取り消す')
    await cancelButton?.trigger('click')
    await flushPromises()

    expect(cancelMaintenanceWindow).not.toHaveBeenCalled()
  })

  it('確認すれば取り消せる', async () => {
    const wrapper = await mountView()

    const cancelButton = wrapper.findAll('button').find((b) => b.text() === '取り消す')
    await cancelButton?.trigger('click')
    await flushPromises()

    expect(cancelMaintenanceWindow).toHaveBeenCalledWith(1)
  })

  it('保存が拒否されたら理由を出す', async () => {
    createMaintenanceWindow.mockRejectedValue(
      apiError('maintenance_in_past', 'すでに終了した期間は登録できません。'),
    )

    const wrapper = await mountView()
    await wrapper.get('#mw-reason').setValue('過去の期間')
    await wrapper.get('[data-testid="maintenance-form"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('すでに終了した期間は登録できません。')
  })

  it('対象一覧が取れなくても期間の一覧は出す', async () => {
    fetchTargets.mockRejectedValue(new Error('failed'))

    const wrapper = await mountView()

    expect(wrapper.text()).toContain('ホストのカーネル更新')
  })
})
