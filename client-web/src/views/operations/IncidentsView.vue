<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import AsyncState from '@/components/common/AsyncState.vue'
import PaginationControls from '@/components/common/PaginationControls.vue'
import { searchIncidents } from '@/api/operations'
import { useAsyncData } from '@/composables/useAsyncData'
import { formatDateTime, incidentStatusTone, severityTone } from '@/utils/format'
import type { IncidentStatus, Severity } from '@/types/operations'

const { t, locale } = useI18n()

const search = ref('')
const severity = ref<Severity | ''>('')
const status = ref<IncidentStatus | ''>('')
const page = ref(1)

const severities: Severity[] = ['Critical', 'High', 'Medium', 'Low']
const statuses: IncidentStatus[] = ['Open', 'Acknowledged', 'Recovering', 'Resolved', 'Closed']

const { data, loading, error, forbidden, load } = useAsyncData(() => {
  // 空文字は「絞り込まない」を意味するため、送らずに省く
  const selectedSeverity = severity.value
  const selectedStatus = status.value

  return searchIncidents({
    search: search.value.length > 0 ? search.value : undefined,
    severity: selectedSeverity === '' ? undefined : selectedSeverity,
    status: selectedStatus === '' ? undefined : selectedStatus,
    page: page.value,
    pageSize: 20,
  })
}, t('common.error'))

onMounted(load)

// 絞り込みを変えたら1ページ目から見せる
watch([search, severity, status], () => {
  page.value = 1
})

watch(page, load)

function handleSubmit(): void {
  page.value = 1
  void load()
}
</script>

<template>
  <div>
    <PageHeader :title="t('nav.incidents')" />

    <form class="filters" role="search" @submit.prevent="handleSubmit">
      <div class="form-field">
        <label for="incident-search">{{ t('common.search') }}</label>
        <input
          id="incident-search"
          v-model="search"
          type="text"
          :placeholder="t('incidents.searchPlaceholder')"
          maxlength="100"
        />
      </div>

      <div class="form-field">
        <label for="incident-severity">{{ t('confirmDialog.risk') }}</label>
        <select id="incident-severity" v-model="severity">
          <option value="">{{ t('incidents.allSeverities') }}</option>
          <option v-for="value in severities" :key="value" :value="value">
            {{ t(`severity.${value.toLowerCase()}`) }}
          </option>
        </select>
      </div>

      <div class="form-field">
        <label for="incident-status">{{ t('incidents.changeStatus') }}</label>
        <select id="incident-status" v-model="status">
          <option value="">{{ t('incidents.allStatuses') }}</option>
          <option v-for="value in statuses" :key="value" :value="value">
            {{ t(`status.${value.toLowerCase()}`) }}
          </option>
        </select>
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
                <th scope="col">{{ t('incidents.title') }}</th>
                <th scope="col">{{ t('confirmDialog.risk') }}</th>
                <th scope="col">{{ t('incidents.changeStatus') }}</th>
                <th scope="col">{{ t('incidents.classification') }}</th>
                <th scope="col">{{ t('incidents.occurrenceCount') }}</th>
                <th scope="col">{{ t('incidents.lastOccurredAt') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="incident in data.items" :key="incident.id">
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
                <td>{{ incident.classification }}</td>
                <td>{{ incident.occurrenceCount }}</td>
                <td>{{ formatDateTime(incident.lastOccurredAt, locale) }}</td>
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
</style>
