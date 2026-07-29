/// <reference lib="webworker" />
import { initializeApp } from 'firebase/app'
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw'

/*
 * 画面を閉じている間にPush通知を受け取るService Worker。
 *
 * FirebaseのWeb設定は公開前提の値だが、環境ごとに変わるため
 * 登録時のクエリ文字列で受け取る(ビルド時に埋め込まない)。
 * 外部CDNは読み込まず、依存はこのバンドルに含める(自ホスト運用のため)。
 */
declare const self: ServiceWorkerGlobalScope

const params = new URLSearchParams(self.location.search)

const config = {
  apiKey: params.get('apiKey') ?? '',
  authDomain: params.get('authDomain') ?? '',
  projectId: params.get('projectId') ?? '',
  messagingSenderId: params.get('messagingSenderId') ?? '',
  appId: params.get('appId') ?? '',
}

if (config.apiKey.length > 0 && config.projectId.length > 0) {
  const app = initializeApp(config)

  onBackgroundMessage(getMessaging(app), (payload) => {
    const title = payload.notification?.title ?? 'Server Operations Platform'

    void self.registration.showNotification(title, {
      body: payload.notification?.body ?? '',
      icon: '/icon-192.png',
      badge: '/icon-192.png',
      // 同じ事象の通知は1つにまとめ、画面を埋めないようにする
      tag: payload.data?.aggregationKey ?? undefined,
      data: payload.data ?? {},
    })
  })
}

// 通知を押したら該当のインシデントを開く
self.addEventListener('notificationclick', (event) => {
  event.notification.close()

  const incidentId = (event.notification.data as Record<string, string> | undefined)?.incidentId
  const path = incidentId ? `/incidents/${incidentId}` : '/'

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
      // 既に開いている画面があればそれを使う
      const existing = clients.find((client) => client.url.startsWith(self.location.origin))
      if (existing) {
        return existing.focus().then((client) => client.navigate(path))
      }
      return self.clients.openWindow(path)
    }),
  )
})
