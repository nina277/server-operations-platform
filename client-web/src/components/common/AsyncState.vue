<script setup lang="ts">
import { useI18n } from 'vue-i18n'

/**
 * 非同期データの状態表示。読み込み・空・取得失敗・権限不足をすべて扱う。
 * 成功時はスロットの内容を表示する。
 */
withDefaults(
  defineProps<{
    loading: boolean
    /** エラーメッセージ。nullなら正常。 */
    error?: string | null
    /** データが空か。 */
    empty?: boolean
    /** 権限不足か(403)。 */
    forbidden?: boolean
    emptyMessage?: string
  }>(),
  { error: null, empty: false, forbidden: false, emptyMessage: undefined },
)

defineEmits<{ retry: [] }>()

const { t } = useI18n()
</script>

<template>
  <div v-if="loading" class="async-state" role="status" aria-live="polite">
    <span class="async-state__icon" aria-hidden="true">◌</span>
    <p>{{ t('common.loading') }}</p>
  </div>

  <div v-else-if="forbidden" class="async-state async-state--error" role="alert">
    <span class="async-state__icon" aria-hidden="true">⛔</span>
    <p>{{ t('common.forbidden') }}</p>
  </div>

  <div v-else-if="error" class="async-state async-state--error" role="alert">
    <span class="async-state__icon" aria-hidden="true">⚠</span>
    <p>{{ error }}</p>
    <button type="button" class="async-state__retry" @click="$emit('retry')">
      {{ t('common.retry') }}
    </button>
  </div>

  <div v-else-if="empty" class="async-state" role="status">
    <span class="async-state__icon" aria-hidden="true">—</span>
    <p>{{ emptyMessage ?? t('common.empty') }}</p>
  </div>

  <slot v-else />
</template>

<style scoped>
.async-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--spacing-sm);
  padding: var(--spacing-xl) var(--spacing-md);
  color: var(--color-text-muted);
  text-align: center;
}

.async-state--error {
  color: var(--color-critical);
}

.async-state__icon {
  font-size: 1.5rem;
  line-height: 1;
}

.async-state__retry {
  padding: 0.4em 1.2em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-surface);
  color: var(--color-text);
  cursor: pointer;
}

.async-state__retry:hover {
  background-color: var(--color-surface-alt);
}
</style>
