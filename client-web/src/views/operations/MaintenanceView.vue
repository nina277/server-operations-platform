<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { extractErrorMessage } from '@/api/http'
import {
  cancelMaintenanceWindow,
  createMaintenanceWindow,
  fetchMaintenanceWindows,
  fetchTargets,
} from '@/api/operations'
import { formatDateTime } from '@/utils/format'
import type { MaintenanceWindow, Target } from '@/types/operations'

/**
 * メンテナンス期間。計画停止中の通知と自動復旧を止める。
 * 検知は止めないため、期間中に起きたことはインシデントとして残る。
 */
const { t, locale } = useI18n()

const windows = ref<MaintenanceWindow[]>([])
const targets = ref<Target[]>([])
const loading = ref(true)
const busy = ref(false)
const message = ref<string | null>(null)
const errorMessage = ref<string | null>(null)

/** datetime-local の値。ローカル時刻で入力し、送信時にUTCへ直す。 */
function toLocalInput(value: Date): string {
  const offset = value.getTimezoneOffset() * 60000
  return new Date(value.getTime() - offset).toISOString().slice(0, 16)
}

const form = ref({
  targetId: '' as string,
  reason: '',
  startsAt: toLocalInput(new Date()),
  endsAt: toLocalInput(new Date(Date.now() + 2 * 60 * 60 * 1000)),
  suppressNotifications: true,
  suppressAutoRecovery: true,
})

/** どちらも止めない設定は保存が拒否されるため、送る前に画面で止める。 */
const canSubmit = computed(
  () =>
    !busy.value &&
    form.value.reason.trim().length > 0 &&
    (form.value.suppressNotifications || form.value.suppressAutoRecovery),
)

async function load(): Promise<void> {
  loading.value = true
  errorMessage.value = null

  // 対象一覧が取れなくても期間の一覧は出す(対象なしの期間は登録できる)
  const [windowResult, targetResult] = await Promise.allSettled([
    fetchMaintenanceWindows(),
    fetchTargets(),
  ])

  windows.value = windowResult.status === 'fulfilled' ? windowResult.value : []
  targets.value = targetResult.status === 'fulfilled' ? targetResult.value : []

  if (windowResult.status === 'rejected') {
    errorMessage.value = extractErrorMessage(windowResult.reason, t('common.error'))
  }

  loading.value = false
}

onMounted(load)

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

const handleCreate = () =>
  run(async () => {
    await createMaintenanceWindow({
      // 空文字は「すべての対象」を意味する。数値へ変換すると0になり別物になる。
      targetId: form.value.targetId === '' ? null : Number(form.value.targetId),
      reason: form.value.reason.trim(),
      startsAt: new Date(form.value.startsAt).toISOString(),
      endsAt: new Date(form.value.endsAt).toISOString(),
      suppressNotifications: form.value.suppressNotifications,
      suppressAutoRecovery: form.value.suppressAutoRecovery,
    })

    form.value.reason = ''
    windows.value = await fetchMaintenanceWindows()
  })

const handleCancel = (window: MaintenanceWindow) => {
  // 抑止を解くと通知と自動復旧が戻るため、取り違えないよう確認する
  if (!globalThis.confirm(t('maintenance.cancelConfirm'))) {
    return
  }

  return run(async () => {
    await cancelMaintenanceWindow(window.id)
    windows.value = await fetchMaintenanceWindows()
  })
}

function suppressLabel(window: MaintenanceWindow): string {
  const parts: string[] = []
  if (window.suppressNotifications) {
    parts.push(t('maintenance.suppressNotifications'))
  }
  if (window.suppressAutoRecovery) {
    parts.push(t('maintenance.suppressAutoRecovery'))
  }
  return parts.join(' / ')
}
</script>

<template>
  <div>
    <PageHeader :title="t('maintenance.title')" :description="t('maintenance.description')" />

    <p v-if="errorMessage" role="alert" class="message message--error">{{ errorMessage }}</p>
    <p v-if="message" role="status" class="message message--ok">{{ message }}</p>

    <section aria-labelledby="add-heading" class="section">
      <h2 id="add-heading" class="section__title">{{ t('maintenance.add') }}</h2>

      <form data-testid="maintenance-form" @submit.prevent="handleCreate">
        <div class="form-field">
          <label for="mw-reason">{{ t('maintenance.reason') }}</label>
          <input
            id="mw-reason"
            v-model="form.reason"
            type="text"
            required
            maxlength="200"
            aria-describedby="mw-reason-help"
          />
          <p id="mw-reason-help" class="form-field__help">{{ t('maintenance.reasonHelp') }}</p>
        </div>

        <div class="grid">
          <div class="form-field">
            <label for="mw-target">{{ t('maintenance.target') }}</label>
            <select id="mw-target" v-model="form.targetId">
              <option value="">{{ t('maintenance.allTargets') }}</option>
              <option v-for="target in targets" :key="target.id" :value="String(target.id)">
                {{ target.name }}
              </option>
            </select>
          </div>

          <div class="form-field">
            <label for="mw-starts">{{ t('maintenance.startsAt') }}</label>
            <input id="mw-starts" v-model="form.startsAt" type="datetime-local" required />
          </div>

          <div class="form-field">
            <label for="mw-ends">{{ t('maintenance.endsAt') }}</label>
            <input id="mw-ends" v-model="form.endsAt" type="datetime-local" required />
          </div>
        </div>

        <div class="form-field form-field--inline">
          <input
            id="mw-notifications"
            v-model="form.suppressNotifications"
            type="checkbox"
            data-testid="suppress-notifications"
          />
          <label for="mw-notifications">{{ t('maintenance.suppressNotifications') }}</label>
        </div>

        <div class="form-field form-field--inline">
          <input
            id="mw-auto-recovery"
            v-model="form.suppressAutoRecovery"
            type="checkbox"
            data-testid="suppress-auto-recovery"
          />
          <label for="mw-auto-recovery">{{ t('maintenance.suppressAutoRecovery') }}</label>
        </div>

        <p class="form-field__help">{{ t('maintenance.suppressHelp') }}</p>

        <button
          type="submit"
          class="button button--primary"
          :disabled="!canSubmit"
          data-testid="create-window"
        >
          {{ t('common.save') }}
        </button>
      </form>
    </section>

    <section aria-labelledby="list-heading" class="section">
      <h2 id="list-heading" class="section__title">{{ t('maintenance.period') }}</h2>

      <p v-if="loading" role="status">{{ t('common.loading') }}</p>

      <div v-else-if="windows.length > 0" class="table-scroll">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">{{ t('maintenance.reason') }}</th>
              <th scope="col">{{ t('maintenance.target') }}</th>
              <th scope="col">{{ t('maintenance.period') }}</th>
              <th scope="col">{{ t('maintenance.suppresses') }}</th>
              <th scope="col">
                <span class="sr-only">{{ t('common.execute') }}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="window in windows" :key="window.id">
              <th scope="row">
                <StatusBadge
                  :tone="window.isActive ? 'high' : 'low'"
                  :label="window.isActive ? t('maintenance.active') : t('maintenance.scheduled')"
                />
                {{ window.reason }}
              </th>
              <td>{{ window.targetName ?? t('maintenance.allTargets') }}</td>
              <td>
                {{ formatDateTime(window.startsAt, locale) }} 〜
                {{ formatDateTime(window.endsAt, locale) }}
              </td>
              <td>{{ suppressLabel(window) }}</td>
              <td>
                <button
                  type="button"
                  class="button"
                  :disabled="busy"
                  @click="handleCancel(window)"
                >
                  {{ t('maintenance.cancel') }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <p v-else class="muted">{{ t('maintenance.empty') }}</p>
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
  padding-top: var(--spacing-md);
  border-top: 1px solid var(--color-border);
}

.section:first-of-type {
  border-top: none;
}

.section__title {
  font-size: 1.125rem;
  font-weight: 600;
  margin-bottom: var(--spacing-sm);
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  gap: var(--spacing-md);
}

.form-field--inline {
  flex-direction: row;
  align-items: center;
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
}
</style>
