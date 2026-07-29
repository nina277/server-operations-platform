<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import AsyncState from '@/components/common/AsyncState.vue'
import { RouterLink } from 'vue-router'
import { extractErrorMessage } from '@/api/http'
import {
  fetchDiagnosticRules,
  setDiagnosticRuleEnabled,
  testDiagnosticRules,
} from '@/api/operations'
import { useAsyncData } from '@/composables/useAsyncData'
import { useAuthStore } from '@/stores/auth'
import { severityTone } from '@/utils/format'
import type { RuleTestMatch } from '@/types/operations'

const { t } = useI18n()
const auth = useAuthStore()

const { data, loading, error, forbidden, load } = useAsyncData(
  fetchDiagnosticRules,
  t('common.error'),
)

// ルール試験の入力。空欄は「その値を渡さない」を意味する。
const form = ref({
  containerState: '',
  containerName: '',
  restartCount: '',
  memoryUsagePercent: '',
  diskUsagePercent: '',
  httpStatus: '',
  httpLatencyMs: '',
  logExcerpt: '',
})

const matches = ref<RuleTestMatch[] | null>(null)
const testing = ref(false)
const testError = ref<string | null>(null)
const toggling = ref(false)

onMounted(load)

/** ルールを消さずに止める。切り替えは監査に残る。 */
async function handleToggleEnabled(id: number, isEnabled: boolean): Promise<void> {
  toggling.value = true

  try {
    await setDiagnosticRuleEnabled(id, isEnabled)
    await load()
  } catch (e) {
    error.value = extractErrorMessage(e, t('common.error'))
  } finally {
    toggling.value = false
  }
}

function toNumber(value: string): number | null {
  if (value.trim().length === 0) {
    return null
  }
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

function toText(value: string): string | null {
  return value.trim().length > 0 ? value : null
}

async function handleTest(): Promise<void> {
  testing.value = true
  testError.value = null
  matches.value = null

  try {
    const response = await testDiagnosticRules({
      containerState: toText(form.value.containerState),
      containerName: toText(form.value.containerName),
      restartCount: toNumber(form.value.restartCount),
      memoryUsagePercent: toNumber(form.value.memoryUsagePercent),
      diskUsagePercent: toNumber(form.value.diskUsagePercent),
      httpStatus: toNumber(form.value.httpStatus),
      httpLatencyMs: toNumber(form.value.httpLatencyMs),
      logExcerpt: toText(form.value.logExcerpt),
    })
    matches.value = response.matches
  } catch (e) {
    testError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    testing.value = false
  }
}
</script>

<template>
  <div>
    <PageHeader :title="t('nav.rules')">
      <template #actions>
        <RouterLink v-if="auth.isAdmin" class="button button--primary" :to="{ name: 'rule-new' }">
          {{ t('rules.add') }}
        </RouterLink>
      </template>
    </PageHeader>

    <AsyncState
      :loading="loading"
      :error="error"
      :forbidden="forbidden"
      :empty="data !== null && data.length === 0"
      @retry="load"
    >
      <div v-if="data" class="table-scroll">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">{{ t('rules.name') }}</th>
              <th scope="col">{{ t('rules.classification') }}</th>
              <th scope="col">{{ t('rules.ruleType') }}</th>
              <th scope="col">{{ t('confirmDialog.risk') }}</th>
              <th scope="col">{{ t('rules.recommendedAction') }}</th>
              <th scope="col">{{ t('rules.priority') }}</th>
              <th scope="col">{{ t('rules.enabled') }}</th>
              <th v-if="auth.isAdmin" scope="col">
                <span class="sr-only">{{ t('common.execute') }}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="rule in data" :key="rule.id">
              <th scope="row" class="table__title">
                <RouterLink
                  v-if="auth.isAdmin"
                  :to="{ name: 'rule-edit', params: { id: rule.id } }"
                >
                  {{ rule.name }}
                </RouterLink>
                <template v-else>{{ rule.name }}</template>
              </th>
              <td>{{ rule.classification }}</td>
              <td>{{ rule.ruleType }}</td>
              <td>
                <StatusBadge
                  :tone="severityTone(rule.severity)"
                  :label="t(`severity.${rule.severity.toLowerCase()}`)"
                />
              </td>
              <td>{{ rule.recommendedActionId ?? '—' }}</td>
              <td>{{ rule.priority }}</td>
              <td>
                <StatusBadge
                  :tone="rule.isEnabled ? 'low' : 'neutral'"
                  :label="rule.isEnabled ? t('rules.enabled') : t('rules.disabled')"
                />
              </td>
              <td v-if="auth.isAdmin">
                <button
                  type="button"
                  class="button"
                  :disabled="toggling"
                  @click="handleToggleEnabled(rule.id, !rule.isEnabled)"
                >
                  {{ rule.isEnabled ? t('rules.disabled') : t('rules.enabled') }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </AsyncState>

    <section v-if="auth.isAdmin" aria-labelledby="rule-test-heading" class="section">
      <h2 id="rule-test-heading" class="section__title">{{ t('rules.test') }}</h2>
      <p class="form-field__help">{{ t('rules.testDescription') }}</p>

      <form class="test-form" @submit.prevent="handleTest">
        <div class="form-field">
          <label for="test-container-state">{{ t('rules.containerState') }}</label>
          <input id="test-container-state" v-model="form.containerState" type="text" />
        </div>
        <div class="form-field">
          <label for="test-container-name">{{ t('rules.containerName') }}</label>
          <input id="test-container-name" v-model="form.containerName" type="text" />
        </div>
        <div class="form-field">
          <label for="test-restart-count">{{ t('rules.restartCount') }}</label>
          <input id="test-restart-count" v-model="form.restartCount" type="number" min="0" />
        </div>
        <div class="form-field">
          <label for="test-memory">{{ t('rules.memoryUsagePercent') }}</label>
          <input
            id="test-memory"
            v-model="form.memoryUsagePercent"
            type="number"
            min="0"
            max="100"
          />
        </div>
        <div class="form-field">
          <label for="test-disk">{{ t('rules.diskUsagePercent') }}</label>
          <input id="test-disk" v-model="form.diskUsagePercent" type="number" min="0" max="100" />
        </div>
        <div class="form-field">
          <label for="test-http-status">{{ t('rules.httpStatus') }}</label>
          <input id="test-http-status" v-model="form.httpStatus" type="number" min="0" />
        </div>
        <div class="form-field">
          <label for="test-http-latency">{{ t('rules.httpLatencyMs') }}</label>
          <input id="test-http-latency" v-model="form.httpLatencyMs" type="number" min="0" />
        </div>
        <div class="form-field form-field--wide">
          <label for="test-log">{{ t('rules.logExcerpt') }}</label>
          <textarea id="test-log" v-model="form.logExcerpt" rows="3"></textarea>
        </div>

        <button type="submit" class="button button--primary" :disabled="testing">
          {{ t('rules.run') }}
        </button>
      </form>

      <p v-if="testError" role="alert" class="message message--error">{{ testError }}</p>

      <template v-if="matches !== null">
        <h3 class="section__subtitle">{{ t('rules.matches') }}</h3>
        <ul v-if="matches.length > 0" class="cards">
          <li v-for="match in matches" :key="match.ruleId" class="card">
            <div class="card__head">
              <strong>{{ match.ruleName }}</strong>
              <StatusBadge
                :tone="severityTone(match.severity)"
                :label="t(`severity.${match.severity.toLowerCase()}`)"
              />
            </div>
            <p>{{ match.rationale }}</p>
            <p v-if="match.recommendedActionId" class="muted">
              {{ t('rules.recommendedAction') }}: {{ match.recommendedActionId }}
            </p>
          </li>
        </ul>
        <p v-else role="status" class="muted">{{ t('rules.noMatches') }}</p>
      </template>
    </section>
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
}

.test-form {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
  gap: var(--spacing-md);
  align-items: end;
  margin-top: var(--spacing-md);
}

.test-form .form-field {
  margin-bottom: 0;
}

.form-field--wide {
  grid-column: 1 / -1;
}

.cards {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
  margin-top: var(--spacing-sm);
}

.card {
  padding: var(--spacing-md);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background-color: var(--color-surface);
}

.card__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  margin-bottom: var(--spacing-xs);
}

.message {
  padding: var(--spacing-sm);
  margin-top: var(--spacing-md);
  border: 1px solid currentColor;
  border-radius: var(--radius);
  color: var(--color-critical);
  background-color: var(--color-critical-bg);
}

.muted {
  color: var(--color-text-muted);
  font-size: 0.875rem;
}
</style>
