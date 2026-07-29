<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <main class="not-found">
    <h1>404</h1>
    <p>{{ t('common.empty') }}</p>
    <RouterLink :to="{ name: 'dashboard' }">{{ t('nav.dashboard') }}</RouterLink>
  </main>
</template>

<style scoped>
.not-found {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-sm);
}

h1 {
  font-size: 2rem;
  font-weight: 600;
}
</style>
