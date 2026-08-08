<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import AsyncState from '@/components/common/AsyncState.vue'
import PaginationControls from '@/components/common/PaginationControls.vue'
import { extractErrorMessage } from '@/api/http'
import {
  fetchDeviceTokens,
  markNotificationRead,
  revokeDeviceToken,
  searchNotifications,
} from '@/api/operations'
import { useAsyncData } from '@/composables/useAsyncData'
import { useNotificationsStore } from '@/stores/notifications'
import { enablePushNotifications, describePushState, type PushState } from '@/services/push'
import { formatDateTime, severityTone } from '@/utils/format'
import type { DeviceToken, Severity } from '@/types/operations'

const { t, locale } = useI18n()
const notificationsStore = useNotificationsStore()

const filter = ref<'all' | 'unread' | 'read'>('all')
/** 空文字は「重大度で絞らない」。設定側と揃えて「この重大度以上」で絞る。 */
const minimumSeverity = ref<Severity | ''>('')
const severities: Severity[] = ['Critical', 'High', 'Medium', 'Low']
const page = ref(1)

const { data, loading, error, forbidden, load } = useAsyncData(
  () =>
    searchNotifications(
      filter.value === 'all' ? undefined : filter.value === 'read',
      page.value,
      20,
      minimumSeverity.value === '' ? undefined : minimumSeverity.value,
    ),
  t('common.error'),
)

const devices = ref<DeviceToken[]>([])
const pushState = ref<PushState | null>(null)
const pushBusy = ref(false)
const pushError = ref<string | null>(null)

async function loadDevices(): Promise<void> {
  try {
    devices.value = await fetchDeviceTokens()
  } catch {
    devices.value = []
  }
}

onMounted(async () => {
  await Promise.all([load(), loadDevices(), notificationsStore.refreshUnreadCount()])
})

watch([filter, minimumSeverity], () => {
  page.value = 1
  void load()
})

watch(page, load)

async function handleMarkRead(id: number): Promise<void> {
  try {
    await markNotificationRead(id)
    await Promise.all([load(), notificationsStore.refreshUnreadCount()])
  } catch (e) {
    error.value = extractErrorMessage(e, t('common.error'))
  }
}

async function handleEnablePush(): Promise<void> {
  pushBusy.value = true
  pushError.value = null

  try {
    pushState.value = await enablePushNotifications()
    if (pushState.value === 'registered') {
      await loadDevices()
    } else {
      pushError.value = t(describePushState(pushState.value))
    }
  } catch (e) {
    pushError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    pushBusy.value = false
  }
}

async function handleRevokeDevice(id: number): Promise<void> {
  pushBusy.value = true
  pushError.value = null

  try {
    await revokeDeviceToken(id)
    await loadDevices()
  } catch (e) {
    pushError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    pushBusy.value = false
  }
}
</script>

<template>
  <div>
    <PageHeader
      :title="t('nav.notifications')"
      :description="t('notifications.unreadCount', { count: notificationsStore.unreadCount })"
    />

    <div class="filters" role="group" :aria-label="t('common.filter')">
      <button
        v-for="value in ['all', 'unread', 'read'] as const"
        :key="value"
        type="button"
        class="button"
        :class="{ 'button--primary': filter === value }"
        :aria-pressed="filter === value"
        @click="filter = value"
      >
        {{ t(`notifications.${value}`) }}
      </button>

      <div class="form-field form-field--compact">
        <label for="notification-severity">{{ t('notifications.minimumSeverity') }}</label>
        <select
          id="notification-severity"
          v-model="minimumSeverity"
          data-testid="filter-severity"
        >
          <option value="">{{ t('incidents.allSeverities') }}</option>
          <option v-for="value in severities" :key="value" :value="value">
            {{ t(`severity.${value.toLowerCase()}`) }}
          </option>
        </select>
      </div>
    </div>

    <AsyncState
      :loading="loading"
      :error="error"
      :forbidden="forbidden"
      :empty="data !== null && data.items.length === 0"
      @retry="load"
    >
      <template v-if="data">
        <ul class="cards">
          <li
            v-for="notification in data.items"
            :key="notification.id"
            class="card"
            :class="{ 'card--unread': !notification.isRead }"
          >
            <div class="card__head">
              <StatusBadge
                :tone="severityTone(notification.severity)"
                :label="t(`severity.${notification.severity.toLowerCase()}`)"
              />
              <strong>{{ notification.title }}</strong>
              <StatusBadge
                v-if="!notification.isRead"
                tone="medium"
                :label="t('notifications.unread')"
              />
            </div>

            <p class="card__body">{{ notification.body }}</p>

            <div class="card__foot">
              <span class="muted">{{ formatDateTime(notification.lastNotifiedAt, locale) }}</span>
              <span v-if="notification.occurrenceCount > 1" class="muted">
                {{ t('notifications.occurrences', { count: notification.occurrenceCount }) }}
              </span>
              <RouterLink
                v-if="notification.incidentId"
                :to="{ name: 'incident-detail', params: { id: notification.incidentId } }"
              >
                {{ t('nav.incidents') }}
              </RouterLink>
              <button
                v-if="!notification.isRead"
                type="button"
                class="button"
                @click="handleMarkRead(notification.id)"
              >
                {{ t('notifications.markAsRead') }}
              </button>
            </div>
          </li>
        </ul>

        <PaginationControls
          :page="data.page"
          :page-size="data.pageSize"
          :total-count="data.totalCount"
          :total-pages="data.totalPages"
          @update:page="(value) => (page = value)"
        />
      </template>
    </AsyncState>

    <section aria-labelledby="devices-heading" class="section">
      <h2 id="devices-heading" class="section__title">{{ t('notifications.devices') }}</h2>

      <p v-if="pushError" role="alert" class="message message--error">{{ pushError }}</p>
      <p v-if="pushState === 'registered'" role="status" class="message message--ok">
        {{ t('notifications.deviceRegistered') }}
      </p>

      <button type="button" class="button" :disabled="pushBusy" @click="handleEnablePush">
        {{ t('notifications.registerDevice') }}
      </button>

      <div v-if="devices.length > 0" class="table-scroll">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">{{ t('notifications.deviceLabel') }}</th>
              <th scope="col">ID</th>
              <th scope="col">{{ t('settings.startedAt') }}</th>
              <th scope="col">{{ t('notifications.lastUsedAt') }}</th>
              <th scope="col">
                <span class="sr-only">{{ t('common.execute') }}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="device in devices" :key="device.id">
              <th scope="row">{{ device.label ?? '—' }}</th>
              <td>{{ device.tokenSuffix }}</td>
              <td>{{ formatDateTime(device.createdAt, locale) }}</td>
              <td>{{ formatDateTime(device.lastUsedAt, locale) }}</td>
              <td>
                <button
                  v-if="device.isActive"
                  type="button"
                  class="button"
                  :disabled="pushBusy"
                  @click="handleRevokeDevice(device.id)"
                >
                  {{ t('notifications.revokeDevice') }}
                </button>
                <StatusBadge v-else tone="neutral" :label="t('notifications.revoked')" />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-else class="muted">{{ t('common.empty') }}</p>
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

.form-field--compact {
  margin-bottom: 0;
}

.filters {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
  margin-bottom: var(--spacing-lg);
}

.cards {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
}

.card {
  padding: var(--spacing-md);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background-color: var(--color-surface);
}

/* 未読は左端の太線でも示す(色だけに頼らない) */
.card--unread {
  border-left: 4px solid var(--color-accent);
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
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-md);
  margin-top: var(--spacing-sm);
}

.section {
  margin-top: var(--spacing-xl);
}

.section__title {
  font-size: 1.125rem;
  font-weight: 600;
  margin-bottom: var(--spacing-sm);
}

.section .table-scroll {
  margin-top: var(--spacing-md);
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
