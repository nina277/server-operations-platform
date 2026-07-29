import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { describePushState, enablePushNotifications, isPushSupported } from '../push'

const registerDeviceToken = vi.fn<(token: string, label?: string) => Promise<unknown>>()

vi.mock('@/api/operations', () => ({
  registerDeviceToken: (token: string, label?: string) => registerDeviceToken(token, label),
}))

/** Push通知に必要なブラウザ機能があることにする。 */
function stubBrowserSupport(permission: NotificationPermission = 'granted'): void {
  vi.stubGlobal('Notification', {
    requestPermission: vi.fn<() => Promise<NotificationPermission>>().mockResolvedValue(permission),
  })
  vi.stubGlobal('PushManager', class {})
  Object.defineProperty(navigator, 'serviceWorker', {
    configurable: true,
    value: { register: vi.fn<() => Promise<unknown>>().mockResolvedValue({}) },
  })
}

describe('Push通知の購読', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.unstubAllEnvs()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('必要な機能が無い環境では購読しない', async () => {
    // Notification等を差し込まないまま呼ぶ
    vi.stubGlobal('Notification', undefined)

    expect(isPushSupported()).toBe(false)
    await expect(enablePushNotifications()).resolves.toBe('unsupported')
    expect(registerDeviceToken).not.toHaveBeenCalled()
  })

  it('設定が入っていなければ購読を試みない', async () => {
    stubBrowserSupport()
    // VITE_FIREBASE_* を設定しない

    await expect(enablePushNotifications()).resolves.toBe('not-configured')
    expect(registerDeviceToken).not.toHaveBeenCalled()
  })

  it('設定が一部だけでも購読を試みない', async () => {
    stubBrowserSupport()
    vi.stubEnv('VITE_FIREBASE_API_KEY', 'key')
    vi.stubEnv('VITE_FIREBASE_PROJECT_ID', 'project')
    // 残りは未設定

    await expect(enablePushNotifications()).resolves.toBe('not-configured')
    expect(registerDeviceToken).not.toHaveBeenCalled()
  })

  it('通知が許可されなければ端末を登録しない', async () => {
    stubBrowserSupport('denied')
    vi.stubEnv('VITE_FIREBASE_API_KEY', 'key')
    vi.stubEnv('VITE_FIREBASE_AUTH_DOMAIN', 'example.firebaseapp.com')
    vi.stubEnv('VITE_FIREBASE_PROJECT_ID', 'project')
    vi.stubEnv('VITE_FIREBASE_MESSAGING_SENDER_ID', '123')
    vi.stubEnv('VITE_FIREBASE_APP_ID', 'app')
    vi.stubEnv('VITE_FIREBASE_VAPID_KEY', 'vapid')

    await expect(enablePushNotifications()).resolves.toBe('denied')
    expect(registerDeviceToken).not.toHaveBeenCalled()
  })

  it('状態ごとに案内の文言を切り替える', () => {
    expect(describePushState('unsupported')).toBe('notifications.pushUnsupported')
    expect(describePushState('denied')).toBe('notifications.pushDenied')
    expect(describePushState('not-configured')).toBe('notifications.pushNotConfigured')
    expect(describePushState('failed')).toBe('common.error')
  })
})
