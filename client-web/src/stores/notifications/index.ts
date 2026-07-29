import { ref } from 'vue'
import { defineStore } from 'pinia'
import { fetchUnreadCount } from '@/api/operations'

/** 画面共通で使う未読件数。取得に失敗しても画面は動かし続ける。 */
export const useNotificationsStore = defineStore('notifications', () => {
  const unreadCount = ref(0)

  async function refreshUnreadCount(): Promise<void> {
    try {
      unreadCount.value = await fetchUnreadCount()
    } catch {
      // 未読件数はあくまで補助情報のため、失敗しても表示を壊さない
    }
  }

  return { unreadCount, refreshUnreadCount }
})
