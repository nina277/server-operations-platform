import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import router from './router'
import { i18n } from './locales'
import { useAuthStore } from './stores/auth'

const app = createApp(App)

app.use(createPinia())
app.use(i18n)

// 保存済みトークンから利用者情報を復元してから経路判定を始める。
// 先にrouterを登録すると、役割が未取得のまま権限判定が走ってしまう。
const auth = useAuthStore()
document.documentElement.lang = i18n.global.locale.value

auth.restore().finally(() => {
  app.use(router)
  app.mount('#app')
})

// 画面の外枠だけを保持するService Worker。運用データはキャッシュしない。
if ('serviceWorker' in navigator && import.meta.env.PROD) {
  window.addEventListener('load', () => {
    void navigator.serviceWorker.register('/sw.js').catch(() => {
      // 登録できなくてもアプリは通常どおり動くため、失敗は無視する
    })
  })
}
