import axios from 'axios'

// 同一オリジンの /api 配下をnginx(本番)またはViteのdevプロキシ(開発)がAPIへ中継する
const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/',
  timeout: 10_000,
})

export default http
