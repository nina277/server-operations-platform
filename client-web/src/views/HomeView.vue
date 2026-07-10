<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { fetchApiLiveness, type HealthStatus } from '@/api/health'

const apiStatus = ref<HealthStatus | 'checking'>('checking')

onMounted(async () => {
  apiStatus.value = await fetchApiLiveness()
})
</script>

<template>
  <main>
    <h1>Server Operations Platform</h1>
    <p>自律型サーバー運用支援システム</p>

    <section aria-label="システム状態">
      <h2>システム状態</h2>
      <dl>
        <dt>API</dt>
        <dd data-testid="api-status">
          <span v-if="apiStatus === 'checking'">確認中...</span>
          <span v-else-if="apiStatus === 'healthy'">正常</span>
          <span v-else>接続不可</span>
        </dd>
      </dl>
    </section>
  </main>
</template>

<style scoped>
h1 {
  font-size: 1.5rem;
  font-weight: 600;
}

section {
  margin-top: 2rem;
}

h2 {
  font-size: 1.125rem;
  font-weight: 600;
}

dl {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 0.25rem 1rem;
  margin-top: 0.5rem;
}
</style>
