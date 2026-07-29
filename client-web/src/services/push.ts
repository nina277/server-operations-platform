import { registerDeviceToken } from '@/api/operations'

/**
 * Push通知(FCM Web Push)の購読。
 *
 * 設定値は配信時の環境変数から入る。値が入っていない環境では購読を試みず、
 * 「未設定」として扱う(通知が使えないだけで、他の機能は動かし続ける)。
 */
export type PushState = 'registered' | 'unsupported' | 'denied' | 'not-configured' | 'failed'

interface FirebaseWebConfig {
  apiKey: string
  authDomain: string
  projectId: string
  messagingSenderId: string
  appId: string
  vapidKey: string
}

function readConfig(): FirebaseWebConfig | null {
  const env = import.meta.env
  const config = {
    apiKey: env.VITE_FIREBASE_API_KEY,
    authDomain: env.VITE_FIREBASE_AUTH_DOMAIN,
    projectId: env.VITE_FIREBASE_PROJECT_ID,
    messagingSenderId: env.VITE_FIREBASE_MESSAGING_SENDER_ID,
    appId: env.VITE_FIREBASE_APP_ID,
    vapidKey: env.VITE_FIREBASE_VAPID_KEY,
  }

  // 1つでも欠けていたら購読しない(中途半端な設定で失敗させない)
  return Object.values(config).every((value) => typeof value === 'string' && value.length > 0)
    ? (config as FirebaseWebConfig)
    : null
}

/** 状態に対応する表示文言のキー。 */
export function describePushState(state: PushState): string {
  switch (state) {
    case 'unsupported':
      return 'notifications.pushUnsupported'
    case 'denied':
      return 'notifications.pushDenied'
    case 'not-configured':
      return 'notifications.pushNotConfigured'
    default:
      return 'common.error'
  }
}

export function isPushSupported(): boolean {
  return (
    typeof window !== 'undefined' &&
    'serviceWorker' in navigator &&
    'Notification' in window &&
    'PushManager' in window
  )
}

/**
 * この端末で通知を受け取れるようにする。
 * 端末トークンはサーバーへ登録し、以後の配信に使う。
 */
export async function enablePushNotifications(label?: string): Promise<PushState> {
  if (!isPushSupported()) {
    return 'unsupported'
  }

  const config = readConfig()
  if (config === null) {
    return 'not-configured'
  }

  const permission = await Notification.requestPermission()
  if (permission !== 'granted') {
    return 'denied'
  }

  try {
    // 通知を使うときだけ読み込む(初回表示のために抱え込まない)
    const [{ initializeApp, getApps }, { getMessaging, getToken }] = await Promise.all([
      import('firebase/app'),
      import('firebase/messaging'),
    ])

    const app = getApps().length > 0 ? getApps()[0] : initializeApp(config)

    // Service Workerはビルド成果物のため設定を埋め込めない。
    // Firebaseのweb設定は公開前提の値なので、登録時のクエリ文字列で渡す。
    const swParams = new URLSearchParams({
      apiKey: config.apiKey,
      authDomain: config.authDomain,
      projectId: config.projectId,
      messagingSenderId: config.messagingSenderId,
      appId: config.appId,
    })
    const registration = await navigator.serviceWorker.register(
      `/firebase-messaging-sw.js?${swParams.toString()}`,
      { type: 'module' },
    )

    const token = await getToken(getMessaging(app), {
      vapidKey: config.vapidKey,
      serviceWorkerRegistration: registration,
    })

    if (!token) {
      return 'failed'
    }

    await registerDeviceToken(token, label ?? navigator.userAgent.slice(0, 100))
    return 'registered'
  } catch {
    return 'failed'
  }
}
