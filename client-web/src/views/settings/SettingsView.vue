<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import SecretField from '@/components/common/SecretField.vue'
import { extractErrorMessage } from '@/api/http'
import {
  addNetworkCidr,
  deleteNetworkCidr,
  fetchBackupRuns,
  fetchNetworkCidrs,
  fetchProfile,
  fetchRetention,
  fetchSecretStatus,
  previewRetention,
  runBackup,
  testBackupConnection,
  updateProfile,
  updateRetention,
  updateSecret,
} from '@/api/settings'
import { fetchAiUsage, updateAiEnabled, updateAiLimits } from '@/api/operations'
import { formatBytes, formatDateTime, resultTone } from '@/utils/format'
import type { AiUsageSummary } from '@/types/operations'
import type {
  BackupRun,
  NetworkCidr,
  ProfileSettings,
  RetentionPreview,
  RetentionSettings,
  SecretStatus,
} from '@/types/settings'
import type { ConnectionTestResult } from '@/types/operations'

const { t, locale } = useI18n()

/** 画面で扱う秘密値の種別。値そのものは決して受け取らない。 */
const SECRET_KINDS = ['smtp-password', 'ai-api-key', 'backup-secret-key', 'fcm-service-account']

const profile = ref<ProfileSettings | null>(null)
const retention = ref<RetentionSettings | null>(null)
const retentionPreview = ref<RetentionPreview | null>(null)
const cidrs = ref<NetworkCidr[]>([])
const secrets = ref<SecretStatus[]>([])
const secretDrafts = ref<Record<string, string | null>>({})
const backupRuns = ref<BackupRun[]>([])
const aiUsage = ref<AiUsageSummary | null>(null)

const newCidr = ref({ cidr: '', description: '' })
const backupResult = ref<ConnectionTestResult | null>(null)

const loading = ref(true)
const busy = ref(false)
const message = ref<string | null>(null)
const errorMessage = ref<string | null>(null)

const retentionProfiles = ['compact', 'standard', 'long-term', 'custom'] as const

async function loadAll(): Promise<void> {
  loading.value = true
  errorMessage.value = null

  // 一部が取得できなくても、取れた設定だけは表示する
  const [
    profileResult,
    retentionResult,
    previewResult,
    cidrResult,
    backupResult_,
    aiResult,
    ...secretResults
  ] = await Promise.allSettled([
    fetchProfile(),
    fetchRetention(),
    previewRetention(),
    fetchNetworkCidrs(),
    fetchBackupRuns(),
    fetchAiUsage(),
    ...SECRET_KINDS.map((kind) => fetchSecretStatus(kind)),
  ])

  profile.value = profileResult.status === 'fulfilled' ? profileResult.value : null
  retention.value = retentionResult.status === 'fulfilled' ? retentionResult.value : null
  retentionPreview.value = previewResult.status === 'fulfilled' ? previewResult.value : null
  cidrs.value = cidrResult.status === 'fulfilled' ? cidrResult.value : []
  backupRuns.value = backupResult_.status === 'fulfilled' ? backupResult_.value : []
  aiUsage.value = aiResult.status === 'fulfilled' ? aiResult.value : null

  secrets.value = secretResults
    .map((result) => (result.status === 'fulfilled' ? result.value : null))
    .filter((value): value is SecretStatus => value !== null)

  if (profileResult.status === 'rejected') {
    errorMessage.value = extractErrorMessage(profileResult.reason, t('common.error'))
  }

  loading.value = false
}

onMounted(loadAll)

async function run(action: () => Promise<void>): Promise<void> {
  busy.value = true
  message.value = null
  errorMessage.value = null

  try {
    await action()
    message.value = t('common.saved')
  } catch (e) {
    errorMessage.value = extractErrorMessage(e, t('common.error'))
  } finally {
    busy.value = false
  }
}

const handleSaveProfile = () =>
  run(async () => {
    if (profile.value) {
      profile.value = await updateProfile(profile.value)
    }
  })

const handleSaveRetention = () =>
  run(async () => {
    if (retention.value) {
      retention.value = await updateRetention(retention.value)
      retentionPreview.value = await previewRetention()
    }
  })

const handleAddCidr = () =>
  run(async () => {
    await addNetworkCidr(
      newCidr.value.cidr,
      newCidr.value.description.length > 0 ? newCidr.value.description : undefined,
    )
    newCidr.value = { cidr: '', description: '' }
    cidrs.value = await fetchNetworkCidrs()
  })

const handleDeleteCidr = (id: number) =>
  run(async () => {
    await deleteNetworkCidr(id)
    cidrs.value = await fetchNetworkCidrs()
  })

const handleSaveSecret = (kind: string) =>
  run(async () => {
    const value = secretDrafts.value[kind]
    if (!value) {
      return
    }

    const updated = await updateSecret(kind, value)
    secrets.value = secrets.value.map((s) => (s.kind === kind ? updated : s))
    // 保存後は入力欄から値を消す(画面にも残さない)
    secretDrafts.value[kind] = null
  })

const handleTestBackup = () =>
  run(async () => {
    backupResult.value = await testBackupConnection()
  })

const handleRunBackup = () =>
  run(async () => {
    await runBackup()
    backupRuns.value = await fetchBackupRuns()
  })

const handleToggleAi = (isEnabled: boolean) =>
  run(async () => {
    aiUsage.value = await updateAiEnabled(isEnabled)
  })

const handleSaveAiLimits = () =>
  run(async () => {
    if (aiUsage.value) {
      aiUsage.value = await updateAiLimits({
        model: aiUsage.value.model,
        monthlyLimit: aiUsage.value.monthlyLimit,
        dailyLimit: aiUsage.value.dailyLimit,
        hourlyLimit: aiUsage.value.hourlyLimit,
        maxInputCharacters: aiUsage.value.maxInputCharacters,
        maxOutputTokens: aiUsage.value.maxOutputTokens,
        timeoutSeconds: 20,
      })
    }
  })
</script>

<template>
  <div>
    <PageHeader :title="t('nav.settings')" />

    <p v-if="errorMessage" role="alert" class="message message--error">{{ errorMessage }}</p>
    <p v-if="message" role="status" class="message message--ok">{{ message }}</p>
    <p v-if="loading" role="status">{{ t('common.loading') }}</p>

    <template v-else>
      <section v-if="profile" aria-labelledby="general-heading" class="section">
        <h2 id="general-heading" class="section__title">{{ t('settings.general') }}</h2>
        <form @submit.prevent="handleSaveProfile">
          <div class="form-field">
            <label for="system-name">{{ t('settings.systemName') }}</label>
            <input
              id="system-name"
              v-model="profile.systemName"
              type="text"
              required
              maxlength="100"
            />
          </div>
          <div class="form-field">
            <label for="default-language">{{ t('settings.defaultLanguage') }}</label>
            <select id="default-language" v-model="profile.language">
              <option value="ja">{{ t('common.japanese') }}</option>
              <option value="en">{{ t('common.english') }}</option>
            </select>
          </div>
          <button type="submit" class="button button--primary" :disabled="busy">
            {{ t('common.save') }}
          </button>
        </form>
      </section>

      <section v-if="retention" aria-labelledby="retention-heading" class="section">
        <h2 id="retention-heading" class="section__title">{{ t('settings.retention') }}</h2>
        <form @submit.prevent="handleSaveRetention">
          <div class="form-field">
            <label for="retention-profile">{{ t('settings.retentionProfile') }}</label>
            <select id="retention-profile" v-model="retention.profile">
              <option v-for="value in retentionProfiles" :key="value" :value="value">
                {{ value }}
              </option>
            </select>
          </div>

          <div class="grid">
            <div class="form-field">
              <label for="retention-metrics">{{ t('settings.metricsDays') }}</label>
              <input
                id="retention-metrics"
                v-model.number="retention.metricsDays"
                type="number"
                min="1"
                max="3650"
              />
            </div>
            <div class="form-field">
              <label for="retention-logs">{{ t('settings.logsDays') }}</label>
              <input
                id="retention-logs"
                v-model.number="retention.logsDays"
                type="number"
                min="1"
                max="3650"
              />
            </div>
            <div class="form-field">
              <label for="retention-incidents">{{ t('settings.incidentsDays') }}</label>
              <input
                id="retention-incidents"
                v-model.number="retention.incidentsDays"
                type="number"
                min="1"
                max="3650"
              />
            </div>
            <div class="form-field">
              <label for="retention-audit">{{ t('settings.auditDays') }}</label>
              <input
                id="retention-audit"
                v-model.number="retention.auditDays"
                type="number"
                min="1"
                max="3650"
              />
            </div>
          </div>

          <button type="submit" class="button button--primary" :disabled="busy">
            {{ t('common.save') }}
          </button>
        </form>

        <template v-if="retentionPreview">
          <h3 class="section__subtitle">{{ t('settings.retentionPreview') }}</h3>
          <p class="form-field__help">{{ t('settings.previewNote') }}</p>
          <dl class="definition">
            <dt>{{ t('settings.metricsDays') }}</dt>
            <dd>{{ retentionPreview.metricSnapshots }}</dd>
            <dt>{{ t('settings.logsDays') }}</dt>
            <dd>{{ retentionPreview.incidentLogs }}</dd>
            <dt>{{ t('settings.incidentsDays') }}</dt>
            <dd>{{ retentionPreview.incidents }}</dd>
            <dt>{{ t('settings.auditDays') }}</dt>
            <dd>{{ retentionPreview.auditLogs }}</dd>
          </dl>
        </template>
      </section>

      <section aria-labelledby="cidrs-heading" class="section">
        <h2 id="cidrs-heading" class="section__title">{{ t('settings.networkCidrs') }}</h2>
        <p class="form-field__help">{{ t('settings.networkCidrsHelp') }}</p>

        <form class="inline-form" @submit.prevent="handleAddCidr">
          <div class="form-field">
            <label for="new-cidr">{{ t('settings.cidr') }}</label>
            <input id="new-cidr" v-model="newCidr.cidr" type="text" required maxlength="64" />
          </div>
          <div class="form-field">
            <label for="new-cidr-description">{{ t('targets.description') }}</label>
            <input
              id="new-cidr-description"
              v-model="newCidr.description"
              type="text"
              maxlength="200"
            />
          </div>
          <button type="submit" class="button" :disabled="busy">{{ t('common.save') }}</button>
        </form>

        <div v-if="cidrs.length > 0" class="table-scroll">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">{{ t('settings.cidr') }}</th>
                <th scope="col">{{ t('targets.description') }}</th>
                <th scope="col">
                  <span class="sr-only">{{ t('common.execute') }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="cidr in cidrs" :key="cidr.id">
                <th scope="row">{{ cidr.cidr }}</th>
                <td>{{ cidr.description ?? '—' }}</td>
                <td>
                  <button
                    type="button"
                    class="button"
                    :disabled="busy"
                    @click="handleDeleteCidr(cidr.id)"
                  >
                    {{ t('common.cancel') }}
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p v-else class="muted">{{ t('common.empty') }}</p>
      </section>

      <section v-if="secrets.length > 0" aria-labelledby="secrets-heading" class="section">
        <h2 id="secrets-heading" class="section__title">{{ t('settings.secrets') }}</h2>

        <div v-for="secret in secrets" :key="secret.kind" class="secret-row">
          <SecretField
            :id="`secret-${secret.kind}`"
            :label="secret.kind"
            :configured="secret.isConfigured"
            :model-value="secretDrafts[secret.kind] ?? null"
            @update:model-value="(value) => (secretDrafts[secret.kind] = value)"
          />
          <button
            type="button"
            class="button"
            :disabled="busy || !secretDrafts[secret.kind]"
            @click="handleSaveSecret(secret.kind)"
          >
            {{ t('common.save') }}
          </button>
        </div>
      </section>

      <section aria-labelledby="backup-heading" class="section">
        <h2 id="backup-heading" class="section__title">{{ t('settings.backup') }}</h2>

        <div class="inline-form">
          <button type="button" class="button" :disabled="busy" @click="handleTestBackup">
            {{ t('targets.testConnection') }}
          </button>
          <button type="button" class="button" :disabled="busy" @click="handleRunBackup">
            {{ t('settings.runBackup') }}
          </button>
        </div>

        <p v-if="backupResult" role="status" class="result">
          <StatusBadge
            :tone="backupResult.success ? 'low' : 'critical'"
            :label="
              backupResult.success
                ? t('targets.connectionSucceeded')
                : t('targets.connectionFailed')
            "
          />
          <span>{{ backupResult.message }}</span>
        </p>

        <div v-if="backupRuns.length > 0" class="table-scroll">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">{{ t('settings.startedAt') }}</th>
                <th scope="col">{{ t('settings.result') }}</th>
                <th scope="col">{{ t('settings.size') }}</th>
                <th scope="col">{{ t('incidents.resultMessage') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="backup in backupRuns" :key="backup.id">
                <th scope="row">{{ formatDateTime(backup.startedAt, locale) }}</th>
                <td><StatusBadge :tone="resultTone(backup.status)" :label="backup.status" /></td>
                <td>{{ formatBytes(backup.sizeBytes) }}</td>
                <td class="table__title">{{ backup.message ?? '—' }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section v-if="aiUsage" aria-labelledby="ai-heading" class="section">
        <h2 id="ai-heading" class="section__title">{{ t('settings.ai') }}</h2>

        <div class="form-field form-field--inline">
          <input
            id="ai-enabled"
            type="checkbox"
            :checked="aiUsage.isEnabled"
            :disabled="busy"
            aria-describedby="ai-enabled-help"
            @change="handleToggleAi(($event.target as HTMLInputElement).checked)"
          />
          <label for="ai-enabled">{{ t('settings.aiEnabled') }}</label>
        </div>
        <p id="ai-enabled-help" class="form-field__help">{{ t('settings.aiEnabledHelp') }}</p>

        <dl class="definition">
          <dt>{{ t('settings.provider') }}</dt>
          <dd>{{ aiUsage.provider }}</dd>
          <dt>{{ t('settings.hourlyLimit') }}</dt>
          <dd>
            {{ t('settings.used', { used: aiUsage.hourlyUsed, limit: aiUsage.hourlyLimit }) }}
          </dd>
          <dt>{{ t('settings.dailyLimit') }}</dt>
          <dd>{{ t('settings.used', { used: aiUsage.dailyUsed, limit: aiUsage.dailyLimit }) }}</dd>
          <dt>{{ t('settings.monthlyLimit') }}</dt>
          <dd>
            {{ t('settings.used', { used: aiUsage.monthlyUsed, limit: aiUsage.monthlyLimit }) }}
          </dd>
        </dl>

        <form @submit.prevent="handleSaveAiLimits">
          <div class="grid">
            <div class="form-field">
              <label for="ai-model">{{ t('settings.model') }}</label>
              <input id="ai-model" v-model="aiUsage.model" type="text" maxlength="64" />
            </div>
            <div class="form-field">
              <label for="ai-hourly">{{ t('settings.hourlyLimit') }}</label>
              <input
                id="ai-hourly"
                v-model.number="aiUsage.hourlyLimit"
                type="number"
                min="1"
                max="100"
              />
            </div>
            <div class="form-field">
              <label for="ai-daily">{{ t('settings.dailyLimit') }}</label>
              <input
                id="ai-daily"
                v-model.number="aiUsage.dailyLimit"
                type="number"
                min="1"
                max="1000"
              />
            </div>
            <div class="form-field">
              <label for="ai-monthly">{{ t('settings.monthlyLimit') }}</label>
              <input
                id="ai-monthly"
                v-model.number="aiUsage.monthlyLimit"
                type="number"
                min="1"
                max="10000"
              />
            </div>
            <div class="form-field">
              <label for="ai-input">{{ t('settings.maxInputCharacters') }}</label>
              <input
                id="ai-input"
                v-model.number="aiUsage.maxInputCharacters"
                type="number"
                min="100"
                max="100000"
              />
            </div>
            <div class="form-field">
              <label for="ai-output">{{ t('settings.maxOutputTokens') }}</label>
              <input
                id="ai-output"
                v-model.number="aiUsage.maxOutputTokens"
                type="number"
                min="50"
                max="8000"
              />
            </div>
          </div>

          <button type="submit" class="button button--primary" :disabled="busy">
            {{ t('common.save') }}
          </button>
        </form>

        <template v-if="aiUsage.recentCalls.length > 0">
          <h3 class="section__subtitle">{{ t('settings.recentCalls') }}</h3>
          <div class="table-scroll">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">{{ t('settings.calledAt') }}</th>
                  <th scope="col">{{ t('settings.result') }}</th>
                  <th scope="col">{{ t('settings.inputCharacters') }}</th>
                  <th scope="col">{{ t('settings.outputTokens') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="call in aiUsage.recentCalls" :key="call.id">
                  <th scope="row">{{ formatDateTime(call.calledAt, locale) }}</th>
                  <td><StatusBadge :tone="resultTone(call.result)" :label="call.result" /></td>
                  <td>{{ call.inputCharacters }}</td>
                  <td>{{ call.outputTokens ?? '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </template>
      </section>
    </template>
  </div>
</template>

<style scoped>
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip-path: inset(50%);
  white-space: nowrap;
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

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  gap: var(--spacing-md);
}

.inline-form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-md);
}

.inline-form .form-field {
  margin-bottom: 0;
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

.secret-row {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-lg);
}

.result {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  margin-bottom: var(--spacing-md);
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

.form-field--inline {
  flex-direction: row;
  align-items: center;
}

.muted {
  color: var(--color-text-muted);
}
</style>
