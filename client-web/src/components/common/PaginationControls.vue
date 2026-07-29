<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}>()

const emit = defineEmits<{ 'update:page': [page: number] }>()

const { t } = useI18n()

const from = computed(() => (props.totalCount === 0 ? 0 : (props.page - 1) * props.pageSize + 1))
const to = computed(() => Math.min(props.page * props.pageSize, props.totalCount))

const hasPrevious = computed(() => props.page > 1)
const hasNext = computed(() => props.page < props.totalPages)
</script>

<template>
  <nav class="pagination" :aria-label="t('pagination.page', { page, totalPages })">
    <p class="pagination__summary" aria-live="polite">
      {{ t('pagination.summary', { total: totalCount, from, to }) }}
    </p>

    <div class="pagination__buttons">
      <button
        type="button"
        class="pagination__button"
        :disabled="!hasPrevious"
        @click="emit('update:page', page - 1)"
      >
        {{ t('pagination.previous') }}
      </button>
      <span class="pagination__page">{{ t('pagination.page', { page, totalPages }) }}</span>
      <button
        type="button"
        class="pagination__button"
        :disabled="!hasNext"
        @click="emit('update:page', page + 1)"
      >
        {{ t('pagination.next') }}
      </button>
    </div>
  </nav>
</template>

<style scoped>
.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-sm);
  margin-top: var(--spacing-md);
}

.pagination__summary,
.pagination__page {
  font-size: 0.875rem;
  color: var(--color-text-muted);
}

.pagination__buttons {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
}

.pagination__button {
  padding: 0.35em 0.9em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-surface);
  color: var(--color-text);
  cursor: pointer;
}

.pagination__button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
