import { ref, shallowRef, type Ref, type ShallowRef } from 'vue'
import axios from 'axios'
import { extractErrorMessage } from '@/api/http'

export interface AsyncData<T> {
  data: ShallowRef<T | null>
  loading: Ref<boolean>
  error: Ref<string | null>
  /** 403のときだけtrue。権限不足は取得失敗と分けて案内する。 */
  forbidden: Ref<boolean>
  load: () => Promise<void>
}

/**
 * 一覧・詳細の取得に共通する読み込み状態を扱う。
 * 権限不足(403)は取得失敗と区別し、画面側で別の案内を出せるようにする。
 */
export function useAsyncData<T>(fetcher: () => Promise<T>, fallbackMessage: string): AsyncData<T> {
  const data = shallowRef<T | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)
  const forbidden = ref(false)

  async function load(): Promise<void> {
    loading.value = true
    error.value = null
    forbidden.value = false

    try {
      data.value = await fetcher()
    } catch (e) {
      if (axios.isAxiosError(e) && e.response?.status === 403) {
        forbidden.value = true
      } else {
        error.value = extractErrorMessage(e, fallbackMessage)
      }
    } finally {
      loading.value = false
    }
  }

  return { data, loading, error, forbidden, load }
}
