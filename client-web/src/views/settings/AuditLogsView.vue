<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import AsyncState from '@/components/common/AsyncState.vue'
import PaginationControls from '@/components/common/PaginationControls.vue'
import { exportAuditLogs, fetchAuditLogFilterOptions, searchAuditLogs } from '@/api/settings'
import { useAsyncData } from '@/composables/useAsyncData'
import { formatDateTime, resultTone } from '@/utils/format'
import type { AuditLogFilterOptions, AuditLogQuery, AuditResultValue } from '@/types/settings'

const { t, locale } = useI18n()

const actorName = ref('')
const targetType = ref('')
const action = ref('')
const result = ref<AuditResultValue | ''>('')
const from = ref('')
const to = ref('')
const page = ref(1)

const options = ref<AuditLogFilterOptions | null>(null)

/** datetime-localの値はローカル時刻。APIへはUTCのISO文字列で渡す。 */
function toIsoUtc(value: string): string | undefined {
  if (value.length === 0) {
    return undefined
  }
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString()
}

/**
 * 一覧とCSV出力で同じ絞り込みを使う。片方だけ条件が違うと、
 * 画面で見えている範囲と出力される範囲がずれる。
 */
function currentFilter(): AuditLogQuery {
  // 空文字は「絞り込まない」を意味するため、送らずに省く
  const selectedResult = result.value

  return {
    actorName: actorName.value.length > 0 ? actorName.value : undefined,
    targetType: targetType.value.length > 0 ? targetType.value : undefined,
    action: action.value.length > 0 ? action.value : undefined,
    result: selectedResult === '' ? undefined : selectedResult,
    from: toIsoUtc(from.value),
    to: toIsoUtc(to.value),
  }
}

const { data, loading, error, forbidden, load } = useAsyncData(
  () => searchAuditLogs({ ...currentFilter(), page: page.value, pageSize: 20 }),
  t('common.error'),
)

const exporting = ref(false)
const exportError = ref<string | null>(null)

/** CSVを取り出す。ブラウザにダウンロードさせるだけで、画面の状態は変えない。 */
async function handleExport(): Promise<void> {
  exporting.value = true
  exportError.value = null

  let url: string | null = null
  try {
    const blob = await exportAuditLogs(currentFilter())
    url = URL.createObjectURL(blob)

    const link = document.createElement('a')
    link.href = url
    link.download = `audit-logs-${new Date().toISOString().slice(0, 10)}.csv`
    link.click()
  } catch {
    exportError.value = t('auditExport.failed')
  } finally {
    // 解放しないとページを離れるまでBlobがメモリに残る
    if (url !== null) {
      URL.revokeObjectURL(url)
    }
    exporting.value = false
  }
}

onMounted(async () => {
  await load()
  try {
    options.value = await fetchAuditLogFilterOptions()
  } catch {
    options.value = null
  }
})

watch(page, load)

function handleSubmit(): void {
  page.value = 1
  void load()
}
</script>

<template>
  <div>
    <PageHeader :title="t('nav.auditLogs')" />

    <div class="export">
      <button
        type="button"
        class="button"
        :disabled="exporting"
        data-testid="export-csv"
        @click="handleExport"
      >
        {{ t('auditExport.export') }}
      </button>
      <p class="form-field__help">{{ t('auditExport.help') }}</p>
    </div>

    <p v-if="exportError" role="alert" class="message message--error">{{ exportError }}</p>

    <form class="filters" role="search" @submit.prevent="handleSubmit">
      <div class="form-field">
        <label for="audit-actor">{{ t('auditLogs.actor') }}</label>
        <input
          id="audit-actor"
          v-model="actorName"
          type="text"
          :placeholder="t('auditLogs.actorPlaceholder')"
        />
      </div>

      <div class="form-field">
        <label for="audit-target-type">{{ t('auditLogs.targetType') }}</label>
        <select id="audit-target-type" v-model="targetType">
          <option value="">{{ t('auditLogs.allTargetTypes') }}</option>
          <option v-for="value in options?.targetTypes ?? []" :key="value" :value="value">
            {{ value }}
          </option>
        </select>
      </div>

      <div class="form-field">
        <label for="audit-action">{{ t('auditLogs.action') }}</label>
        <select id="audit-action" v-model="action">
          <option value="">{{ t('auditLogs.allActions') }}</option>
          <option v-for="value in options?.actions ?? []" :key="value" :value="value">
            {{ value }}
          </option>
        </select>
      </div>

      <div class="form-field">
        <label for="audit-result">{{ t('auditLogs.result') }}</label>
        <select id="audit-result" v-model="result">
          <option value="">{{ t('auditLogs.allResults') }}</option>
          <option
            v-for="value in ['Success', 'Failure', 'Denied'] as const"
            :key="value"
            :value="value"
          >
            {{ t(`auditLogs.resultValues.${value}`) }}
          </option>
        </select>
      </div>

      <div class="form-field">
        <label for="audit-from">{{ t('auditLogs.from') }}</label>
        <input id="audit-from" v-model="from" type="datetime-local" />
      </div>

      <div class="form-field">
        <label for="audit-to">{{ t('auditLogs.to') }}</label>
        <input id="audit-to" v-model="to" type="datetime-local" />
      </div>

      <button type="submit" class="button">{{ t('common.filter') }}</button>
    </form>

    <AsyncState
      :loading="loading"
      :error="error"
      :forbidden="forbidden"
      :empty="data !== null && data.items.length === 0"
      @retry="load"
    >
      <template v-if="data">
        <div class="table-scroll">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">{{ t('auditLogs.occurredAt') }}</th>
                <th scope="col">{{ t('auditLogs.actor') }}</th>
                <th scope="col">{{ t('auditLogs.ipAddress') }}</th>
                <th scope="col">{{ t('auditLogs.action') }}</th>
                <th scope="col">{{ t('auditLogs.targetType') }}</th>
                <th scope="col">{{ t('auditLogs.result') }}</th>
                <th scope="col">{{ t('auditLogs.details') }}</th>
                <th scope="col">{{ t('auditLogs.userAgent') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="log in data.items" :key="log.id">
                <th scope="row">{{ formatDateTime(log.occurredAt, locale) }}</th>
                <td>{{ log.actorName ?? '—' }}</td>
                <td>{{ log.ipAddress }}</td>
                <td>{{ log.action }}</td>
                <td>{{ log.targetType }}{{ log.targetId ? ` #${log.targetId}` : '' }}</td>
                <td>
                  <StatusBadge
                    :tone="resultTone(log.result)"
                    :label="t(`auditLogs.resultValues.${log.result}`)"
                  />
                </td>
                <td class="table__title">{{ log.details ?? '—' }}</td>
                <td class="user-agent">{{ log.userAgent }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <PaginationControls
          :page="data.page"
          :page-size="data.pageSize"
          :total-count="data.totalCount"
          :total-pages="data.totalPages"
          @update:page="(value) => (page = value)"
        />
      </template>
    </AsyncState>
  </div>
</template>

<style scoped>
.export {
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

.filters {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-lg);
}

.filters .form-field {
  margin-bottom: 0;
}

.user-agent {
  max-width: 18rem;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
