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

/**
 * 収集が届いていない対象。
 * インシデント0件は「異常が無い」とは限らない。監視が止まっていても0件になる。
 * その区別が付くよう、他の集計より先に、目立つ形で出す。
 */
const unreachedTargets = computed(() => data.value?.unreachedTargets ?? [])

/** 対象ごとの状態。手当てが要るものから順に並んでサーバーから返る。 */
const targetStates = computed(() => data.value?.targetStates ?? [])

/** 経過時間を読みやすい単位にする。秒のままだと大きい値が頭に入らない。 */
function formatElapsed(seconds: number | null): string {
  if (seconds === null) {
    return '—'
  }

  if (seconds < 60) {
    return t('monitoringHealth.seconds', { n: seconds })
  }
  if (seconds < 3600) {
    return t('monitoringHealth.minutes', { n: Math.floor(seconds / 60) })
  }
  return t('monitoringHealth.hours', { n: Math.floor(seconds / 3600) })
}
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
        <section
          v-if="unreachedTargets.length > 0"
          aria-labelledby="unreached-heading"
          class="alert"
          data-testid="unreached-targets"
        >
          <h2 id="unreached-heading" class="alert__title">
            {{ t('monitoringHealth.title') }}
          </h2>
          <p class="alert__note">{{ t('monitoringHealth.description') }}</p>

          <ul class="alert__list">
            <li v-for="item in unreachedTargets" :key="item.targetId">
              <StatusBadge
                tone="critical"
                :label="
                  item.reach === 'NeverCollected'
                    ? t('monitoringHealth.neverCollected')
                    : t('monitoringHealth.stale')
                "
              />
              <RouterLink :to="{ name: 'target-detail', params: { id: item.targetId } }">
                {{ item.targetName }}
              </RouterLink>
              <span v-if="item.lastCollectedAt">
                {{ t('monitoringHealth.lastCollectedAt') }}:
                {{ formatDateTime(item.lastCollectedAt, locale) }}
                ({{ formatElapsed(item.staleForSeconds) }})
              </span>
            </li>
          </ul>
        </section>

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

        <section
          v-if="targetStates.length > 0"
          aria-labelledby="states-heading"
          class="section states"
          data-testid="target-states"
        >
          <h2 id="states-heading" class="section__title">{{ t('dashboard.targetStates') }}</h2>
          <p class="form-field__help">{{ t('dashboard.targetStatesHelp') }}</p>

          <div class="table-scroll">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">{{ t('incidents.target') }}</th>
                  <th scope="col">{{ t('dashboard.reach') }}</th>
                  <th scope="col">{{ t('dashboard.activeIncidents') }}</th>
                  <th scope="col">{{ t('monitoringHealth.lastCollectedAt') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="state in targetStates" :key="state.targetId">
                  <th scope="row">
                    <RouterLink :to="{ name: 'target-detail', params: { id: state.targetId } }">
                      {{ state.targetName }}
                    </RouterLink>
                    <StatusBadge
                      v-if="!state.isEnabled"
                      tone="medium"
                      :label="t('dashboard.notMonitored')"
                    />
                  </th>
                  <td>
                    <StatusBadge
                      :tone="state.reach === 'Reaching' ? 'low' : 'critical'"
                      :label="
                        state.reach === 'Reaching'
                          ? t('dashboard.reaching')
                          : state.reach === 'Stale'
                            ? t('monitoringHealth.stale')
                            : t('monitoringHealth.neverCollected')
                      "
                    />
                  </td>
                  <td>
                    <StatusBadge
                      v-if="state.highestSeverity"
                      :tone="severityTone(state.highestSeverity)"
                      :label="`${t(`severity.${state.highestSeverity.toLowerCase()}`)}: ${state.activeIncidents}`"
                    />
                    <span v-else class="muted">0</span>
                  </td>
                  <td>
                    {{
                      state.lastCollectedAt ? formatDateTime(state.lastCollectedAt, locale) : '—'
                    }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
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
.states {
  margin-top: var(--spacing-lg);
}

/* 監視が届いていないことは他の集計より重い知らせなので、明確に区切って出す */
.alert {
  padding: var(--spacing-md);
  margin-bottom: var(--spacing-lg);
  border: 1px solid var(--color-critical);
  border-radius: var(--radius);
  background-color: var(--color-critical-bg);
}

.alert__title {
  font-size: 1.0625rem;
  font-weight: 600;
  color: var(--color-critical);
  margin-bottom: var(--spacing-xs);
}

.alert__note {
  font-size: 0.875rem;
  margin-bottom: var(--spacing-sm);
}

.alert__list {
  list-style: none;
  padding: 0;
  margin: 0;
}

.alert__list li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  margin-bottom: var(--spacing-xs);
}

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
