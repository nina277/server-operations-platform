import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { h } from 'vue'
import TargetDetailView from '../TargetDetailView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { nthCall } from '@/test-utils/mock'
import type { AdapterTemplate, Target, UpdateTargetRequest } from '@/types/operations'
import type { CurrentUser } from '@/types/auth'

const target: Target = {
  id: 3,
  name: 'home-docker',
  templateId: 'docker',
  description: null,
  isEnabled: true,
  autoRecoveryEnabled: false,
  allowedContainers: ['nextcloud-app'],
  settings: { socketPath: '/var/run/docker.sock' },
  configuredCredentials: ['apiToken'],
  createdAt: '2026-07-01T00:00:00Z',
  updatedAt: '2026-07-01T00:00:00Z',
}

const template: AdapterTemplate = {
  id: 'docker',
  name: 'Docker',
  description: 'Docker監視',
  inputs: [
    {
      key: 'socketPath',
      label: 'ソケットパス',
      type: 'string',
      required: true,
      secret: false,
      description: 'Dockerソケットの場所',
      defaultValue: null,
    },
    {
      key: 'apiToken',
      label: 'APIトークン',
      type: 'string',
      required: false,
      secret: true,
      description: '認証用トークン',
      defaultValue: null,
    },
  ],
  recommendedMonitors: [],
  initialRules: [],
  allowedOperations: ['RESTART_ALLOWED_CONTAINER'],
  capabilities: ['container'],
}

const admin: CurrentUser = { id: 1, username: 'admin', role: 'OperatorAdmin', mfaEnabled: true }

const updateTarget = vi.fn<(id: number, request: UpdateTargetRequest) => Promise<Target>>()

vi.mock('@/api/operations', () => ({
  fetchTarget: () => Promise.resolve(target),
  fetchTargetCapabilities: () =>
    Promise.resolve({
      targetId: 3,
      templateId: 'docker',
      capabilities: ['container'],
      allowedOperations: ['RESTART_ALLOWED_CONTAINER'],
      recommendedMonitors: [],
      initialRules: [],
    }),
  fetchAdapterTemplates: () => Promise.resolve([template]),
  fetchTargetMetrics: () => Promise.resolve([]),
  fetchTargetLogs: () => Promise.resolve([]),
  updateTarget: (id: number, request: UpdateTargetRequest) => updateTarget(id, request),
  testTargetConnection: vi.fn<() => Promise<never>>(),
  runHealthCheck: vi.fn<() => Promise<never>>(),
}))

vi.mock('@/api/auth', () => ({
  login: vi.fn<() => Promise<never>>(),
  logout: vi.fn<() => Promise<void>>(),
  fetchCurrentUser: () => Promise.resolve(admin),
}))

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/targets/:id', name: 'target-detail', component: TargetDetailView },
      { path: '/', name: 'dashboard', component: { render: () => h('div') } },
    ],
  })
}

async function mountView() {
  const router = createTestRouter()
  await router.push('/targets/3')
  await router.isReady()

  const wrapper = mount(TargetDetailView, {
    global: { plugins: [createTestI18n(), router] },
  })
  await flushPromises()
  return wrapper
}

describe('TargetDetailView', () => {
  beforeEach(async () => {
    localStorage.setItem('sop.accessToken', 'access-1')
    localStorage.setItem('sop.refreshToken', 'refresh-1')
    vi.clearAllMocks()
    setActivePinia(createPinia())
    updateTarget.mockResolvedValue(target)

    const { useAuthStore } = await import('@/stores/auth')
    await useAuthStore().loadCurrentUser()
  })

  it('設定済みの秘密値は入力欄にも表示しない', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('設定済み(値は表示されません)')
    expect(wrapper.find('#credential-apiToken').exists()).toBe(false)
  })

  it('許可コンテナは1行1件として送り、空行は除く', async () => {
    const wrapper = await mountView()

    await wrapper.get('#target-containers').setValue('nextcloud-app\n\n  nextcloud-db  \n\n')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(updateTarget).toHaveBeenCalledTimes(1)
    expect(nthCall(updateTarget.mock.calls, 0)[1].allowedContainers).toEqual([
      'nextcloud-app',
      'nextcloud-db',
    ])
  })

  it('許可コンテナを空にすると空の一覧を送る(どのコンテナも操作させない)', async () => {
    const wrapper = await mountView()

    await wrapper.get('#target-containers').setValue('')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(nthCall(updateTarget.mock.calls, 0)[1].allowedContainers).toEqual([])
  })

  it('入力しなかった秘密値は送らない(既存の値を維持する)', async () => {
    const wrapper = await mountView()

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(nthCall(updateTarget.mock.calls, 0)[1].credentials).toEqual({})
  })

  it('入力した秘密値だけを送る', async () => {
    const wrapper = await mountView()

    // 「値を変更する」を押してから入力する
    const replace = wrapper.findAll('button').find((b) => b.text() === '値を変更する')
    await replace?.trigger('click')
    await wrapper.get('#credential-apiToken').setValue('new-token')

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(nthCall(updateTarget.mock.calls, 0)[1].credentials).toEqual({ apiToken: 'new-token' })
  })

  it('自動復旧の切り替えを保存できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('#target-auto-recovery').setValue(true)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(nthCall(updateTarget.mock.calls, 0)[1].autoRecoveryEnabled).toBe(true)
  })

  it('自動復旧の説明で低危険度のみ自動実行されることを示す', async () => {
    const wrapper = await mountView()

    expect(wrapper.get('#target-auto-recovery-help').text()).toContain('危険度が低い操作')
  })
})
