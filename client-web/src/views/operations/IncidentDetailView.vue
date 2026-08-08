<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import AsyncState from '@/components/common/AsyncState.vue'
import ConfirmActionDialog from '@/components/common/ConfirmActionDialog.vue'
import { extractErrorMessage } from '@/api/http'
import {
  addIncidentNote,
  createApproval,
  createRecoveryAction,
  fetchApprovals,
  fetchDiagnoses,
  fetchIncident,
  fetchRecoveryActionCatalog,
  fetchIncidentNotes,
  fetchRecoveryActions,
  fetchRecurrence,
  fetchTarget,
  fetchTargetCapabilities,
  rediagnose,
  updateIncidentStatus,
} from '@/api/operations'
import { useAsyncData } from '@/composables/useAsyncData'
import { useAuthStore } from '@/stores/auth'
import {
  formatDateTime,
  incidentStatusTone,
  newIdempotencyKey,
  resultTone,
  riskTone,
  severityTone,
} from '@/utils/format'
import type {
  Approval,
  Diagnosis,
  IncidentNote,
  IncidentStatus,
  Recurrence,
  RecoveryAction,
  RecoveryActionDefinition,
  Target,
  TargetCapabilities,
} from '@/types/operations'

const { t, locale } = useI18n()
const route = useRoute()
const auth = useAuthStore()

const incidentId = computed(() => Number(route.params.id))

const { data, loading, error, forbidden, load } = useAsyncData(
  () => fetchIncident(incidentId.value),
  t('common.error'),
)

const diagnoses = ref<Diagnosis[]>([])
const approvals = ref<Approval[]>([])
const actions = ref<RecoveryAction[]>([])
const catalog = ref<RecoveryActionDefinition[]>([])
const target = ref<Target | null>(null)
const capabilities = ref<TargetCapabilities | null>(null)
const notes = ref<IncidentNote[]>([])
const recurrence = ref<Recurrence | null>(null)
const noteDraft = ref('')

const statuses: IncidentStatus[] = ['Open', 'Acknowledged', 'Recovering', 'Resolved', 'Closed']
const selectedStatus = ref<IncidentStatus | ''>('')

const actionMessage = ref<string | null>(null)
const actionError = ref<string | null>(null)
const busy = ref(false)

// 復旧の要求内容。確認ダイアログで実行が決まるまで送らない。
const pendingAction = ref<{
  definition: RecoveryActionDefinition
  targetResource: string
  approvalId: number | null
} | null>(null)

const approvalForm = ref<{ actionId: string; targetResource: string; comment: string }>({
  actionId: '',
  targetResource: '',
  comment: '',
})

const allowedContainers = computed(() => target.value?.allowedContainers ?? [])

/**
 * 実行の候補は、この対象で許可されている操作のみに絞る。
 * 危険度Highはカタログにも存在しないが、念のためここでも除く。
 * 最終的な可否はAPI側で再度判定される。
 */
const runnableActions = computed(() => {
  const allowedOperations = capabilities.value?.allowedOperations ?? null

  return catalog.value.filter(
    (definition) =>
      definition.riskLevel !== 'High' &&
      (allowedOperations === null || allowedOperations.includes(definition.actionId)),
  )
})

/** 承認済みでまだ使われておらず、期限内の承認だけが実行に使える。 */
function usableApprovals(actionId: string): Approval[] {
  const now = Date.now()
  return approvals.value.filter(
    (approval) =>
      approval.actionId === actionId &&
      approval.status === 'Approved' &&
      !approval.isConsumed &&
      new Date(approval.expiresAt).getTime() > now,
  )
}

async function loadRelated(): Promise<void> {
  const [diagnosisList, approvalList, actionList, catalogList, noteList, recurrenceResult] =
    await Promise.allSettled([
      fetchDiagnoses(incidentId.value),
      fetchApprovals(incidentId.value),
      fetchRecoveryActions(incidentId.value),
      fetchRecoveryActionCatalog(),
      fetchIncidentNotes(incidentId.value),
      fetchRecurrence(incidentId.value),
    ])

  diagnoses.value = diagnosisList.status === 'fulfilled' ? diagnosisList.value : []
  approvals.value = approvalList.status === 'fulfilled' ? approvalList.value : []
  actions.value = actionList.status === 'fulfilled' ? actionList.value : []
  catalog.value = catalogList.status === 'fulfilled' ? catalogList.value : []
  notes.value = noteList.status === 'fulfilled' ? noteList.value : []
  recurrence.value = recurrenceResult.status === 'fulfilled' ? recurrenceResult.value : null

  if (data.value) {
    const [targetResult, capabilityResult] = await Promise.allSettled([
      fetchTarget(data.value.targetId),
      fetchTargetCapabilities(data.value.targetId),
    ])

    target.value = targetResult.status === 'fulfilled' ? targetResult.value : null
    capabilities.value = capabilityResult.status === 'fulfilled' ? capabilityResult.value : null
  }
}

async function loadAll(): Promise<void> {
  await load()
  selectedStatus.value = data.value?.status ?? ''
  if (data.value !== null) {
    await loadRelated()
  }
}

onMounted(loadAll)

async function handleStatusChange(): Promise<void> {
  if (selectedStatus.value === '' || data.value === null) {
    return
  }

  busy.value = true
  actionError.value = null
  actionMessage.value = null

  try {
    data.value = await updateIncidentStatus(incidentId.value, selectedStatus.value)
    actionMessage.value = t('common.saved')
  } catch (e) {
    actionError.value = extractErrorMessage(e, t('common.error'))
    selectedStatus.value = data.value.status
  } finally {
    busy.value = false
  }
}

/** 対応メモを追加する。書き換えと削除の口は無いため、追加のみ。 */
async function handleAddNote(): Promise<void> {
  const body = noteDraft.value.trim()
  if (body.length === 0) {
    return
  }

  busy.value = true
  actionError.value = null
  actionMessage.value = null

  try {
    await addIncidentNote(incidentId.value, body)
    noteDraft.value = ''
    notes.value = await fetchIncidentNotes(incidentId.value)
    actionMessage.value = t('common.saved')
  } catch (e) {
    actionError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    busy.value = false
  }
}

async function handleRediagnose(): Promise<void> {
  busy.value = true
  actionError.value = null
  actionMessage.value = null

  try {
    const result = await rediagnose(incidentId.value)
    if (result.diagnosis === null) {
      // AI無効・上限到達・検証失敗のいずれでも、診断は作らず理由だけを示す
      actionError.value = `${t('incidents.rediagnoseSkipped')}: ${result.message ?? result.outcome}`
    } else {
      actionMessage.value = t('common.saved')
    }
    await loadRelated()
  } catch (e) {
    actionError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    busy.value = false
  }
}

async function handleApproval(approve: boolean): Promise<void> {
  if (approvalForm.value.actionId === '') {
    return
  }

  busy.value = true
  actionError.value = null
  actionMessage.value = null

  try {
    await createApproval(incidentId.value, {
      actionId: approvalForm.value.actionId,
      targetResource:
        approvalForm.value.targetResource.length > 0 ? approvalForm.value.targetResource : null,
      approve,
      comment: approvalForm.value.comment.length > 0 ? approvalForm.value.comment : null,
    })
    approvalForm.value = { actionId: '', targetResource: '', comment: '' }
    await loadRelated()
    actionMessage.value = t('common.saved')
  } catch (e) {
    actionError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    busy.value = false
  }
}

function requestRecovery(definition: RecoveryActionDefinition, targetResource: string): void {
  const approval = definition.requiresApproval
    ? (usableApprovals(definition.actionId)[0] ?? null)
    : null

  if (definition.requiresApproval && approval === null) {
    actionError.value = t('incidents.noApprovedApproval')
    return
  }

  actionError.value = null
  pendingAction.value = {
    definition,
    targetResource,
    approvalId: approval?.id ?? null,
  }
}

async function confirmRecovery(): Promise<void> {
  const pending = pendingAction.value
  if (pending === null) {
    return
  }

  busy.value = true
  actionError.value = null
  actionMessage.value = null

  try {
    await createRecoveryAction(
      incidentId.value,
      {
        actionId: pending.definition.actionId,
        targetResource: pending.targetResource.length > 0 ? pending.targetResource : null,
        approvalId: pending.approvalId,
      },
      // 二度押しで二重に実行されないよう、要求ごとに固有の鍵を付ける
      newIdempotencyKey(),
    )
    pendingAction.value = null
    await loadRelated()
    actionMessage.value = t('common.saved')
  } catch (e) {
    pendingAction.value = null
    actionError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    busy.value = false
  }
}

// 操作ごとの入力欄(対象コンテナ)。actionIdごとに保持する。
const resourceInputs = ref<Record<string, string>>({})
</script>

<template>
  <div>
    <PageHeader :title="data?.title ?? t('nav.incidents')" :description="data?.classification" />

    <AsyncState
      :loading="loading"
      :error="error"
      :forbidden="forbidden"
      :empty="data === null"
      @retry="loadAll"
    >
      <template v-if="data">
        <p v-if="actionError" role="alert" class="message message--error">{{ actionError }}</p>
        <p v-if="actionMessage" role="status" class="message message--ok">{{ actionMessage }}</p>

        <dl class="definition">
          <dt>{{ t('confirmDialog.risk') }}</dt>
          <dd>
            <StatusBadge
              :tone="severityTone(data.severity)"
              :label="t(`severity.${data.severity.toLowerCase()}`)"
            />
          </dd>
          <dt>{{ t('incidents.changeStatus') }}</dt>
          <dd>
            <StatusBadge
              :tone="incidentStatusTone(data.status)"
              :label="t(`status.${data.status.toLowerCase()}`)"
            />
          </dd>
          <dt>{{ t('incidents.target') }}</dt>
          <dd>{{ target?.name ?? data.targetId }}</dd>
          <dt>{{ t('incidents.occurrenceCount') }}</dt>
          <dd>{{ data.occurrenceCount }}</dd>
          <dt>{{ t('incidents.firstOccurredAt') }}</dt>
          <dd>{{ formatDateTime(data.firstOccurredAt, locale) }}</dd>
          <dt>{{ t('incidents.lastOccurredAt') }}</dt>
          <dd>{{ formatDateTime(data.lastOccurredAt, locale) }}</dd>
        </dl>

        <section v-if="auth.isAdmin" aria-labelledby="status-heading" class="section">
          <h2 id="status-heading" class="section__title">{{ t('incidents.changeStatus') }}</h2>
          <div class="inline-form">
            <label class="sr-only" for="incident-status-select">
              {{ t('incidents.changeStatus') }}
            </label>
            <select id="incident-status-select" v-model="selectedStatus" :disabled="busy">
              <option v-for="value in statuses" :key="value" :value="value">
                {{ t(`status.${value.toLowerCase()}`) }}
              </option>
            </select>
            <button type="button" class="button" :disabled="busy" @click="handleStatusChange">
              {{ t('common.save') }}
            </button>
          </div>
        </section>

        <section aria-labelledby="diagnoses-heading" class="section">
          <h2 id="diagnoses-heading" class="section__title">{{ t('incidents.diagnoses') }}</h2>

          <button
            v-if="auth.isAdmin"
            type="button"
            class="button"
            :disabled="busy"
            @click="handleRediagnose"
          >
            {{ t('incidents.rediagnose') }}
          </button>

          <ul v-if="diagnoses.length > 0" class="cards">
            <li v-for="diagnosis in diagnoses" :key="diagnosis.id" class="card">
              <div class="card__head">
                <StatusBadge
                  :tone="severityTone(diagnosis.severity)"
                  :label="t(`severity.${diagnosis.severity.toLowerCase()}`)"
                />
                <span class="muted"
                  >{{ t('incidents.diagnosisSource') }}: {{ diagnosis.source }}</span
                >
                <span class="muted">{{ formatDateTime(diagnosis.createdAt, locale) }}</span>
              </div>
              <p class="card__body">{{ diagnosis.rationale }}</p>
              <p v-if="diagnosis.recommendedActionId" class="card__foot">
                {{ t('incidents.recommendedAction') }}: {{ diagnosis.recommendedActionId }}
                <StatusBadge
                  v-if="!diagnosis.recommendedActionAllowed"
                  tone="high"
                  :label="t('incidents.actionNotAllowed')"
                />
              </p>
            </li>
          </ul>
          <p v-else class="muted">{{ t('common.empty') }}</p>
        </section>

        <section v-if="auth.isAdmin" aria-labelledby="approvals-heading" class="section">
          <h2 id="approvals-heading" class="section__title">{{ t('incidents.approvals') }}</h2>

          <div class="approval-form">
            <div class="form-field">
              <label for="approval-action">{{ t('incidents.actionId') }}</label>
              <select id="approval-action" v-model="approvalForm.actionId">
                <option value="">—</option>
                <option
                  v-for="definition in runnableActions.filter((d) => d.requiresApproval)"
                  :key="definition.actionId"
                  :value="definition.actionId"
                >
                  {{ definition.name }}
                </option>
              </select>
            </div>

            <div class="form-field">
              <label for="approval-resource">{{ t('confirmDialog.target') }}</label>
              <select id="approval-resource" v-model="approvalForm.targetResource">
                <option value="">—</option>
                <option v-for="name in allowedContainers" :key="name" :value="name">
                  {{ name }}
                </option>
              </select>
            </div>

            <div class="form-field">
              <label for="approval-comment">{{ t('incidents.comment') }}</label>
              <input
                id="approval-comment"
                v-model="approvalForm.comment"
                type="text"
                maxlength="500"
              />
            </div>

            <div class="inline-form">
              <button
                type="button"
                class="button button--primary"
                :disabled="busy || approvalForm.actionId === ''"
                @click="handleApproval(true)"
              >
                {{ t('incidents.approve') }}
              </button>
              <button
                type="button"
                class="button"
                :disabled="busy || approvalForm.actionId === ''"
                @click="handleApproval(false)"
              >
                {{ t('incidents.reject') }}
              </button>
            </div>
          </div>

          <div v-if="approvals.length > 0" class="table-scroll">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">{{ t('incidents.actionId') }}</th>
                  <th scope="col">{{ t('confirmDialog.target') }}</th>
                  <th scope="col">{{ t('settings.result') }}</th>
                  <th scope="col">{{ t('incidents.expiresAt') }}</th>
                  <th scope="col">{{ t('incidents.comment') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="approval in approvals" :key="approval.id">
                  <td>{{ approval.actionId }}</td>
                  <td>{{ approval.targetResource ?? '—' }}</td>
                  <td>
                    <StatusBadge :tone="resultTone(approval.status)" :label="approval.status" />
                  </td>
                  <td>{{ formatDateTime(approval.expiresAt, locale) }}</td>
                  <td class="table__title">{{ approval.comment ?? '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="muted">{{ t('common.empty') }}</p>
        </section>

        <section v-if="auth.isAdmin" aria-labelledby="recovery-heading" class="section">
          <h2 id="recovery-heading" class="section__title">{{ t('incidents.recoveryActions') }}</h2>

          <p class="form-field__help">{{ t('incidents.highRiskNotSupported') }}</p>

          <ul class="cards">
            <li v-for="definition in runnableActions" :key="definition.actionId" class="card">
              <div class="card__head">
                <strong>{{ definition.name }}</strong>
                <StatusBadge
                  :tone="riskTone(definition.riskLevel)"
                  :label="t(`severity.${definition.riskLevel.toLowerCase()}`)"
                />
                <StatusBadge
                  v-if="definition.requiresApproval"
                  tone="medium"
                  :label="t('incidents.approvalRequired')"
                />
              </div>
              <p class="card__body">{{ definition.description }}</p>

              <div class="inline-form">
                <template v-if="definition.requiresTargetResource">
                  <label class="sr-only" :for="`resource-${definition.actionId}`">
                    {{ t('confirmDialog.target') }}
                  </label>
                  <select
                    :id="`resource-${definition.actionId}`"
                    v-model="resourceInputs[definition.actionId]"
                  >
                    <option value="">—</option>
                    <option v-for="name in allowedContainers" :key="name" :value="name">
                      {{ name }}
                    </option>
                  </select>
                </template>

                <button
                  type="button"
                  class="button button--primary"
                  :disabled="
                    busy ||
                    (definition.requiresTargetResource &&
                      !(resourceInputs[definition.actionId] ?? '').length)
                  "
                  @click="requestRecovery(definition, resourceInputs[definition.actionId] ?? '')"
                >
                  {{ t('incidents.requestRecovery') }}
                </button>
              </div>
            </li>
          </ul>

          <div v-if="actions.length > 0" class="table-scroll">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">{{ t('incidents.actionId') }}</th>
                  <th scope="col">{{ t('confirmDialog.target') }}</th>
                  <th scope="col">{{ t('incidents.riskLevel') }}</th>
                  <th scope="col">{{ t('settings.result') }}</th>
                  <th scope="col">{{ t('incidents.requestedAt') }}</th>
                  <th scope="col">{{ t('incidents.resultMessage') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="action in actions" :key="action.id">
                  <td>{{ action.actionId }}</td>
                  <td>{{ action.targetResource ?? '—' }}</td>
                  <td>
                    <StatusBadge
                      :tone="riskTone(action.riskLevel)"
                      :label="t(`severity.${action.riskLevel.toLowerCase()}`)"
                    />
                  </td>
                  <td>
                    <StatusBadge :tone="resultTone(action.status)" :label="action.status" />
                  </td>
                  <td>{{ formatDateTime(action.requestedAt, locale) }}</td>
                  <td class="table__title">
                    {{ action.resultMessage ?? action.blockedReason ?? '—' }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <section
          v-if="recurrence"
          aria-labelledby="recurrence-heading"
          class="section"
          data-testid="recurrence"
        >
          <h2 id="recurrence-heading" class="section__title">{{ t('recurrence.title') }}</h2>

          <p v-if="recurrence.totalCount === 0" class="muted">{{ t('recurrence.firstTime') }}</p>

          <dl v-else class="definition">
            <dt>{{ t('incidents.occurrenceCount') }}</dt>
            <dd>
              {{ t('recurrence.total', { count: recurrence.totalCount }) }}
              <template v-if="recurrence.resolvedCount > 0">
                ({{ t('recurrence.resolved', { count: recurrence.resolvedCount }) }})
              </template>
            </dd>

            <template v-if="recurrence.previousOccurredAt">
              <dt>{{ t('recurrence.previous') }}</dt>
              <dd>{{ formatDateTime(recurrence.previousOccurredAt, locale) }}</dd>
            </template>

            <template v-if="recurrence.lastSuccessfulActionId">
              <dt>{{ t('recurrence.lastSuccessful') }}</dt>
              <dd data-testid="last-successful-action">
                {{ recurrence.lastSuccessfulActionId }}
                <template v-if="recurrence.lastSuccessfulAt">
                  ({{ formatDateTime(recurrence.lastSuccessfulAt, locale) }})
                </template>
              </dd>
            </template>
          </dl>
        </section>

        <section aria-labelledby="notes-heading" class="section" data-testid="incident-notes">
          <h2 id="notes-heading" class="section__title">{{ t('incidentNotes.title') }}</h2>
          <p class="form-field__help">{{ t('incidentNotes.description') }}</p>

          <form v-if="auth.isAdmin" data-testid="note-form" @submit.prevent="handleAddNote">
            <div class="form-field">
              <label for="note-body" class="sr-only">{{ t('incidentNotes.title') }}</label>
              <textarea
                id="note-body"
                v-model="noteDraft"
                rows="3"
                maxlength="4000"
                :placeholder="t('incidentNotes.placeholder')"
              ></textarea>
            </div>
            <button
              type="submit"
              class="button"
              :disabled="busy || noteDraft.trim().length === 0"
              data-testid="add-note"
            >
              {{ t('incidentNotes.add') }}
            </button>
          </form>

          <ul v-if="notes.length > 0" class="cards">
            <li v-for="note in notes" :key="note.id" class="card">
              <p class="card__head">
                <strong>{{ note.authorName }}</strong>
                <span class="muted">{{ formatDateTime(note.createdAt, locale) }}</span>
              </p>
              <p class="card__body">{{ note.body }}</p>
            </li>
          </ul>
          <p v-else class="muted">{{ t('incidentNotes.empty') }}</p>
        </section>

        <ConfirmActionDialog
          :open="pendingAction !== null"
          :title="t('incidents.confirmTitle')"
          :target-name="pendingAction?.targetResource || (target?.name ?? '')"
          :action-label="pendingAction?.definition.name ?? ''"
          :risk="pendingAction?.definition.riskLevel ?? 'Low'"
          :busy="busy"
          @confirm="confirmRecovery"
          @cancel="pendingAction = null"
        />
      </template>
    </AsyncState>
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

.definition {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: var(--spacing-xs) var(--spacing-md);
  align-items: center;
}

.definition dt {
  color: var(--color-text-muted);
}

.inline-form {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
}

.inline-form select {
  padding: 0.4em 0.6em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-bg);
}

.approval-form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-md);
}

.approval-form .form-field {
  margin-bottom: 0;
}

.cards {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
  margin: var(--spacing-md) 0;
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

.card__body {
  white-space: pre-wrap;
}

.card__foot {
  margin-top: var(--spacing-xs);
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  font-size: 0.875rem;
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

.muted {
  color: var(--color-text-muted);
  font-size: 0.875rem;
}
</style>
