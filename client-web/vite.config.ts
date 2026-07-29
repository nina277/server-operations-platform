import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), vueDevTools()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    rollupOptions: {
      input: {
        index: fileURLToPath(new URL('./index.html', import.meta.url)),
        // Push通知のService Worker。外部CDNへ依存させず、依存ごとバンドルする。
        'firebase-messaging-sw': fileURLToPath(
          new URL('./src/firebase-messaging-sw.ts', import.meta.url),
        ),
      },
      output: {
        // Service Workerは配信元の直下に固定名で置く必要がある
        entryFileNames: (chunk) =>
          chunk.name === 'firebase-messaging-sw' ? '[name].js' : 'assets/[name]-[hash].js',
      },
    },
  },
  server: {
    proxy: {
      // 開発時は /api をローカル起動中のAPI(launchSettings.jsonのポート)へ中継する
      '/api': {
        target: 'http://localhost:5275',
        changeOrigin: true,
      },
    },
  },
})
