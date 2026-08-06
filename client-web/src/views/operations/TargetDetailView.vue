<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import MetricChart, { type ChartPoint } from '@/components/common/MetricChart.vue'
import AsyncState from '@/components/common/AsyncState.vue'
import SecretField from '@/components/common/SecretField.vue'
import { extractErrorMessage } from '@/api/http'
import {
  fetchAdapterTemplates,
  fetchTarget,
  fetchTargetCapabilities,
  fetchTargetLogs,
  fetchTargetMetrics,
  runHealthCheck,
  testTargetConnection,
  updateTarget,
  previewDeleteTarget,
  deleteTarget,
} from '@/api/operations'
import { useAsyncData } from '@/composables/useAsyncData'
import { useAuthStore } from '@/stores/auth'
import { formatDateTime, resultTone, toOptionalNumber } from '@/utils/format'
import type {
  AdapterTemplate,
  ConnectionTestResult,
  HealthCheck,
  IncidentLog,
  MetricSnapshot,
  Target,
  TargetDeletePreview,
} from '@/types/operations'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const targetId = computed(() => Number(route.params.id))

const { data, loading, error, forbidden, load } = useAsyncData(
  () => fetchTarget(targetId.value),
  t('common.error'),
)

const capabilities = ref<Awaited<ReturnType<typeof fetchTargetCapabilities>> | null>(null)
const template = ref<AdapterTemplate | null>(null)
const metrics = ref<MetricSnapshot[]>([])
const logs = ref<IncidentLog[]>([])

// 編集内容。読み込んだ対象から複製し、保存するまで元の値は変えない。
const draft = ref<{
  name: string
  description: string
  isEnabled: boolean
  autoRecoveryEnabled: boolean
  /** 収集間隔(秒)。空文字は「全体の既定値に従う」を意味する。 */
  collectionIntervalText: string
  /** この対象で行う収集の種類。 */
  enabledMonitors: string[]
  allowedContainersText: string
  settings: Record<string, string>
  credentials: Record<string, string | null>
} | null>(null)

// --- 削除 ---
// 削除は元に戻せない。何が消えるかを見てから決められるようにする。
const deletePreview = ref<TargetDeletePreview | null>(null)
const deleting = ref(false)
const deleteError = ref<string | null>(null)

const saving = ref(false)
const saveMessage = ref<string | null>(null)
const saveError = ref<string | null>(null)

const connectionResult = ref<ConnectionTestResult | null>(null)
const connectionError = ref<string | null>(null)
const testing = ref(false)

const healthResult = ref<HealthCheck | null>(null)
const healthError = ref<string | null>(null)
const checking = ref(false)

/**
 * 収集値から折れ線に載せられる系列を取り出す。
 * payloadJsonの形は収集の種類ごとに決まっているため、種類で見分ける。
 * 壊れた値が1件混ざっても他の点は描けるよう、点ごとに握りつぶす。
 */
function seriesFrom(kind: string, extract: (payload: unknown) => number | null): ChartPoint[] {
  const points: ChartPoint[] = []

  for (const metric of metrics.value) {
    if (metric.kind !== kind || metric.payloadJson === null) {
      continue
    }

    try {
      const value = extract(JSON.parse(metric.payloadJson))
      if (value !== null && Number.isFinite(value)) {
        points.push({ at: metric.collectedAt, value })
      }
    } catch {
      // この点だけ落として続ける
    }
  }

  return points
}

/** HTTP監視の応答時間。じりじり悪化しているのか急に落ちたのかを見分けられる。 */
const latencyPoints = computed(() =>
  seriesFrom('http', (payload) => {
    const value = (payload as { latencyMs?: unknown }).latencyMs
    return typeof value === 'number' ? value : null
  }),
)

/** 動いていないコンテナの数。0でない状態が続いているかを見る。 */
const stoppedContainerPoints = computed(() =>
  seriesFrom('docker', (payload) => {
    if (!Array.isArray(payload)) {
      return null
    }

    return payload.filter((c) => {
      const state = (c as { state?: unknown }).state
      return typeof state === 'string' && state.toLowerCase() !== 'running'
    }).length
  }),
)

/**
 * 収集した使用率のうち、そのときいちばん高かったコンテナの値を取る。
 *
 * 平均にすると、1つのコンテナが上限に張り付いていても
 * 他が空いていれば低く見えてしまい、逼迫が消える。
 * 手当てが要るのは最も高いコンテナなので、最大値を追う。
 */
function peakResource(field: 'cpuUsagePercent' | 'memoryUsagePercent'): ChartPoint[] {
  return seriesFrom('resource', (payload) => {
    const containers = (payload as { containers?: unknown }).containers
    if (!Array.isArray(containers)) {
      return null
    }

    const values = containers
      .map((c) => (c as Record<string, unknown>)[field])
      .filter((v): v is number => typeof v === 'number')

    // 1件も取れていない収集は点を落とす。0として描くと「使っていない」に見える
    return values.length === 0 ? null : Math.max(...values)
  })
}

const cpuPoints = computed(() => peakResource('cpuUsagePercent'))
const memoryPoints = computed(() => peakResource('memoryUsagePercent'))

/** テンプレートのうち秘密でない入力。値は設定画面で編集できる。 */
async function handlePreviewDelete(): Promise<void> {
  deleting.value = true
  deleteError.value = null

  try {
    deletePreview.value = await previewDeleteTarget(targetId.value)
  } catch (e) {
    deleteError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    deleting.value = false
  }
}

async function handleDelete(): Promise<void> {
  deleting.value = true
  deleteError.value = null

  try {
    await deleteTarget(targetId.value)
    await router.push({ name: 'targets' })
  } catch (e) {
    deleteError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    deleting.value = false
  }
}

/**
 * 入切できる収集の種類。テンプレートの能力から作る。
 * 「推奨する監視項目」は案内の文章であり、選択肢ではない。
 */
const collectableMonitors = computed(() => capabilities.value?.collectableMonitors ?? [])

const plainInputs = computed(() => template.value?.inputs.filter((i) => !i.secret) ?? [])
const secretInputs = computed(() => template.value?.inputs.filter((i) => i.secret) ?? [])

function resetDraft(target: Target): void {
  draft.value = {
    name: target.name,
    description: target.description ?? '',
    isEnabled: target.isEnabled,
    autoRecoveryEnabled: target.autoRecoveryEnabled,
    collectionIntervalText:
      target.collectionIntervalSeconds === null ? '' : String(target.collectionIntervalSeconds),
    enabledMonitors: [...target.enabledMonitors],
    allowedContainersText: target.allowedContainers.join('\n'),
    settings: { ...target.settings },
    credentials: {},
  }
}

watch(data, (target) => {
  if (target) {
    resetDraft(target)
  }
})

async function loadAll(): Promise<void> {
  await load()
  if (data.value === null) {
    return
  }

  // 付随情報は取得できなくても本体の表示は続ける
  const [caps, templates, metricList, logList] = await Promise.allSettled([
    fetchTargetCapabilities(targetId.value),
    fetchAdapterTemplates(),
    fetchTargetMetrics(targetId.value, 20),
    fetchTargetLogs(targetId.value, 20),
  ])

  capabilities.value = caps.status === 'fulfilled' ? caps.value : null
  template.value =
    templates.status === 'fulfilled'
      ? (templates.value.find((x) => x.id === data.value?.templateId) ?? null)
      : null
  metrics.value = metricList.status === 'fulfilled' ? metricList.value : []
  logs.value = logList.status === 'fulfilled' ? logList.value : []
}

onMounted(loadAll)

async function handleSave(): Promise<void> {
  if (draft.value === null) {
    return
  }

  saving.value = true
  saveMessage.value = null
  saveError.value = null

  try {
    // 空行を除いてコンテナ名の一覧にする
    const allowedContainers = draft.value.allowedContainersText
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => line.length > 0)

    // 入力された秘密値だけ送る(未入力のものは既存の値が維持される)
    const credentials: Record<string, string> = {}
    for (const [key, value] of Object.entries(draft.value.credentials)) {
      if (value !== null && value.length > 0) {
        credentials[key] = value
      }
    }

    const updated = await updateTarget(targetId.value, {
      name: draft.value.name,
      description: draft.value.description.length > 0 ? draft.value.description : null,
      isEnabled: draft.value.isEnabled,
      autoRecoveryEnabled: draft.value.autoRecoveryEnabled,
      collectionIntervalSeconds: toOptionalNumber(draft.value.collectionIntervalText),
      enabledMonitors: draft.value.enabledMonitors,
      allowedContainers,
      settings: draft.value.settings,
      credentials,
    })

    data.value = updated
    resetDraft(updated)
    saveMessage.value = t('common.saved')
  } catch (e) {
    saveError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    saving.value = false
  }
}

async function handleTestConnection(): Promise<void> {
  testing.value = true
  connectionResult.value = null
  connectionError.value = null

  try {
    connectionResult.value = await testTargetConnection(targetId.value)
  } catch (e) {
    connectionError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    testing.value = false
  }
}

async function handleHealthCheck(): Promise<void> {
  checking.value = true
  healthResult.value = null
  healthError.value = null

  try {
    healthResult.value = await runHealthCheck(targetId.value)
  } catch (e) {
    healthError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    checking.value = false
  }
}
</script>

<template>
  <div>
    <PageHeader :title="data?.name ?? t('nav.targets')" :description="data?.templateId" />

    <AsyncState
      :loading="loading"
      :error="error"
      :forbidden="forbidden"
      :empty="data === null"
      @retry="loadAll"
    >
      <template v-if="data && draft">
        <section aria-labelledby="checks-heading" class="section">
          <h2 id="checks-heading" class="section__title">{{ t('targets.healthCheck') }}</h2>

          <div class="actions">
            <button type="button" class="button" :disabled="checking" @click="handleHealthCheck">
              {{ t('targets.healthCheck') }}
            </button>
            <button
              v-if="auth.isAdmin"
              type="button"
              class="button"
              :disabled="testing"
              @click="handleTestConnection"
            >
              {{ t('targets.testConnection') }}
            </button>
          </div>

          <p v-if="healthError" role="alert" class="message message--error">{{ healthError }}</p>
          <p v-if="healthResult" role="status" class="result">
            <StatusBadge :tone="resultTone(healthResult.status)" :label="healthResult.status" />
            <span>{{ healthResult.message }}</span>
            <span v-if="healthResult.latencyMs !== null">
              {{ t('targets.latency') }}: {{ healthResult.latencyMs }} ms
            </span>
          </p>

          <p v-if="connectionError" role="alert" class="message message--error">
            {{ connectionError }}
          </p>
          <p v-if="connectionResult" role="status" class="result">
            <StatusBadge
              :tone="connectionResult.success ? 'low' : 'critical'"
              :label="
                connectionResult.success
                  ? t('targets.connectionSucceeded')
                  : t('targets.connectionFailed')
              "
            />
            <span>{{ connectionResult.message }}</span>
            <span v-if="connectionResult.latencyMs !== null">
              {{ t('targets.latency') }}: {{ connectionResult.latencyMs }} ms
            </span>
          </p>
        </section>

        <section v-if="capabilities" aria-labelledby="capabilities-heading" class="section">
          <h2 id="capabilities-heading" class="section__title">{{ t('targets.capabilities') }}</h2>
          <dl class="definition">
            <dt>{{ t('targets.allowedOperations') }}</dt>
            <dd>{{ capabilities.allowedOperations.join(', ') || '—' }}</dd>
            <dt>{{ t('targets.recommendedMonitors') }}</dt>
            <dd>{{ capabilities.recommendedMonitors.join(', ') || '—' }}</dd>
          </dl>
        </section>

        <form v-if="auth.isAdmin" class="section" @submit.prevent="handleSave">
          <h2 class="section__title">{{ t('targets.edit') }}</h2>

          <p v-if="saveError" role="alert" class="message message--error">{{ saveError }}</p>
          <p v-if="saveMessage" role="status" class="message message--ok">{{ saveMessage }}</p>

          <div class="form-field">
            <label for="target-name">{{ t('targets.name') }}</label>
            <input id="target-name" v-model="draft.name" type="text" required maxlength="100" />
          </div>

          <div class="form-field">
            <label for="target-description">{{ t('targets.description') }}</label>
            <input
              id="target-description"
              v-model="draft.description"
              type="text"
              maxlength="500"
            />
          </div>

          <div class="form-field form-field--inline">
            <input id="target-enabled" v-model="draft.isEnabled" type="checkbox" />
            <label for="target-enabled">{{ t('targets.isEnabled') }}</label>
          </div>

          <div class="form-field form-field--inline">
            <input
              id="target-auto-recovery"
              v-model="draft.autoRecoveryEnabled"
              type="checkbox"
              aria-describedby="target-auto-recovery-help"
            />
            <label for="target-auto-recovery">{{ t('targets.autoRecovery') }}</label>
          </div>
          <p id="target-auto-recovery-help" class="form-field__help">
            {{ t('targets.autoRecoveryHelp') }}
          </p>

          <div class="form-field">
            <label for="target-interval">{{ t('targets.collectionInterval') }}</label>
            <input
              id="target-interval"
              v-model="draft.collectionIntervalText"
              type="number"
              min="60"
              max="3600"
              :placeholder="t('targets.collectionIntervalDefault')"
              aria-describedby="target-interval-help"
              data-testid="collection-interval"
            />
            <p id="target-interval-help" class="form-field__help">
              {{ t('targets.collectionIntervalHelp') }}
            </p>
          </div>

          <fieldset v-if="collectableMonitors.length > 0" class="monitors">
            <legend>{{ t('targets.monitors') }}</legend>
            <p class="form-field__help">{{ t('targets.monitorsHelp') }}</p>

            <div
              v-for="monitor in collectableMonitors"
              :key="monitor"
              class="form-field form-field--inline"
            >
              <input
                :id="`monitor-${monitor}`"
                v-model="draft.enabledMonitors"
                type="checkbox"
                :value="monitor"
                :data-testid="`monitor-${monitor}`"
              />
              <label :for="`monitor-${monitor}`">{{ t(`monitorKinds.${monitor}`) }}</label>
            </div>

            <p v-if="draft.enabledMonitors.length === 0" class="form-field__help">
              {{ t('targets.monitorsNoneNote') }}
            </p>
          </fieldset>

          <div class="form-field">
            <label for="target-containers">{{ t('targets.allowedContainers') }}</label>
            <textarea
              id="target-containers"
              v-model="draft.allowedContainersText"
              rows="4"
              :placeholder="t('targets.allowedContainersPlaceholder')"
              aria-describedby="target-containers-help"
            ></textarea>
            <p id="target-containers-help" class="form-field__help">
              {{ t('targets.allowedContainersHelp') }}
            </p>
          </div>

          <div v-for="input in plainInputs" :key="input.key" class="form-field">
            <label :for="`setting-${input.key}`">{{ input.label }}</label>
            <input
              :id="`setting-${input.key}`"
              v-model="draft.settings[input.key]"
              type="text"
              :required="input.required"
            />
            <p class="form-field__help">{{ input.description }}</p>
          </div>

          <SecretField
            v-for="input in secretInputs"
            :key="input.key"
            :id="`credential-${input.key}`"
            :label="input.label"
            :configured="data.configuredCredentials.includes(input.key)"
            :model-value="draft.credentials[input.key] ?? null"
            class="form-field"
            @update:model-value="(value) => (draft!.credentials[input.key] = value)"
          />

          <button type="submit" class="button button--primary" :disabled="saving">
            {{ t('common.save') }}
          </button>
        </form>

        <section aria-labelledby="metrics-heading" class="section">
          <h2 id="metrics-heading" class="section__title">{{ t('targets.metrics') }}</h2>

          <MetricChart
            v-if="latencyPoints.length > 0"
            :title="t('targets.latency')"
            :points="latencyPoints"
            unit="ms"
            data-testid="latency-chart"
          />
          <MetricChart
            v-if="stoppedContainerPoints.length > 0"
            :title="t('targets.stoppedContainers')"
            :points="stoppedContainerPoints"
            data-testid="stopped-chart"
          />
          <MetricChart
            v-if="cpuPoints.length > 0"
            :title="t('targets.peakCpu')"
            :points="cpuPoints"
            unit="%"
            data-testid="cpu-chart"
          />
          <MetricChart
            v-if="memoryPoints.length > 0"
            :title="t('targets.peakMemory')"
            :points="memoryPoints"
            unit="%"
            data-testid="memory-chart"
          />

          <div v-if="metrics.length > 0" class="table-scroll">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">{{ t('settings.calledAt') }}</th>
                  <th scope="col">{{ t('rules.ruleType') }}</th>
                  <th scope="col">{{ t('settings.result') }}</th>
                  <th scope="col">{{ t('auditLogs.details') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="metric in metrics" :key="metric.id">
                  <td>{{ formatDateTime(metric.collectedAt, locale) }}</td>
                  <td>{{ metric.kind }}</td>
                  <td>
                    <StatusBadge :tone="resultTone(metric.status)" :label="metric.status" />
                  </td>
                  <td class="table__title">
                    {{ metric.errorMessage ?? metric.payloadJson ?? '—' }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="muted">{{ t('common.empty') }}</p>
        </section>

        <section
          v-if="auth.isAdmin"
          aria-labelledby="delete-heading"
          class="section section--danger"
          data-testid="delete-section"
        >
          <h2 id="delete-heading" class="section__title">{{ t('targets.delete') }}</h2>
          <p class="form-field__help">{{ t('targets.deleteHelp') }}</p>

          <p v-if="deleteError" role="alert" class="message message--error">{{ deleteError }}</p>

          <button
            v-if="deletePreview === null"
            type="button"
            class="button"
            :disabled="deleting"
            data-testid="preview-delete"
            @click="handlePreviewDelete"
          >
            {{ t('targets.deletePreview') }}
          </button>

          <div v-else data-testid="delete-preview">
            <p>{{ t('targets.deleteConfirm', { name: deletePreview.targetName }) }}</p>

            <dl class="definition">
              <dt>{{ t('nav.incidents') }}</dt>
              <dd>{{ deletePreview.incidents }}</dd>
              <dt>{{ t('targets.metrics') }}</dt>
              <dd>{{ deletePreview.metricSnapshots }}</dd>
              <dt>{{ t('incidents.recoveryActions') }}</dt>
              <dd>{{ deletePreview.recoveryActions }}</dd>
              <dt>{{ t('nav.notifications') }}</dt>
              <dd>{{ deletePreview.notifications }}</dd>
            </dl>

            <p class="form-field__help">{{ t('targets.deleteAuditNote') }}</p>

            <div class="inline-form">
              <button
                type="button"
                class="button button--danger"
                :disabled="deleting"
                data-testid="confirm-delete"
                @click="handleDelete"
              >
                {{ t('targets.deleteExecute') }}
              </button>
              <button type="button" class="button" @click="deletePreview = null">
                {{ t('common.cancel') }}
              </button>
            </div>
          </div>
        </section>

        <section aria-labelledby="logs-heading" class="section">
          <h2 id="logs-heading" class="section__title">{{ t('targets.logs') }}</h2>
          <ul v-if="logs.length > 0" class="logs">
            <li v-for="log in logs" :key="log.id">
              <span class="logs__time">{{ formatDateTime(log.collectedAt, locale) }}</span>
              <span class="logs__source">{{ log.source }}</span>
              <span class="logs__content">{{ log.maskedContent }}</span>
            </li>
          </ul>
          <p v-else class="muted">{{ t('common.empty') }}</p>
        </section>
      </template>
    </AsyncState>
  </div>
</template>

<style scoped>
.section {
  margin-top: var(--spacing-xl);
}

.section__title {
  font-size: 1.125rem;
  font-weight: 600;
  margin-bottom: var(--spacing-sm);
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
}

.result {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  margin-top: var(--spacing-sm);
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

.message--ok {
  color: var(--color-low);
  background-color: var(--color-low-bg);
}

.monitors {
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  padding: var(--spacing-md);
  margin-bottom: var(--spacing-md);
}

.monitors legend {
  font-weight: 600;
  padding: 0 var(--spacing-xs);
}

.form-field--inline {
  flex-direction: row;
  align-items: center;
  margin-bottom: var(--spacing-sm);
}

.definition {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: var(--spacing-xs) var(--spacing-md);
}

.definition dt {
  color: var(--color-text-muted);
}

.logs {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  padding: var(--spacing-sm);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background-color: var(--color-surface);
  font-family: ui-monospace, monospace;
  font-size: 0.8125rem;
  overflow-x: auto;
}

.logs__time,
.logs__source {
  color: var(--color-text-muted);
  margin-right: var(--spacing-sm);
}

.muted {
  color: var(--color-text-muted);
}
</style>
