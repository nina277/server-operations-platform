/*
 * アプリの外枠(HTML)だけをキャッシュするService Worker。
 *
 * 運用データは常に最新である必要があるため、/api配下は一切キャッシュしない。
 * 古い状態を「今の状態」として見せてしまうと、復旧の判断を誤らせるため。
 */
const CACHE_NAME = 'serverops-shell-v1'
const SHELL_URL = '/index.html'

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE_NAME)
      .then((cache) => cache.addAll([SHELL_URL, '/manifest.webmanifest', '/icon-192.png']))
      .then(() => self.skipWaiting()),
  )
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))),
      )
      .then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', (event) => {
  const request = event.request
  const url = new URL(request.url)

  // API・認証は必ずネットワークへ。取得できなければ失敗として扱う。
  if (url.origin !== self.location.origin || url.pathname.startsWith('/api/')) {
    return
  }

  // 画面遷移はネットワーク優先。切断時のみ外枠を返し、中身は画面側で再取得させる。
  if (request.mode === 'navigate') {
    event.respondWith(fetch(request).catch(() => caches.match(SHELL_URL)))
    return
  }

  // ビルド成果物はファイル名にハッシュが入るため、キャッシュ優先で問題ない
  if (url.pathname.startsWith('/assets/')) {
    event.respondWith(
      caches.match(request).then(
        (cached) =>
          cached ??
          fetch(request).then((response) => {
            if (response.ok) {
              const copy = response.clone()
              caches.open(CACHE_NAME).then((cache) => cache.put(request, copy))
            }
            return response
          }),
      ),
    )
  }
})
