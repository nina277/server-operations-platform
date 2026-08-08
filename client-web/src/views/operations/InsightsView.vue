<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import { extractErrorMessage } from '@/api/http'
import { fetchOperationsInsights } from '@/api/operations'
import type { DurationStats, OperationsInsights } from '@/types/operations'

/**
 * 運用実績サマリ。docs/verification.md の成功基準を画面から測れるようにする。
 * 集計するだけで何も変更しないため、ログイン済みなら役割を問わず開ける。
 */
const { t } = useI18n()

const data = ref<OperationsInsights | null>(null)
const loading = ref(true)
const errorMessage = ref<string | null>(null)

const PRESET_DAYS = [7, 30, 90]

/** input[type=date] へ入れるため YYYY-MM-DD にする。 */
function toDateInput(value: Date): string {
  return value.toISOString().slice(0, 10)
}

const to = ref(toDateInput(new Date()))
const from = ref(toDateInput(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000)))

async function load(): Promise<void> {
  loading.value = true
  errorMessage.value = null

  try {
    // 終了日はその日いっぱいを含める。当日を指定して当日分が出ないと分かりにくい。
    data.value = await fetchOperationsInsights(
      new Date(`${from.value}T00:00:00Z`).toISOString(),
      new Date(`${to.value}T23:59:59Z`).toISOString(),
    )
  } catch (e) {
    errorMessage.value = extractErrorMessage(e, t('common.error'))
    data.value = null
  } finally {
    loading.value = false
  }
}

onMounted(load)

function applyPreset(days: number): void {
  to.value = toDateInput(new Date())
  from.value = toDateInput(new Date(Date.now() - days * 24 * 60 * 60 * 1000))
  void load()
}

/** 割合は百分率にして小数1桁まで。nullは「—」で出す(0%と区別する)。 */
function formatRatio(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }
  return `${Math.round(value * 1000) / 10}%`
}

function formatSeconds(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }
  return `${value} ${t('insights.seconds')}`
}

const resolvedRatio = computed(() => {
  const d = data.value
  if (!d || d.incidentsDetected === 0) {
    return null
  }
  return d.incidentsResolved / d.incidentsDetected
})

/** 内訳は件数の多い順に出す。多いものから目に入るようにする。 */
function sortedEntries(record: Record<string, number>): [string, number][] {
  return Object.entries(record).sort((a, b) => b[1] - a[1])
}

function hasEntries(record: Record<string, number> | undefined): boolean {
  return record !== undefined && Object.keys(record).length > 0
}

/** 所要時間の分布を表の1行にまとめる。 */
function statsRow(stats: DurationStats): { label: string; value: string }[] {
  return [
    { label: t('insights.count'), value: String(stats.count) },
    { label: t('insights.average'), value: formatSeconds(stats.averageSeconds) },
    { label: t('insights.median'), value: formatSeconds(stats.medianSeconds) },
    { label: t('insights.p95'), value: formatSeconds(stats.p95Seconds) },
    { label: t('insights.max'), value: formatSeconds(stats.maxSeconds) },
  ]
}
</script>

<template>
  <div>
    <PageHeader :title="t('insights.title')" :description="t('insights.description')" />

    <form class="range" @submit.prevent="load">
      <div class="form-field">
        <label for="range-from">{{ t('insights.from') }}</label>
        <input id="range-from" v-model="from" type="date" required />
      </div>
      <div class="form-field">
        <label for="range-to">{{ t('insights.to') }}</label>
        <input id="range-to" v-model="to" type="date" required />
      </div>
      <button type="submit" class="button button--primary" :disabled="loading">
        {{ t('insights.apply') }}
      </button>
    </form>

    <div class="presets">
      <button
        v-for="days in PRESET_DAYS"
        :key="days"
        type="button"
        class="button"
        :disabled="loading"
        @click="applyPreset(days)"
      >
        {{ t('insights.presetDays', { days }) }}
      </button>
    </div>

    <p v-if="errorMessage" role="alert" class="message message--error">{{ errorMessage }}</p>
    <p v-if="loading" role="status">{{ t('common.loading') }}</p>

    <template v-else-if="data">
      <section aria-labelledby="detection-heading" class="section">
        <h2 id="detection-heading" class="section__title">{{ t('insights.detection') }}</h2>
        <p class="form-field__help">{{ t('insights.detectionHelp') }}</p>

        <p class="headline" data-testid="within-target">
          {{ t('insights.withinTarget', { seconds: data.notificationTargetSeconds }) }}:
          <strong>{{ formatRatio(data.notifiedWithinTargetRatio) }}</strong>
        </p>

        <dl class="definition">
          <template v-for="row in statsRow(data.detectionToNotification)" :key="row.label">
            <dt>{{ row.label }}</dt>
            <dd>{{ row.value }}</dd>
          </template>
        </dl>
      </section>

      <section aria-labelledby="incidents-heading" class="section">
        <h2 id="incidents-heading" class="section__title">{{ t('insights.incidents') }}</h2>

        <dl class="definition">
          <dt>{{ t('insights.detected') }}</dt>
          <dd data-testid="detected">{{ data.incidentsDetected }}</dd>
          <dt>{{ t('insights.resolved') }}</dt>
          <dd>{{ data.incidentsResolved }}</dd>
          <dt>{{ t('insights.resolvedRatio') }}</dt>
          <dd>{{ formatRatio(resolvedRatio) }}</dd>
        </dl>

        <div v-if="hasEntries(data.incidentsBySeverity)" class="table-scroll">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">{{ t('rules.classification') }}</th>
                <th scope="col">{{ t('insights.count') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="[key, value] in sortedEntries(data.incidentsBySeverity)" :key="key">
                <th scope="row">{{ t(`severity.${key.toLowerCase()}`) }}</th>
                <td>{{ value }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section aria-labelledby="recovery-heading" class="section">
        <h2 id="recovery-heading" class="section__title">{{ t('insights.recovery') }}</h2>
        <p class="form-field__help">{{ t('insights.recoveryHelp') }}</p>

        <dl class="definition">
          <template v-for="row in statsRow(data.recoveryDuration)" :key="row.label">
            <dt>{{ row.label }}</dt>
            <dd>{{ row.value }}</dd>
          </template>
        </dl>

        <h3 class="section__subtitle">{{ t('insights.autoRecovery') }}</h3>
        <p class="headline" data-testid="auto-success-ratio">
          {{ t('insights.autoSuccessRatio') }}:
          <strong>{{ formatRatio(data.autoRecoverySuccessRatio) }}</strong>
        </p>
        <p class="form-field__help">{{ t('insights.autoSuccessHelp') }}</p>

        <dl class="definition">
          <template v-for="row in statsRow(data.autoRecoveryDuration)" :key="row.label">
            <dt>{{ row.label }}</dt>
            <dd>{{ row.value }}</dd>
          </template>
        </dl>
      </section>

      <section aria-labelledby="blocked-heading" class="section">
        <h2 id="blocked-heading" class="section__title">{{ t('insights.blockedReasons') }}</h2>
        <p class="form-field__help">{{ t('insights.blockedReasonsHelp') }}</p>

        <div v-if="hasEntries(data.blockedReasons)" class="table-scroll">
          <table class="table" data-testid="blocked-reasons">
            <thead>
              <tr>
                <th scope="col">{{ t('incidents.blockedReason') }}</th>
                <th scope="col">{{ t('insights.count') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="[key, value] in sortedEntries(data.blockedReasons)" :key="key">
                <th scope="row">{{ key }}</th>
                <td>{{ value }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <p v-else class="muted">{{ t('insights.noData') }}</p>
      </section>
    </template>
  </div>
</template>

<style scoped>
.range,
.presets {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-md);
}

.range .form-field {
  margin-bottom: 0;
}

.section {
  margin-top: var(--spacing-xl);
  padding-top: var(--spacing-md);
  border-top: 1px solid var(--color-border);
}

.section__title {
  font-size: 1.125rem;
  font-weight: 600;
  margin-bottom: var(--spacing-sm);
}

.section__subtitle {
  font-size: 1rem;
  font-weight: 600;
  margin-top: var(--spacing-lg);
  margin-bottom: var(--spacing-sm);
}

.headline {
  font-size: 1.0625rem;
  margin: var(--spacing-sm) 0;
}

.definition {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: var(--spacing-xs) var(--spacing-md);
  margin: var(--spacing-md) 0;
}

.definition dt {
  color: var(--color-text-muted);
}

.message {
  padding: var(--spacing-sm);
  margin-bottom: var(--spacing-md);
  border: 1px solid currentColor;
  border-radius: var(--radius);
}

.message--error {
  color: var(--color-critical);
  background-color: var(--color-critical-bg);
}

.muted {
  color: var(--color-text-muted);
}
</style>
