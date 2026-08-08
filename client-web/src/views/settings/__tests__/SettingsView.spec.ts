import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'
import SettingsView from '../SettingsView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { lastCall } from '@/test-utils/mock'
import type {
  BackupGeneration,
  BackupRestorePlan,
  BackupSettings,
  NotificationSettings,
} from '@/types/settings'
import type { NotificationTestResult } from '@/types/operations'

const notificationSettings: NotificationSettings = {
  minimumSeverity: 'High',
  renotifyIntervalMinutes: 60,
  emailEnabled: true,
  emailRecipients: ['ops@example.com'],
  smtpHost: 'smtp.example.com',
  smtpPort: 587,
  smtpUseStartTls: true,
  smtpUsername: 'ops',
  smtpFromAddress: 'alerts@example.com',
  pushEnabled: false,
  pushFailureThreshold: 3,
}

const backupSettings: BackupSettings = {
  enabled: false,
  endpoint: 'http://minio.example.com:9000',
  bucketName: 'backups',
  prefix: 'server-operations/',
  region: 'us-east-1',
  usePathStyle: true,
  keepGenerations: 7,
}

const fetchNotificationSettings = vi.fn<() => Promise<NotificationSettings>>()
const updateNotificationSettings =
  vi.fn<(s: NotificationSettings) => Promise<NotificationSettings>>()
const sendTestNotification = vi.fn<() => Promise<NotificationTestResult[]>>()
const fetchBackupSettings = vi.fn<() => Promise<BackupSettings>>()
const fetchBackupGenerations = vi.fn<() => Promise<BackupGeneration[]>>()
const previewBackupRestore = vi.fn<(key: string) => Promise<BackupRestorePlan>>()
const restoreBackup = vi.fn<(key: string) => Promise<BackupRestorePlan>>()
const updateBackupSettings = vi.fn<(s: BackupSettings) => Promise<BackupSettings>>()

vi.mock('@/api/settings', () => ({
  fetchProfile: () => Promise.resolve({ systemName: 'ServerOps', language: 'ja' }),
  updateProfile: vi.fn<() => Promise<never>>(),
  fetchRetention: () =>
    Promise.resolve({
      profile: 'standard',
      metricsDays: 30,
      logsDays: 30,
      incidentsDays: 365,
      auditDays: 365,
    }),
  updateRetention: vi.fn<() => Promise<never>>(),
  previewRetention: () =>
    Promise.resolve({
      metricSnapshots: 0,
      incidentLogs: 0,
      incidents: 0,
      auditLogs: 0,
      notifications: 0,
      healthChecks: 0,
      total: 0,
    }),
  fetchNetworkCidrs: () => Promise.resolve([]),
  addNetworkCidr: vi.fn<() => Promise<never>>(),
  deleteNetworkCidr: vi.fn<() => Promise<never>>(),
  fetchSecretStatus: (kind: string) =>
    Promise.resolve({ kind, isConfigured: false, updatedAt: null }),
  updateSecret: vi.fn<() => Promise<never>>(),
  fetchBackupRuns: () => Promise.resolve([]),
  testBackupConnection: vi.fn<() => Promise<never>>(),
  runBackup: vi.fn<() => Promise<never>>(),
  fetchBackupGenerations: () => fetchBackupGenerations(),
  previewBackupRestore: (key: string) => previewBackupRestore(key),
  restoreBackup: (key: string) => restoreBackup(key),
  fetchNotificationSettings: () => fetchNotificationSettings(),
  updateNotificationSettings: (s: NotificationSettings) => updateNotificationSettings(s),
  sendTestNotification: () => sendTestNotification(),
  fetchBackupSettings: () => fetchBackupSettings(),
  updateBackupSettings: (s: BackupSettings) => updateBackupSettings(s),
}))

vi.mock('@/api/operations', () => ({
  fetchAiUsage: () => Promise.reject(new Error('not used in this test')),
  updateAiEnabled: vi.fn<() => Promise<never>>(),
  updateAiLimits: vi.fn<() => Promise<never>>(),
}))

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
  const wrapper = mount(SettingsView, {
    global: { plugins: [createTestI18n()] },
  })
  await flushPromises()
  return wrapper
}

describe('SettingsView の通知設定', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())

    fetchNotificationSettings.mockResolvedValue({ ...notificationSettings })
    updateNotificationSettings.mockImplementation((s) => Promise.resolve(s))
    fetchBackupSettings.mockResolvedValue({ ...backupSettings })
    updateBackupSettings.mockImplementation((s) => Promise.resolve(s))
    sendTestNotification.mockResolvedValue([
      { channel: 'Email', success: true, skipped: false, message: null },
    ])
  })

  it('保存済みの通知設定を表示する', async () => {
    const wrapper = await mountView()

    expect((wrapper.get('#notify-smtp-host').element as HTMLInputElement).value).toBe(
      'smtp.example.com',
    )
    expect((wrapper.get('#notify-smtp-port').element as HTMLInputElement).value).toBe('587')
    expect((wrapper.get('#notify-severity').element as HTMLSelectElement).value).toBe('High')
  })

  it('宛先を1行1件で表示する', async () => {
    fetchNotificationSettings.mockResolvedValue({
      ...notificationSettings,
      emailRecipients: ['ops@example.com', 'oncall@example.com'],
    })

    const wrapper = await mountView()

    expect((wrapper.get('#notify-recipients').element as HTMLTextAreaElement).value).toBe(
      'ops@example.com\noncall@example.com',
    )
  })

  it('秘密値はこの画面で扱わないことを伝える', async () => {
    // SMTPパスワードを通知設定に混ぜないため、置き場所を案内する
    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="notification-section"]').text()).toContain(
      '「秘密情報」で登録します',
    )
  })

  it('通知設定を保存できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('#notify-renotify').setValue(120)
    await wrapper.get('[data-testid="notification-form"]').trigger('submit')
    await flushPromises()

    expect(lastCall(updateNotificationSettings.mock.calls)[0].renotifyIntervalMinutes).toBe(120)
  })

  it('宛先の空行と前後の空白を落として送る', async () => {
    // 空行をそのまま送ると宛先として扱われ、保存が拒否される
    const wrapper = await mountView()

    await wrapper.get('#notify-recipients').setValue(' ops@example.com \n\n  \noncall@example.com\n')
    await wrapper.get('[data-testid="notification-form"]').trigger('submit')
    await flushPromises()

    expect(lastCall(updateNotificationSettings.mock.calls)[0].emailRecipients).toEqual([
      'ops@example.com',
      'oncall@example.com',
    ])
  })

  it('保存後は返ってきた値で表示を作り直す', async () => {
    updateNotificationSettings.mockResolvedValue({
      ...notificationSettings,
      emailRecipients: ['normalized@example.com'],
    })

    const wrapper = await mountView()
    await wrapper.get('[data-testid="notification-form"]').trigger('submit')
    await flushPromises()

    expect((wrapper.get('#notify-recipients').element as HTMLTextAreaElement).value).toBe(
      'normalized@example.com',
    )
  })

  it('保存が拒否されたら理由を出す', async () => {
    updateNotificationSettings.mockRejectedValue(
      apiError('smtp_host_required', 'メール通知を有効にするにはSMTPサーバーが必要です。'),
    )

    const wrapper = await mountView()
    await wrapper.get('[data-testid="notification-form"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('メール通知を有効にするにはSMTPサーバーが必要です。')
  })

  it('通知設定が取得できなくても他の設定は表示する', async () => {
    fetchNotificationSettings.mockRejectedValue(new Error('failed'))

    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="notification-section"]').exists()).toBe(false)
    expect(wrapper.find('#system-name').exists()).toBe(true)
  })

  // --- テスト送信 ---

  it('テスト送信できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="test-notification"]').trigger('click')
    await flushPromises()

    expect(sendTestNotification).toHaveBeenCalledTimes(1)
  })

  it('宛先を指定する入力欄をテスト送信に付けない', async () => {
    // 任意の宛先へ送れるようにすると踏み台になる
    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="notification-section"]').text()).toContain(
      '宛先はこの画面から指定できません',
    )
  })

  it('チャネルごとの結果を出す', async () => {
    sendTestNotification.mockResolvedValue([
      { channel: 'Email', success: true, skipped: false, message: null },
      { channel: 'Push', success: false, skipped: false, message: '接続できません。' },
    ])

    const wrapper = await mountView()
    await wrapper.get('[data-testid="test-notification"]').trigger('click')
    await flushPromises()

    const results = wrapper.get('[data-testid="notification-test-results"]').text()
    expect(results).toContain('Email')
    expect(results).toContain('送信しました')
    expect(results).toContain('Push')
    expect(results).toContain('送信できませんでした')
    expect(results).toContain('接続できません。')
  })

  it('未設定のチャネルは失敗と区別して出す', async () => {
    // 「設定していない」を失敗と出すと、直すべき問題があるように見える
    sendTestNotification.mockResolvedValue([
      { channel: 'Push', success: false, skipped: true, message: 'Push通知は無効です。' },
    ])

    const wrapper = await mountView()
    await wrapper.get('[data-testid="test-notification"]').trigger('click')
    await flushPromises()

    const results = wrapper.get('[data-testid="notification-test-results"]').text()
    expect(results).toContain('未設定のため送りませんでした')
    expect(results).not.toContain('送信できませんでした')
  })
})

describe('SettingsView のバックアップ設定', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())

    fetchNotificationSettings.mockResolvedValue({ ...notificationSettings })
    updateNotificationSettings.mockImplementation((s) => Promise.resolve(s))
    fetchBackupSettings.mockResolvedValue({ ...backupSettings })
    updateBackupSettings.mockImplementation((s) => Promise.resolve(s))
  })

  it('保存済みのバックアップ設定を表示する', async () => {
    const wrapper = await mountView()

    expect((wrapper.get('#backup-endpoint').element as HTMLInputElement).value).toBe(
      'http://minio.example.com:9000',
    )
    expect((wrapper.get('#backup-bucket').element as HTMLInputElement).value).toBe('backups')
    expect((wrapper.get('#backup-generations').element as HTMLInputElement).value).toBe('7')
  })

  it('バックアップ設定を保存できる', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="backup-enabled"]').setValue(true)
    await wrapper.get('#backup-generations').setValue(30)
    await wrapper.get('[data-testid="backup-settings-form"]').trigger('submit')
    await flushPromises()

    expect(lastCall(updateBackupSettings.mock.calls)[0]).toMatchObject({
      enabled: true,
      keepGenerations: 30,
    })
  })

  it('有効にするなら保存先とバケット名を必須にする', async () => {
    // 有効なのに保存先が空だとサーバー側で拒否されるため、先に画面で止める
    const wrapper = await mountView()

    await wrapper.get('[data-testid="backup-enabled"]').setValue(true)

    expect(wrapper.get('#backup-endpoint').attributes('required')).toBeDefined()
    expect(wrapper.get('#backup-bucket').attributes('required')).toBeDefined()
  })

  it('無効なら保存先を必須にしない', async () => {
    // 保存先を決める前の状態も保存できるようにする
    const wrapper = await mountView()

    expect(wrapper.get('#backup-endpoint').attributes('required')).toBeUndefined()
  })

  it('遮断される保存先を保存しようとしたら理由を出す', async () => {
    updateBackupSettings.mockRejectedValue(
      apiError(
        'url_not_allowed',
        'localhost・リンクローカル・メタデータIP・マルチキャスト宛の接続先は登録できません。',
      ),
    )

    const wrapper = await mountView()
    await wrapper.get('[data-testid="backup-settings-form"]').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('メタデータIP')
  })

  it('バックアップ設定が取得できなくても他の設定は表示する', async () => {
    fetchBackupSettings.mockRejectedValue(new Error('failed'))

    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="backup-settings-form"]').exists()).toBe(false)
    expect(wrapper.find('#system-name').exists()).toBe(true)
  })
})

/**
 * バックアップからの復元。
 *
 * **復元は既存のデータを書き換える。**
 * 下見を通さずに実行できないこと、確認を取ることを固定する。
 */
describe('SettingsView バックアップからの復元', () => {
  const generation: BackupGeneration = {
    objectKey: 'server-operations/backup-20260808-120000.bin',
    lastModified: '2026-08-08T12:00:00Z',
    sizeBytes: 4966,
  }

  const plan = (applied: boolean): BackupRestorePlan => ({
    objectKey: generation.objectKey,
    snapshotCreatedAt: '2026-08-08T12:00:00Z',
    version: 1,
    applied,
    items: [
      { category: '監視対象', added: 2, updated: 1, unchanged: 0, notInBackup: 3 },
      { category: '診断ルール', added: 0, updated: 0, unchanged: 6, notInBackup: 0 },
    ],
    notes: ['利用者 1 件は復元しません。'],
  })

  beforeEach(() => {
    fetchBackupGenerations.mockResolvedValue([generation])
    previewBackupRestore.mockResolvedValue(plan(false))
    restoreBackup.mockResolvedValue(plan(true))
  })

  it('下見をするまで復元を実行できない', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="load-generations"]').trigger('click')
    await flushPromises()

    // 世代を選んだだけでは押せない
    expect(
      wrapper.get('[data-testid="apply-restore"]').attributes('disabled'),
    ).toBeDefined()
  })

  it('下見は何が起きるかを出すが、復元は呼ばない', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="load-generations"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="preview-restore"]').trigger('click')
    await flushPromises()

    expect(previewBackupRestore).toHaveBeenCalledWith(generation.objectKey)
    expect(restoreBackup).not.toHaveBeenCalled()

    const shown = wrapper.get('[data-testid="restore-plan"]').text()
    expect(shown).toContain('監視対象')
    // バックアップに無いものを消さないことが読み取れる
    expect(shown).toContain('3')
    // 戻さないものの説明が出る
    expect(shown).toContain('利用者 1 件は復元しません。')
  })

  it('確認を断ると復元しない', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    const wrapper = await mountView()

    await wrapper.get('[data-testid="load-generations"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="preview-restore"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="apply-restore"]').trigger('click')
    await flushPromises()

    expect(confirm).toHaveBeenCalled()
    expect(restoreBackup).not.toHaveBeenCalled()
    confirm.mockRestore()
  })

  it('確認を通すと復元する', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    const wrapper = await mountView()

    await wrapper.get('[data-testid="load-generations"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="preview-restore"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="apply-restore"]').trigger('click')
    await flushPromises()

    expect(restoreBackup).toHaveBeenCalledWith(generation.objectKey)
    confirm.mockRestore()
  })
})
