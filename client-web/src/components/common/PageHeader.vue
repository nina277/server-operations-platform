<script setup lang="ts">
/**
 * 各画面の見出し。h1は1画面に1つだけ置く。
 */
defineProps<{
  title: string
  description?: string
}>()
</script>

<template>
  <header class="page-header">
    <div>
      <h1 class="page-header__title">{{ title }}</h1>
      <p v-if="description" class="page-header__description">{{ description }}</p>
    </div>
    <div v-if="$slots.actions" class="page-header__actions">
      <slot name="actions" />
    </div>
  </header>
</template>

<style scoped>
.page-header {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-lg);
  padding-bottom: var(--spacing-sm);
  border-bottom: 1px solid var(--color-border);
}

.page-header__title {
  font-size: 1.375rem;
  font-weight: 600;
}

.page-header__description {
  margin-top: var(--spacing-xs);
  color: var(--color-text-muted);
}

.page-header__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
}
</style>
