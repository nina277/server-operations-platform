<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { fetchApiLiveness, type HealthStatus } from '@/api/health'

// 全体の集計表示はT-10で実装する。ここではAPI疎通のみ確認する。
const { t } = useI18n()
const apiStatus = ref<HealthStatus | 'checking'>('checking')

onMounted(async () => {
  apiStatus.value = await fetchApiLiveness()
})
</script>

<template>
  <div>
    <PageHeader :title="t('nav.dashboard')" :description="t('app.description')" />

    <section aria-labelledby="system-status-heading">
      <h2 id="system-status-heading" class="section-title">{{ t('nav.dashboard') }}</h2>
      <dl class="status-list">
        <dt>API</dt>
        <dd data-testid="api-status">
          <span v-if="apiStatus === 'checking'">{{ t('common.loading') }}</span>
          <StatusBadge
            v-else-if="apiStatus === 'healthy'"
            tone="low"
            :label="t('status.healthy')"
          />
          <StatusBadge v-else tone="critical" :label="t('status.unhealthy')" />
        </dd>
      </dl>
    </section>
  </div>
</template>

<style scoped>
.section-title {
  font-size: 1.125rem;
  font-weight: 600;
}

.status-list {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: var(--spacing-xs) var(--spacing-md);
  margin-top: var(--spacing-sm);
}
</style>
