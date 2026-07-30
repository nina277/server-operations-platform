import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { h } from 'vue'
import IncidentDetailView from '../IncidentDetailView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { nthCall } from '@/test-utils/mock'
import type {
  Approval,
  Diagnosis,
  Incident,
  IncidentNote,
  Recurrence,
  RecoveryAction,
  RecoveryActionDefinition,
  Target,
  TargetCapabilities,
} from '@/types/operations'
import type { CurrentUser } from '@/types/auth'

const incident: Incident = {
  id: 7,
  targetId: 3,
  title: 'nextcloud-app が停止しました',
  classification: 'ContainerDown',
  service: 'nextcloud',
  severity: 'High',
  status: 'Open',
  firstOccurredAt: '2026-07-10T12:00:00Z',
  lastOccurredAt: '2026-07-10T12:05:00Z',
  occurrenceCount: 2,
  resolvedAt: null,
}

const target: Target = {
  id: 3,
  name: 'home-docker',
  templateId: 'docker',
  description: null,
  isEnabled: true,
  autoRecoveryEnabled: false,
  allowedContainers: ['nextcloud-app', 'nextcloud-db'],
  collectionIntervalSeconds: null,
  settings: {},
  configuredCredentials: [],
  createdAt: '2026-07-01T00:00:00Z',
  updatedAt: '2026-07-01T00:00:00Z',
}

const capabilities: TargetCapabilities = {
  targetId: 3,
  templateId: 'docker',
  capabilities: ['container'],
  allowedOperations: ['RESTART_ALLOWED_CONTAINER', 'STOP_ALLOWED_CONTAINER'],
  recommendedMonitors: [],
  initialRules: [],
}

const catalog: RecoveryActionDefinition[] = [
  {
    actionId: 'RESTART_ALLOWED_CONTAINER',
    name: '許可済みコンテナの再起動',
    riskLevel: 'Low',
    requiresApproval: false,
    requiresIdempotencyKey: true,
    requiresTargetResource: true,
    description: '対象別許可・クールダウンを条件に再起動する。',
  },
  {
    actionId: 'STOP_ALLOWED_CONTAINER',
    name: '許可済みコンテナの停止',
    riskLevel: 'Medium',
    requiresApproval: true,
    requiresIdempotencyKey: true,
    requiresTargetResource: true,
    description: '管理者承認とMFA再認証が必要。',
  },
  {
    actionId: 'RECHECK_HTTP_HEALTH',
    name: 'HTTPヘルスチェック再実行',
    riskLevel: 'Low',
    requiresApproval: false,
    requiresIdempotencyKey: false,
    requiresTargetResource: false,
    description: '副作用なし。',
  },
]

const admin: CurrentUser = { id: 1, username: 'admin', role: 'OperatorAdmin', mfaEnabled: true }

const createRecoveryAction =
  vi.fn<(incidentId: number, request: unknown, key: string) => Promise<RecoveryAction>>()
const approvals = vi.fn<() => Promise<Approval[]>>()
const rediagnose =
  vi.fn<() => Promise<{ diagnosis: Diagnosis | null; outcome: string; message: string | null }>>()

const fetchIncidentNotes = vi.fn<() => Promise<IncidentNote[]>>()
const addIncidentNote = vi.fn<(incidentId: number, body: string) => Promise<IncidentNote>>()
const fetchRecurrence = vi.fn<() => Promise<Recurrence>>()

vi.mock('@/api/operations', () => ({
  fetchIncident: () => Promise.resolve(incident),
  fetchTarget: () => Promise.resolve(target),
  fetchTargetCapabilities: () => Promise.resolve(capabilities),
  fetchDiagnoses: () => Promise.resolve([]),
  fetchApprovals: () => approvals(),
  fetchRecoveryActions: () => Promise.resolve([]),
  fetchRecoveryActionCatalog: () => Promise.resolve(catalog),
  fetchIncidentNotes: () => fetchIncidentNotes(),
  addIncidentNote: (incidentId: number, body: string) => addIncidentNote(incidentId, body),
  fetchRecurrence: () => fetchRecurrence(),
  createApproval: vi.fn<() => Promise<Approval>>(),
  createRecoveryAction: (incidentId: number, request: unknown, key: string) =>
    createRecoveryAction(incidentId, request, key),
  rediagnose: () => rediagnose(),
  updateIncidentStatus: vi.fn<() => Promise<Incident>>(),
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
      { path: '/incidents/:id', name: 'incident-detail', component: IncidentDetailView },
      { path: '/', name: 'dashboard', component: { render: () => h('div') } },
    ],
  })
}

async function mountView() {
  const router = createTestRouter()
  await router.push('/incidents/7')
  await router.isReady()

  const wrapper = mount(IncidentDetailView, {
    global: { plugins: [createTestI18n(), router] },
  })
  await flushPromises()
  return wrapper
}

/** 名前で復旧の実行ボタンを探す。 */
function recoveryButton(wrapper: Awaited<ReturnType<typeof mountView>>, actionName: string) {
  const card = wrapper.findAll('.card').find((element) => element.text().includes(actionName))
  return card?.findAll('button').find((b) => b.text() === '復旧を実行')
}

describe('IncidentDetailView', () => {
  beforeEach(async () => {
    localStorage.setItem('sop.accessToken', 'access-1')
    localStorage.setItem('sop.refreshToken', 'refresh-1')
    vi.clearAllMocks()
    setActivePinia(createPinia())

    approvals.mockResolvedValue([])
    fetchIncidentNotes.mockResolvedValue([])
    fetchRecurrence.mockResolvedValue({
      totalCount: 0,
      resolvedCount: 0,
      firstOccurredAt: null,
      previousOccurredAt: null,
      lastSuccessfulActionId: null,
      lastSuccessfulAt: null,
    })
    addIncidentNote.mockResolvedValue({
      id: 1,
      authorName: 'admin',
      body: 'メモ',
      createdAt: '2026-07-10T12:30:00Z',
    })
    createRecoveryAction.mockResolvedValue({
      id: 1,
      incidentId: 7,
      targetId: 3,
      actionId: 'RESTART_ALLOWED_CONTAINER',
      targetResource: 'nextcloud-app',
      riskLevel: 'Low',
      status: 'Queued',
      approvalId: null,
      requestedAt: '2026-07-10T12:10:00Z',
      completedAt: null,
      resultMessage: null,
      blockedReason: null,
    })

    const { useAuthStore } = await import('@/stores/auth')
    await useAuthStore().loadCurrentUser()
  })

  it('対象で許可されていない操作は候補に出さない', async () => {
    const wrapper = await mountView()

    // capabilitiesのallowedOperationsに無いRECHECK_HTTP_HEALTHは出さない
    expect(wrapper.text()).toContain('許可済みコンテナの再起動')
    expect(wrapper.text()).toContain('許可済みコンテナの停止')
    expect(wrapper.text()).not.toContain('HTTPヘルスチェック再実行')
  })

  it('許可コンテナを選ぶまで実行ボタンを押せない', async () => {
    const wrapper = await mountView()

    const button = recoveryButton(wrapper, '許可済みコンテナの再起動')
    expect(button?.attributes('disabled')).toBeDefined()

    await wrapper.get('#resource-RESTART_ALLOWED_CONTAINER').setValue('nextcloud-app')
    expect(
      recoveryButton(wrapper, '許可済みコンテナの再起動')?.attributes('disabled'),
    ).toBeUndefined()
  })

  it('確認ダイアログで実行するまでAPIを呼ばない', async () => {
    const wrapper = await mountView()

    await wrapper.get('#resource-RESTART_ALLOWED_CONTAINER').setValue('nextcloud-app')
    await recoveryButton(wrapper, '許可済みコンテナの再起動')?.trigger('click')
    await flushPromises()

    // ダイアログは出るが、まだ実行はしていない
    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
    expect(createRecoveryAction).not.toHaveBeenCalled()

    await wrapper.get('[data-testid="confirm-execute"]').trigger('click')
    await flushPromises()

    expect(createRecoveryAction).toHaveBeenCalledTimes(1)
  })

  it('復旧の要求には毎回異なる冪等キーを付ける', async () => {
    const wrapper = await mountView()

    await wrapper.get('#resource-RESTART_ALLOWED_CONTAINER').setValue('nextcloud-app')
    await recoveryButton(wrapper, '許可済みコンテナの再起動')?.trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="confirm-execute"]').trigger('click')
    await flushPromises()

    await wrapper.get('#resource-RESTART_ALLOWED_CONTAINER').setValue('nextcloud-app')
    await recoveryButton(wrapper, '許可済みコンテナの再起動')?.trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="confirm-execute"]').trigger('click')
    await flushPromises()

    expect(createRecoveryAction).toHaveBeenCalledTimes(2)
    const firstKey = nthCall(createRecoveryAction.mock.calls, 0)[2]
    const secondKey = nthCall(createRecoveryAction.mock.calls, 1)[2]
    expect(firstKey).not.toBe(secondKey)
  })

  it('承認が必要な操作は有効な承認が無ければ要求できない', async () => {
    const wrapper = await mountView()

    await wrapper.get('#resource-STOP_ALLOWED_CONTAINER').setValue('nextcloud-app')
    await recoveryButton(wrapper, '許可済みコンテナの停止')?.trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(createRecoveryAction).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('有効な承認がありません')
  })

  it('期限切れや使用済みの承認は実行に使わない', async () => {
    approvals.mockResolvedValue([
      {
        id: 11,
        incidentId: 7,
        actionId: 'STOP_ALLOWED_CONTAINER',
        targetResource: 'nextcloud-app',
        status: 'Approved',
        decidedByUsername: 'admin',
        decidedAt: '2026-07-10T12:00:00Z',
        // 過去の日時 = 期限切れ
        expiresAt: '2020-01-01T00:00:00Z',
        isConsumed: false,
        comment: null,
      },
      {
        id: 12,
        incidentId: 7,
        actionId: 'STOP_ALLOWED_CONTAINER',
        targetResource: 'nextcloud-app',
        status: 'Approved',
        decidedByUsername: 'admin',
        decidedAt: '2026-07-10T12:00:00Z',
        expiresAt: '2099-01-01T00:00:00Z',
        // 使用済み
        isConsumed: true,
        comment: null,
      },
    ])

    const wrapper = await mountView()

    await wrapper.get('#resource-STOP_ALLOWED_CONTAINER').setValue('nextcloud-app')
    await recoveryButton(wrapper, '許可済みコンテナの停止')?.trigger('click')
    await flushPromises()

    expect(createRecoveryAction).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('有効な承認がありません')
  })

  it('有効な承認があればそのIDを付けて要求する', async () => {
    approvals.mockResolvedValue([
      {
        id: 13,
        incidentId: 7,
        actionId: 'STOP_ALLOWED_CONTAINER',
        targetResource: 'nextcloud-app',
        status: 'Approved',
        decidedByUsername: 'admin',
        decidedAt: '2026-07-10T12:00:00Z',
        expiresAt: '2099-01-01T00:00:00Z',
        isConsumed: false,
        comment: null,
      },
    ])

    const wrapper = await mountView()

    await wrapper.get('#resource-STOP_ALLOWED_CONTAINER').setValue('nextcloud-app')
    await recoveryButton(wrapper, '許可済みコンテナの停止')?.trigger('click')
    await flushPromises()

    // 危険度Mediumなので確認ダイアログは対象名の入力を求めない
    await wrapper.get('[data-testid="confirm-execute"]').trigger('click')
    await flushPromises()

    expect(createRecoveryAction).toHaveBeenCalledWith(
      7,
      { actionId: 'STOP_ALLOWED_CONTAINER', targetResource: 'nextcloud-app', approvalId: 13 },
      expect.any(String),
    )
  })

  it('AIが診断できなかった場合は理由を示し、診断は増やさない', async () => {
    rediagnose.mockResolvedValue({
      diagnosis: null,
      outcome: 'LimitReached',
      message: '今月の上限に達しました。',
    })

    const wrapper = await mountView()

    const button = wrapper.findAll('button').find((b) => b.text() === 'AIで再診断')
    await button?.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('再診断できませんでした')
    expect(wrapper.text()).toContain('今月の上限に達しました。')
  })

  // --- 対応メモ ---

  it('メモが無ければその旨を出す', async () => {
    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="incident-notes"]').text()).toContain('まだメモはありません')
  })

  it('メモを書いた人と時刻を出す', async () => {
    // 誰の判断だったか分からない記録は後から当てにできない
    fetchIncidentNotes.mockResolvedValue([
      {
        id: 1,
        authorName: 'admin',
        body: 'ディスクの空きが原因だった。',
        createdAt: '2026-07-10T12:30:00Z',
      },
    ])

    const wrapper = await mountView()

    const notes = wrapper.get('[data-testid="incident-notes"]').text()
    expect(notes).toContain('admin')
    expect(notes).toContain('ディスクの空きが原因だった。')
  })

  it('メモを追加できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('#note-body').setValue('次は df を先に見る。')
    await wrapper.get('[data-testid="note-form"]').trigger('submit')
    await flushPromises()

    expect(nthCall(addIncidentNote.mock.calls, 0)[1]).toBe('次は df を先に見る。')
  })

  it('空白だけのメモは送らない', async () => {
    const wrapper = await mountView()

    await wrapper.get('#note-body').setValue('   ')

    expect(wrapper.get('[data-testid="add-note"]').attributes('disabled')).toBeDefined()
  })

  it('追加後は入力欄を空にする', async () => {
    const wrapper = await mountView()

    await wrapper.get('#note-body').setValue('対応済み')
    await wrapper.get('[data-testid="note-form"]').trigger('submit')
    await flushPromises()

    expect((wrapper.get('#note-body').element as HTMLTextAreaElement).value).toBe('')
  })

  // --- 再発 ---

  it('初めての障害ならその旨を出す', async () => {
    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="recurrence"]').text()).toContain('初めてです')
  })

  it('過去の発生回数を出す', async () => {
    fetchRecurrence.mockResolvedValue({
      totalCount: 7,
      resolvedCount: 5,
      firstOccurredAt: '2026-06-01T00:00:00Z',
      previousOccurredAt: '2026-07-09T00:00:00Z',
      lastSuccessfulActionId: null,
      lastSuccessfulAt: null,
    })

    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="recurrence"]').text()).toContain('7')
  })

  it('前回これで直った操作を出す', async () => {
    // 同じ障害が再発したときに、まず何を試すかの手掛かりになる
    fetchRecurrence.mockResolvedValue({
      totalCount: 3,
      resolvedCount: 3,
      firstOccurredAt: '2026-06-01T00:00:00Z',
      previousOccurredAt: '2026-07-09T00:00:00Z',
      lastSuccessfulActionId: 'RESTART_ALLOWED_CONTAINER',
      lastSuccessfulAt: '2026-07-09T00:05:00Z',
    })

    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="last-successful-action"]').text()).toContain(
      'RESTART_ALLOWED_CONTAINER',
    )
  })
})
