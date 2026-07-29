<script setup lang="ts">
import { onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import AsyncState from '@/components/common/AsyncState.vue'
import { fetchTargets } from '@/api/operations'
import { useAsyncData } from '@/composables/useAsyncData'
import { formatDateTime } from '@/utils/format'
import { useAuthStore } from '@/stores/auth'

const { t, locale } = useI18n()
const auth = useAuthStore()

const { data, loading, error, forbidden, load } = useAsyncData(fetchTargets, t('common.error'))

onMounted(load)
</script>

<template>
  <div>
    <PageHeader :title="t('nav.targets')">
      <template #actions>
        <RouterLink v-if="auth.isAdmin" class="button button--primary" :to="{ name: 'target-new' }">
          {{ t('targets.add') }}
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
              <th scope="col">{{ t('targets.name') }}</th>
              <th scope="col">{{ t('targets.template') }}</th>
              <th scope="col">{{ t('targets.isEnabled') }}</th>
              <th scope="col">{{ t('targets.autoRecovery') }}</th>
              <th scope="col">{{ t('targets.allowedContainers') }}</th>
              <th scope="col">{{ t('targets.lastUpdated') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="target in data" :key="target.id">
              <th scope="row" class="table__title">
                <RouterLink :to="{ name: 'target-detail', params: { id: target.id } }">
                  {{ target.name }}
                </RouterLink>
              </th>
              <td>{{ target.templateId }}</td>
              <td>
                <StatusBadge
                  :tone="target.isEnabled ? 'low' : 'neutral'"
                  :label="target.isEnabled ? t('status.healthy') : t('rules.disabled')"
                />
              </td>
              <td>
                <StatusBadge
                  :tone="target.autoRecoveryEnabled ? 'medium' : 'neutral'"
                  :label="
                    target.autoRecoveryEnabled
                      ? t('targets.autoRecoveryOn')
                      : t('targets.autoRecoveryOff')
                  "
                />
              </td>
              <td>
                <span v-if="target.allowedContainers.length > 0">
                  {{ target.allowedContainers.join(', ') }}
                </span>
                <StatusBadge v-else tone="neutral" :label="t('targets.noAllowedContainers')" />
              </td>
              <td>{{ formatDateTime(target.updatedAt, locale) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </AsyncState>
  </div>
</template>
