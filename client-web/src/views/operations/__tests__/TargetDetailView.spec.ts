import { describe, it, expect, beforeEach, vi } from 'vitest'
import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { h } from 'vue'
import TargetDetailView from '../TargetDetailView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { nthCall } from '@/test-utils/mock'
import type {
  AdapterTemplate,
  MetricSnapshot,
  Target,
  TargetDeletePreview,
  UpdateTargetRequest,
} from '@/types/operations'
import type { CurrentUser } from '@/types/auth'

const target: Target = {
  id: 3,
  name: 'home-docker',
  templateId: 'docker',
  description: null,
  isEnabled: true,
  autoRecoveryEnabled: false,
  allowedContainers: ['nextcloud-app'],
  collectionIntervalSeconds: null,
  enabledMonitors: ['container-state', 'log-excerpt'],
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
  collectableMonitors: ['container-state', 'log-excerpt'],
  initialRules: [],
  allowedOperations: ['RESTART_ALLOWED_CONTAINER'],
  capabilities: ['container'],
}

const admin: CurrentUser = { id: 1, username: 'admin', role: 'OperatorAdmin', mfaEnabled: true }

const updateTarget = vi.fn<(id: number, request: UpdateTargetRequest) => Promise<Target>>()

const fetchTarget = vi.fn<() => Promise<Target>>()
const previewDeleteTarget = vi.fn<() => Promise<TargetDeletePreview>>()
const deleteTarget = vi.fn<(id: number) => Promise<void>>()
const fetchTargetMetrics = vi.fn<() => Promise<MetricSnapshot[]>>()

vi.mock('@/api/operations', () => ({
  fetchTarget: () => fetchTarget(),
  fetchTargetCapabilities: () =>
    Promise.resolve({
      targetId: 3,
      templateId: 'docker',
      capabilities: ['container'],
      allowedOperations: ['RESTART_ALLOWED_CONTAINER'],
      recommendedMonitors: [],
      collectableMonitors: ['container-state', 'log-excerpt'],
      initialRules: [],
    }),
  fetchAdapterTemplates: () => Promise.resolve([template]),
  fetchTargetMetrics: () => fetchTargetMetrics(),
  previewDeleteTarget: () => previewDeleteTarget(),
  deleteTarget: (id: number) => deleteTarget(id),
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
    fetchTarget.mockResolvedValue(target)
    fetchTargetMetrics.mockResolvedValue([])
    previewDeleteTarget.mockResolvedValue({
      targetId: 3,
      targetName: 'home-docker',
      metricSnapshots: 120,
      incidents: 3,
      incidentLogs: 40,
      diagnoses: 3,
      recoveryActions: 2,
      healthChecks: 5,
      notifications: 1,
      maintenanceWindows: 0,
      total: 174,
    })
    deleteTarget.mockResolvedValue()
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

  // --- 収集間隔 ---

  it('未設定なら空欄で出す', async () => {
    // 空欄は「全体の既定値に従う」を意味する
    const wrapper = await mountView()

    expect((wrapper.get('[data-testid="collection-interval"]').element as HTMLInputElement).value)
      .toBe('')
  })

  it('設定済みなら値を出す', async () => {
    fetchTarget.mockResolvedValue({ ...target, collectionIntervalSeconds: 300 })

    const wrapper = await mountView()

    expect((wrapper.get('[data-testid="collection-interval"]').element as HTMLInputElement).value)
      .toBe('300')
  })

  it('収集間隔を送れる', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="collection-interval"]').setValue(300)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(nthCall(updateTarget.mock.calls, 0)[1].collectionIntervalSeconds).toBe(300)
  })

  it('空欄なら既定値に従う指定として送る', async () => {
    // 空文字を数値に変換すると0になり、あり得ない間隔を送ることになる
    fetchTarget.mockResolvedValue({ ...target, collectionIntervalSeconds: 300 })

    const wrapper = await mountView()
    await wrapper.get('[data-testid="collection-interval"]').setValue('')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(nthCall(updateTarget.mock.calls, 0)[1].collectionIntervalSeconds).toBeNull()
  })

  // --- 収集値のグラフ ---

  it('HTTP監視の応答時間を折れ線で出す', async () => {
    fetchTargetMetrics.mockResolvedValue([
      {
        id: 1,
        collectedAt: '2026-07-10T12:00:00Z',
        kind: 'http',
        status: 'Ok',
        payloadJson: JSON.stringify({ success: true, latencyMs: 120 }),
        errorMessage: null,
      },
      {
        id: 2,
        collectedAt: '2026-07-10T12:01:00Z',
        kind: 'http',
        status: 'Ok',
        payloadJson: JSON.stringify({ success: true, latencyMs: 340 }),
        errorMessage: null,
      },
    ])

    const wrapper = await mountView()

    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.text()).toContain('340')
  })

  it('動いていないコンテナ数を折れ線で出す', async () => {
    fetchTargetMetrics.mockResolvedValue([
      {
        id: 1,
        collectedAt: '2026-07-10T12:00:00Z',
        kind: 'docker',
        status: 'Ok',
        payloadJson: JSON.stringify([
          { name: 'web', state: 'running' },
          { name: 'db', state: 'exited' },
        ]),
        errorMessage: null,
      },
    ])

    const wrapper = await mountView()

    expect(wrapper.find('svg').exists()).toBe(true)
  })

  it('使用率は最も高いコンテナの値を描く', async () => {
    // 平均にすると、1つが上限に張り付いていても他が空いていれば低く見え、逼迫が消える
    fetchTargetMetrics.mockResolvedValue([
      {
        id: 1,
        collectedAt: '2026-07-10T12:00:00Z',
        kind: 'resource',
        status: 'Ok',
        payloadJson: JSON.stringify({
          measured: 2,
          skipped: 0,
          containers: [
            { name: 'web', cpuUsagePercent: 5, memoryUsagePercent: 12 },
            { name: 'db', cpuUsagePercent: 88, memoryUsagePercent: 94 },
          ],
        }),
        errorMessage: null,
      },
    ])

    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="cpu-chart"]').text()).toContain('88')
    expect(wrapper.get('[data-testid="memory-chart"]').text()).toContain('94')
  })

  it('使用率が取れていない収集は点にしない', async () => {
    // 0として描くと「使っていない」に見え、取れていないことが隠れる
    fetchTargetMetrics.mockResolvedValue([
      {
        id: 1,
        collectedAt: '2026-07-10T12:00:00Z',
        kind: 'resource',
        status: 'Failed',
        payloadJson: JSON.stringify({
          measured: 1,
          skipped: 0,
          containers: [{ name: 'web', cpuUsagePercent: null, memoryUsagePercent: null }],
        }),
        errorMessage: 'リソース使用率を取得できませんでした。',
      },
    ])

    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="cpu-chart"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="memory-chart"]').exists()).toBe(false)
  })

  it('ディスクは最も埋まっているファイルシステムの値を描く', async () => {
    fetchTargetMetrics.mockResolvedValue([
      {
        id: 1,
        collectedAt: '2026-07-10T12:00:00Z',
        kind: 'disk',
        status: 'Ok',
        payloadJson: JSON.stringify({
          filesystems: [
            { mountpoint: '/', sizeBytes: 1000, availableBytes: 400, usagePercent: 60 },
            { mountpoint: '/mnt/data', sizeBytes: 1000, availableBytes: 30, usagePercent: 97 },
          ],
        }),
        errorMessage: null,
      },
    ])

    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="disk-chart"]').text()).toContain('97')
  })

  it('壊れた収集値が混ざっても他の点は描く', async () => {
    // 1件のJSONが壊れているだけでグラフ全体が消えるのは困る
    fetchTargetMetrics.mockResolvedValue([
      {
        id: 1,
        collectedAt: '2026-07-10T12:00:00Z',
        kind: 'http',
        status: 'Ok',
        payloadJson: 'これはJSONではない',
        errorMessage: null,
      },
      {
        id: 2,
        collectedAt: '2026-07-10T12:01:00Z',
        kind: 'http',
        status: 'Ok',
        payloadJson: JSON.stringify({ latencyMs: 55 }),
        errorMessage: null,
      },
    ])

    const wrapper = await mountView()

    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.text()).toContain('55')
  })

  it('数値の収集値が無ければグラフを出さない', async () => {
    const wrapper = await mountView()

    expect(wrapper.find('svg').exists()).toBe(false)
  })

  // --- 削除 ---

  it('削除で何が消えるかを先に見せる', async () => {
    // 削除は元に戻せないため、確認してから決められるようにする
    const wrapper = await mountView()

    await wrapper.get('[data-testid="preview-delete"]').trigger('click')
    await flushPromises()

    const preview = wrapper.get('[data-testid="delete-preview"]')
    expect(preview.text()).toContain('120')
    expect(preview.text()).toContain('3')
  })

  it('確認を挟むまで削除しない', async () => {
    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="confirm-delete"]').exists()).toBe(false)
    expect(deleteTarget).not.toHaveBeenCalled()
  })

  it('確認後に削除できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="preview-delete"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="confirm-delete"]').trigger('click')
    await flushPromises()

    expect(deleteTarget).toHaveBeenCalledWith(3)
  })

  it('監査ログは消えないことを伝える', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="preview-delete"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="delete-preview"]').text()).toContain(
      '監査ログは削除されません',
    )
  })

  it('監視中で削除できない場合は理由を出す', async () => {
    deleteTarget.mockRejectedValue(
      apiError('target_still_enabled', '監視中の対象は削除できません。'),
    )

    const wrapper = await mountView()
    await wrapper.get('[data-testid="preview-delete"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="confirm-delete"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="delete-section"]').text()).toContain(
      '監視中の対象は削除できません。',
    )
  })

  // --- 行う収集の選択(B-06) ---

  it('行える収集を選択肢として出す', async () => {
    // 選択肢はテンプレートの能力から作る。「推奨」は案内であって選択肢ではない。
    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="monitor-container-state"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="monitor-log-excerpt"]').exists()).toBe(true)
  })

  it('保存済みの選択を反映する', async () => {
    fetchTarget.mockResolvedValue({ ...target, enabledMonitors: ['container-state'] })

    const wrapper = await mountView()

    expect(
      (wrapper.get('[data-testid="monitor-container-state"]').element as HTMLInputElement).checked,
    ).toBe(true)
    expect(
      (wrapper.get('[data-testid="monitor-log-excerpt"]').element as HTMLInputElement).checked,
    ).toBe(false)
  })

  it('外した収集は送らない', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="monitor-log-excerpt"]').setValue(false)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(nthCall(updateTarget.mock.calls, 0)[1].enabledMonitors).toEqual(['container-state'])
  })

  it('すべて外した場合も送る内容は空にする', async () => {
    // サーバー側で既定へ戻す。画面が勝手に補わない。
    const wrapper = await mountView()

    await wrapper.get('[data-testid="monitor-container-state"]').setValue(false)
    await wrapper.get('[data-testid="monitor-log-excerpt"]').setValue(false)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(nthCall(updateTarget.mock.calls, 0)[1].enabledMonitors).toEqual([])
  })

  it('すべて外すと既定に戻ることを伝える', async () => {
    // 「何もしない」と読み違えると、監視を止めたつもりで止まっていない
    const wrapper = await mountView()

    await wrapper.get('[data-testid="monitor-container-state"]').setValue(false)
    await wrapper.get('[data-testid="monitor-log-excerpt"]').setValue(false)

    expect(wrapper.text()).toContain('テンプレートの既定に戻ります')
  })
})
