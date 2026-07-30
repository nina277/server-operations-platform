<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import AsyncState from '@/components/common/AsyncState.vue'
import { extractErrorMessage } from '@/api/http'
import {
  createDiagnosticRule,
  fetchDiagnosticRule,
  fetchRuleEditorOptions,
  testDiagnosticRules,
  updateDiagnosticRule,
} from '@/api/operations'
import { severityTone, toOptionalNumber, toOptionalText } from '@/utils/format'
import type {
  DiagnosticRuleType,
  RuleEditorOptions,
  RuleTestMatch,
  Severity,
} from '@/types/operations'

/**
 * 診断ルールの作成・編集。
 *
 * ルールは自動復旧の入口にあたるため、条件は自由記述のJSONではなく
 * 種別ごとのフォームで入力させる。保存前に判定を試せるようにしている。
 */
const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const ruleId = computed(() => {
  const id = route.params.id
  return typeof id === 'string' ? Number(id) : null
})
const isNew = computed(() => ruleId.value === null)

const options = ref<RuleEditorOptions | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)

const form = ref({
  name: '',
  classification: '',
  ruleType: 'State' as DiagnosticRuleType,
  severity: 'Medium' as Severity,
  recommendedActionId: '',
  priority: 100,
  rationaleTemplate: '{field} が {value} です(判定条件: {expected})。',
  isEnabled: true,
})

// 条件は種別ごとに別の入力欄を持つ
const condition = ref({
  field: 'containerState',
  equalsAnyText: '',
  operator: '>=',
  value: 0,
  pattern: '',
})

const saving = ref(false)
const saveError = ref<string | null>(null)

/** 保存前の試し撃ち。判定結果を確かめてから保存させる。 */
const testInput = ref({
  containerState: '',
  containerName: '',
  restartCount: '',
  memoryUsagePercent: '',
  diskUsagePercent: '',
  httpStatus: '',
  httpLatencyMs: '',
  logExcerpt: '',
})
const testMatches = ref<RuleTestMatch[] | null>(null)
const testing = ref(false)
const testError = ref<string | null>(null)

/** 入力から条件のJSONを組み立てる。画面ではJSONを直接書かせない。 */
const conditionJson = computed(() => {
  switch (form.value.ruleType) {
    case 'State':
      return JSON.stringify({
        field: condition.value.field,
        equalsAny: condition.value.equalsAnyText
          .split('\n')
          .map((line) => line.trim())
          .filter((line) => line.length > 0),
      })
    case 'Threshold':
      return JSON.stringify({
        field: condition.value.field,
        operator: condition.value.operator,
        value: condition.value.value,
      })
    default:
      return JSON.stringify({
        field: condition.value.field,
        pattern: condition.value.pattern,
      })
  }
})

/** 保存済みの条件JSONをフォームへ戻す。 */
function applyConditionJson(ruleType: DiagnosticRuleType, json: string): void {
  try {
    const parsed = JSON.parse(json) as Record<string, unknown>
    condition.value.field = typeof parsed.field === 'string' ? parsed.field : 'containerState'

    if (ruleType === 'State' && Array.isArray(parsed.equalsAny)) {
      condition.value.equalsAnyText = (parsed.equalsAny as string[]).join('\n')
    } else if (ruleType === 'Threshold') {
      condition.value.operator = typeof parsed.operator === 'string' ? parsed.operator : '>='
      condition.value.value = typeof parsed.value === 'number' ? parsed.value : 0
    } else if (ruleType === 'Regex') {
      condition.value.pattern = typeof parsed.pattern === 'string' ? parsed.pattern : ''
    }
  } catch {
    // 壊れた条件はフォームの初期値のままにする(保存時にサーバー側が拒否する)
  }
}

const canSubmit = computed(
  () =>
    !saving.value &&
    form.value.name.trim().length > 0 &&
    form.value.classification.trim().length > 0 &&
    form.value.rationaleTemplate.trim().length > 0,
)

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null

  try {
    options.value = await fetchRuleEditorOptions()

    if (ruleId.value !== null) {
      const rule = await fetchDiagnosticRule(ruleId.value)
      form.value = {
        name: rule.name,
        classification: rule.classification,
        ruleType: rule.ruleType as DiagnosticRuleType,
        severity: rule.severity,
        recommendedActionId: rule.recommendedActionId ?? '',
        priority: rule.priority,
        rationaleTemplate: '{field} が {value} です(判定条件: {expected})。',
        isEnabled: rule.isEnabled,
      }
      applyConditionJson(rule.ruleType as DiagnosticRuleType, rule.conditionJson)
    }
  } catch (e) {
    loadError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    loading.value = false
  }
}

onMounted(load)

// 種別を変えたら、その種別に無い入力は判定へ渡さない
watch(
  () => form.value.ruleType,
  () => {
    testMatches.value = null
  },
)

async function handleTest(): Promise<void> {
  testing.value = true
  testError.value = null
  testMatches.value = null

  try {
    // 編集中の内容をそのまま渡して確かめる。保存はされない。
    // 保存済みのルールも一緒に評価されるので、他のルールとの兼ね合いも見える。
    const response = await testDiagnosticRules({
      containerState: toOptionalText(testInput.value.containerState),
      containerName: toOptionalText(testInput.value.containerName),
      restartCount: toOptionalNumber(testInput.value.restartCount),
      memoryUsagePercent: toOptionalNumber(testInput.value.memoryUsagePercent),
      diskUsagePercent: toOptionalNumber(testInput.value.diskUsagePercent),
      httpStatus: toOptionalNumber(testInput.value.httpStatus),
      httpLatencyMs: toOptionalNumber(testInput.value.httpLatencyMs),
      logExcerpt: toOptionalText(testInput.value.logExcerpt),
      candidateRule: {
        id: ruleId.value ?? 0,
        name: form.value.name.trim().length > 0 ? form.value.name.trim() : t('rules.editingRule'),
        classification:
          form.value.classification.trim().length > 0 ? form.value.classification.trim() : '-',
        ruleType: form.value.ruleType,
        conditionJson: conditionJson.value,
        severity: form.value.severity,
        recommendedActionId:
          form.value.recommendedActionId.length > 0 ? form.value.recommendedActionId : null,
        priority: form.value.priority,
        rationaleTemplate: form.value.rationaleTemplate,
      },
    })
    testMatches.value = response.matches
  } catch (e) {
    testError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    testing.value = false
  }
}

async function handleSubmit(): Promise<void> {
  saving.value = true
  saveError.value = null

  try {
    const request = {
      name: form.value.name.trim(),
      classification: form.value.classification.trim(),
      ruleType: form.value.ruleType,
      conditionJson: conditionJson.value,
      severity: form.value.severity,
      recommendedActionId:
        form.value.recommendedActionId.length > 0 ? form.value.recommendedActionId : null,
      priority: form.value.priority,
      rationaleTemplate: form.value.rationaleTemplate,
      isEnabled: form.value.isEnabled,
    }

    if (ruleId.value === null) {
      await createDiagnosticRule(request)
    } else {
      await updateDiagnosticRule(ruleId.value, request)
    }

    await router.replace({ name: 'rules' })
  } catch (e) {
    saveError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div>
    <PageHeader :title="isNew ? t('rules.add') : t('rules.edit')" />

    <AsyncState :loading="loading" :error="loadError" :empty="options === null" @retry="load">
      <form v-if="options" @submit.prevent="handleSubmit">
        <p v-if="saveError" role="alert" class="message message--error" data-testid="save-error">
          {{ saveError }}
        </p>

        <div class="grid">
          <div class="form-field">
            <label for="rule-name">{{ t('rules.name') }}</label>
            <input id="rule-name" v-model="form.name" type="text" required maxlength="100" />
          </div>

          <div class="form-field">
            <label for="rule-classification">{{ t('rules.classification') }}</label>
            <input
              id="rule-classification"
              v-model="form.classification"
              type="text"
              required
              maxlength="64"
            />
          </div>

          <div class="form-field">
            <label for="rule-type">{{ t('rules.ruleType') }}</label>
            <select id="rule-type" v-model="form.ruleType">
              <option v-for="value in options.ruleTypes" :key="value" :value="value">
                {{ t(`rules.types.${value}`) }}
              </option>
            </select>
          </div>

          <div class="form-field">
            <label for="rule-severity">{{ t('confirmDialog.risk') }}</label>
            <select id="rule-severity" v-model="form.severity">
              <option v-for="value in options.severities" :key="value" :value="value">
                {{ t(`severity.${value.toLowerCase()}`) }}
              </option>
            </select>
          </div>

          <div class="form-field">
            <label for="rule-priority">{{ t('rules.priority') }}</label>
            <input
              id="rule-priority"
              v-model.number="form.priority"
              type="number"
              min="1"
              max="1000"
            />
            <p class="form-field__help">{{ t('rules.priorityHelp') }}</p>
          </div>

          <div class="form-field">
            <label for="rule-action">{{ t('rules.recommendedAction') }}</label>
            <select
              id="rule-action"
              v-model="form.recommendedActionId"
              aria-describedby="rule-action-help"
            >
              <option value="">{{ t('rules.noAction') }}</option>
              <option v-for="value in options.recommendedActionIds" :key="value" :value="value">
                {{ value }}
              </option>
            </select>
            <p id="rule-action-help" class="form-field__help">
              {{ t('rules.recommendedActionHelp') }}
            </p>
          </div>
        </div>

        <fieldset class="fieldset">
          <legend>{{ t('rules.condition') }}</legend>

          <div class="form-field">
            <label for="condition-field">{{ t('rules.conditionField') }}</label>
            <select id="condition-field" v-model="condition.field">
              <option v-for="value in options.fields" :key="value" :value="value">
                {{ value }}
              </option>
            </select>
            <p class="form-field__help">{{ t('rules.conditionFieldHelp') }}</p>
          </div>

          <div v-if="form.ruleType === 'State'" class="form-field">
            <label for="condition-equals">{{ t('rules.conditionEqualsAny') }}</label>
            <textarea
              id="condition-equals"
              v-model="condition.equalsAnyText"
              rows="3"
              :placeholder="t('rules.conditionEqualsAnyPlaceholder')"
            ></textarea>
          </div>

          <template v-else-if="form.ruleType === 'Threshold'">
            <div class="form-field">
              <label for="condition-operator">{{ t('rules.conditionOperator') }}</label>
              <select id="condition-operator" v-model="condition.operator">
                <option v-for="value in options.operators" :key="value" :value="value">
                  {{ value }}
                </option>
              </select>
            </div>
            <div class="form-field">
              <label for="condition-value">{{ t('rules.conditionValue') }}</label>
              <input
                id="condition-value"
                v-model.number="condition.value"
                type="number"
                step="any"
              />
            </div>
          </template>

          <div v-else class="form-field">
            <label for="condition-pattern">{{ t('rules.conditionPattern') }}</label>
            <input
              id="condition-pattern"
              v-model="condition.pattern"
              type="text"
              maxlength="500"
              aria-describedby="condition-pattern-help"
            />
            <p id="condition-pattern-help" class="form-field__help">
              {{ t('rules.conditionPatternHelp') }}
            </p>
          </div>

          <p class="preview">
            <span class="preview__label">{{ t('rules.conditionPreview') }}</span>
            <code data-testid="condition-preview">{{ conditionJson }}</code>
          </p>
        </fieldset>

        <div class="form-field">
          <label for="rule-rationale">{{ t('rules.rationaleTemplate') }}</label>
          <input
            id="rule-rationale"
            v-model="form.rationaleTemplate"
            type="text"
            required
            maxlength="500"
            aria-describedby="rule-rationale-help"
          />
          <p id="rule-rationale-help" class="form-field__help">
            {{ t('rules.rationaleTemplateHelp') }}
          </p>
        </div>

        <div class="form-field form-field--inline">
          <input id="rule-enabled" v-model="form.isEnabled" type="checkbox" />
          <label for="rule-enabled">{{ t('rules.enabled') }}</label>
        </div>

        <p class="form-field__help">{{ t('rules.saveWarning') }}</p>

        <div class="actions">
          <button type="submit" class="button button--primary" :disabled="!canSubmit">
            {{ t('common.save') }}
          </button>
          <button type="button" class="button" @click="router.push({ name: 'rules' })">
            {{ t('common.cancel') }}
          </button>
        </div>
      </form>
    </AsyncState>

    <section aria-labelledby="rule-test-heading" class="section">
      <h2 id="rule-test-heading" class="section__title">{{ t('rules.test') }}</h2>
      <p class="form-field__help">{{ t('rules.testWithEditing') }}</p>

      <div class="grid">
        <div class="form-field">
          <label for="test-container-state">{{ t('rules.containerState') }}</label>
          <input id="test-container-state" v-model="testInput.containerState" type="text" />
        </div>
        <div class="form-field">
          <label for="test-memory">{{ t('rules.memoryUsagePercent') }}</label>
          <input id="test-memory" v-model="testInput.memoryUsagePercent" type="number" />
        </div>
        <div class="form-field">
          <label for="test-disk">{{ t('rules.diskUsagePercent') }}</label>
          <input id="test-disk" v-model="testInput.diskUsagePercent" type="number" />
        </div>
        <div class="form-field">
          <label for="test-http-status">{{ t('rules.httpStatus') }}</label>
          <input id="test-http-status" v-model="testInput.httpStatus" type="number" />
        </div>
        <div class="form-field form-field--wide">
          <label for="test-log">{{ t('rules.logExcerpt') }}</label>
          <textarea id="test-log" v-model="testInput.logExcerpt" rows="3"></textarea>
        </div>
      </div>

      <button type="button" class="button" :disabled="testing" @click="handleTest">
        {{ t('rules.run') }}
      </button>

      <p v-if="testError" role="alert" class="message message--error">{{ testError }}</p>

      <template v-if="testMatches !== null">
        <ul v-if="testMatches.length > 0" class="cards" data-testid="test-matches">
          <li
            v-for="match in testMatches"
            :key="match.ruleId"
            class="card"
            :class="{ 'card--candidate': match.isCandidate }"
          >
            <div class="card__head">
              <strong>{{ match.ruleName }}</strong>
              <StatusBadge
                :tone="severityTone(match.severity)"
                :label="t(`severity.${match.severity.toLowerCase()}`)"
              />
              <!-- 編集中のものか保存済みのものかを取り違えないようにする -->
              <StatusBadge v-if="match.isCandidate" tone="medium" :label="t('rules.editingRule')" />
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
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
  gap: var(--spacing-md);
}

.grid .form-field {
  margin-bottom: 0;
}

.form-field--wide {
  grid-column: 1 / -1;
}

.form-field--inline {
  flex-direction: row;
  align-items: center;
}

.fieldset {
  margin: var(--spacing-lg) 0;
  padding: var(--spacing-md);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
}

.fieldset legend {
  padding: 0 var(--spacing-xs);
  font-weight: 600;
}

.preview {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
  align-items: baseline;
  margin-top: var(--spacing-sm);
  font-size: 0.8125rem;
}

.preview__label {
  color: var(--color-text-muted);
}

.preview code {
  font-family: ui-monospace, monospace;
  word-break: break-all;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
  margin-top: var(--spacing-lg);
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

.section .button {
  margin-top: var(--spacing-md);
}

.cards {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
  margin-top: var(--spacing-md);
}

.card {
  padding: var(--spacing-md);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background-color: var(--color-surface);
}

/* 編集中のルールによる一致は左端の線でも示す(色だけに頼らない) */
.card--candidate {
  border-left: 4px solid var(--color-accent);
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
  margin: var(--spacing-md) 0;
  border: 1px solid currentColor;
  border-radius: var(--radius);
  color: var(--color-critical);
  background-color: var(--color-critical-bg);
}

.muted {
  color: var(--color-text-muted);
  margin-top: var(--spacing-md);
}
</style>
