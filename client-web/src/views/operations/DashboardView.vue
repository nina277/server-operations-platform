<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import MetricValue from '@/components/common/MetricValue.vue'
import AsyncState from '@/components/common/AsyncState.vue'
import { fetchDashboardSummary } from '@/api/operations'
import { useAsyncData } from '@/composables/useAsyncData'
import { formatDateTime, incidentStatusTone, severityTone } from '@/utils/format'

const { t, locale } = useI18n()

const { data, loading, error, forbidden, load } = useAsyncData(
  fetchDashboardSummary,
  t('common.error'),
)

onMounted(load)

/** 対応中(未解決)のインシデント総数。 */
const activeIncidentCount = computed(() =>
  Object.values(data.value?.activeIncidentsBySeverity ?? {}).reduce((sum, n) => sum + n, 0),
)

const severityOrder = ['Critical', 'High', 'Medium', 'Low'] as const

const severityCounts = computed(() =>
  severityOrder.map((severity) => ({
    severity,
    count: data.value?.activeIncidentsBySeverity[severity] ?? 0,
  })),
)

const statusCounts = computed(() =>
  Object.entries(data.value?.incidentsByStatus ?? {}).map(([status, count]) => ({ status, count })),
)
</script>

<template>
  <div>
    <PageHeader :title="t('nav.dashboard')" :description="t('app.description')" />

    <AsyncState
      :loading="loading"
      :error="error"
      :forbidden="forbidden"
      :empty="data === null"
      @retry="load"
    >
      <template v-if="data">
        <dl class="metrics">
          <MetricValue :label="t('dashboard.targetCount')" :value="data.targetCount" />
          <MetricValue
            :label="t('dashboard.enabledTargetCount')"
            :value="data.enabledTargetCount"
          />
          <MetricValue :label="t('dashboard.activeIncidents')" :value="activeIncidentCount" />
        </dl>

        <section aria-labelledby="by-severity-heading" class="section">
          <h2 id="by-severity-heading" class="section__title">{{ t('dashboard.bySeverity') }}</h2>
          <ul class="chips">
            <li v-for="row in severityCounts" :key="row.severity">
              <StatusBadge
                :tone="severityTone(row.severity)"
                :label="`${t(`severity.${row.severity.toLowerCase()}`)}: ${row.count}`"
              />
            </li>
          </ul>
        </section>

        <section aria-labelledby="by-status-heading" class="section">
          <h2 id="by-status-heading" class="section__title">{{ t('dashboard.byStatus') }}</h2>
          <ul v-if="statusCounts.length > 0" class="chips">
            <li v-for="row in statusCounts" :key="row.status">
              <StatusBadge
                :tone="incidentStatusTone(row.status)"
                :label="`${t(`status.${row.status.toLowerCase()}`)}: ${row.count}`"
              />
            </li>
          </ul>
          <p v-else class="muted">{{ t('common.empty') }}</p>
        </section>

        <section aria-labelledby="recent-heading" class="section">
          <h2 id="recent-heading" class="section__title">{{ t('dashboard.recentIncidents') }}</h2>

          <div v-if="data.recentIncidents.length > 0" class="table-scroll">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">{{ t('incidents.title') }}</th>
                  <th scope="col">{{ t('severity.critical') }}/{{ t('severity.low') }}</th>
                  <th scope="col">{{ t('nav.incidents') }}</th>
                  <th scope="col">{{ t('incidents.lastOccurredAt') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="incident in data.recentIncidents" :key="incident.id">
                  <th scope="row" class="table__title">
                    <RouterLink :to="{ name: 'incident-detail', params: { id: incident.id } }">
                      {{ incident.title }}
                    </RouterLink>
                  </th>
                  <td>
                    <StatusBadge
                      :tone="severityTone(incident.severity)"
                      :label="t(`severity.${incident.severity.toLowerCase()}`)"
                    />
                  </td>
                  <td>
                    <StatusBadge
                      :tone="incidentStatusTone(incident.status)"
                      :label="t(`status.${incident.status.toLowerCase()}`)"
                    />
                  </td>
                  <td>{{ formatDateTime(incident.lastOccurredAt, locale) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="muted">{{ t('common.empty') }}</p>
        </section>
      </template>
    </AsyncState>
  </div>
</template>

<style scoped>
.metrics {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  gap: var(--spacing-md);
}

.section {
  margin-top: var(--spacing-xl);
}

.section__title {
  font-size: 1.125rem;
  font-weight: 600;
  margin-bottom: var(--spacing-sm);
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
  list-style: none;
}

.muted {
  color: var(--color-text-muted);
}
</style>
