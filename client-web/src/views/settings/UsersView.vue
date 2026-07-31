<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { extractErrorMessage } from '@/api/http'
import {
  createUser,
  fetchUsers,
  resetUserMfa,
  updateUserActive,
  updateUserRole,
} from '@/api/auth'
import { useAuthStore } from '@/stores/auth'
import { formatDateTime } from '@/utils/format'
import type { ManagedUser, UserRole } from '@/types/auth'

/**
 * 利用者の管理。役割の割り当ては権限そのものを動かす操作。
 * 削除の口は無い(監査ログから誰の操作か辿れなくなるため、無効化にとどめる)。
 */
const { t, locale } = useI18n()
const auth = useAuthStore()

const users = ref<ManagedUser[]>([])
const loading = ref(true)
const busy = ref(false)
const message = ref<string | null>(null)
const errorMessage = ref<string | null>(null)

const roles: UserRole[] = ['Viewer', 'OperatorAdmin', 'SystemExecutor']

const MIN_PASSWORD_LENGTH = 12

const form = ref({ username: '', password: '', role: 'Viewer' as UserRole })

const canCreate = computed(
  () =>
    !busy.value &&
    form.value.username.trim().length > 0 &&
    form.value.password.length >= MIN_PASSWORD_LENGTH,
)

async function load(): Promise<void> {
  loading.value = true
  errorMessage.value = null

  try {
    users.value = await fetchUsers()
  } catch (e) {
    errorMessage.value = extractErrorMessage(e, t('common.error'))
  } finally {
    loading.value = false
  }
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
    await createUser({
      username: form.value.username.trim(),
      password: form.value.password,
      role: form.value.role,
    })

    // 初期パスワードを画面に残さない
    form.value = { username: '', password: '', role: 'Viewer' }
    users.value = await fetchUsers()
  })

const handleRoleChange = (user: ManagedUser, role: string) =>
  run(async () => {
    const updated = await updateUserRole(user.id, role as UserRole)
    users.value = users.value.map((u) => (u.id === updated.id ? updated : u))
  })

const handleActiveChange = (user: ManagedUser, isActive: boolean) =>
  run(async () => {
    const updated = await updateUserActive(user.id, isActive)
    users.value = users.value.map((u) => (u.id === updated.id ? updated : u))
  })

const handleResetMfa = (user: ManagedUser) => {
  // リセットは対象のセッションをすべて切る。押し間違いで締め出さない。
  if (!globalThis.confirm(t('users.resetMfaConfirm', { name: user.username }))) {
    return
  }

  return run(async () => {
    const updated = await resetUserMfa(user.id)
    users.value = users.value.map((u) => (u.id === updated.id ? updated : u))
  })
}

/** 自分自身の行は、役割の変更と無効化を画面でも塞ぐ(APIでも拒否される)。 */
function isSelf(user: ManagedUser): boolean {
  return user.id === auth.currentUser?.id
}
</script>

<template>
  <div>
    <PageHeader :title="t('users.title')" :description="t('users.description')" />

    <p v-if="errorMessage" role="alert" class="message message--error">{{ errorMessage }}</p>
    <p v-if="message" role="status" class="message message--ok">{{ message }}</p>

    <section aria-labelledby="add-heading" class="section">
      <h2 id="add-heading" class="section__title">{{ t('users.add') }}</h2>

      <form data-testid="user-form" @submit.prevent="handleCreate">
        <div class="grid">
          <div class="form-field">
            <label for="new-username">{{ t('auth.username') }}</label>
            <input
              id="new-username"
              v-model="form.username"
              type="text"
              required
              maxlength="64"
              pattern="[A-Za-z0-9._\-]+"
              autocomplete="off"
              aria-describedby="new-username-help"
            />
            <p id="new-username-help" class="form-field__help">{{ t('users.usernameHelp') }}</p>
          </div>

          <div class="form-field">
            <label for="new-password">{{ t('users.initialPassword') }}</label>
            <input
              id="new-password"
              v-model="form.password"
              type="password"
              required
              :minlength="MIN_PASSWORD_LENGTH"
              autocomplete="new-password"
              aria-describedby="new-password-help"
            />
            <p id="new-password-help" class="form-field__help">
              {{ t('users.initialPasswordHelp', { min: MIN_PASSWORD_LENGTH }) }}
            </p>
          </div>

          <div class="form-field">
            <label for="new-role">{{ t('users.role') }}</label>
            <select id="new-role" v-model="form.role">
              <option v-for="role in roles" :key="role" :value="role">
                {{ t(`role.${role}`) }}
              </option>
            </select>
          </div>
        </div>

        <button
          type="submit"
          class="button button--primary"
          :disabled="!canCreate"
          data-testid="create-user"
        >
          {{ t('users.add') }}
        </button>
      </form>
    </section>

    <section aria-labelledby="list-heading" class="section">
      <h2 id="list-heading" class="section__title">{{ t('users.list') }}</h2>

      <p v-if="loading" role="status">{{ t('common.loading') }}</p>

      <div v-else-if="users.length > 0" class="table-scroll">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">{{ t('auth.username') }}</th>
              <th scope="col">{{ t('users.role') }}</th>
              <th scope="col">{{ t('users.status') }}</th>
              <th scope="col">{{ t('account.mfa') }}</th>
              <th scope="col">{{ t('targets.lastUpdated') }}</th>
              <th scope="col">
                <span class="sr-only">{{ t('common.execute') }}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="user in users" :key="user.id" :data-testid="`user-${user.id}`">
              <th scope="row">
                {{ user.username }}
                <span v-if="isSelf(user)" class="muted">({{ t('users.self') }})</span>
              </th>
              <td>
                <label class="sr-only" :for="`role-${user.id}`">{{ t('users.role') }}</label>
                <select
                  :id="`role-${user.id}`"
                  :value="user.role"
                  :disabled="busy || isSelf(user)"
                  @change="handleRoleChange(user, ($event.target as HTMLSelectElement).value)"
                >
                  <option v-for="role in roles" :key="role" :value="role">
                    {{ t(`role.${role}`) }}
                  </option>
                </select>
              </td>
              <td>
                <StatusBadge
                  :tone="user.isActive ? 'low' : 'medium'"
                  :label="user.isActive ? t('users.active') : t('users.inactive')"
                />
              </td>
              <td>
                <StatusBadge
                  :tone="user.mfaEnabled ? 'low' : 'high'"
                  :label="user.mfaEnabled ? t('account.mfaOn') : t('account.mfaOff')"
                />
              </td>
              <td>{{ formatDateTime(user.updatedAt, locale) }}</td>
              <td class="actions">
                <button
                  type="button"
                  class="button"
                  :disabled="busy || isSelf(user)"
                  :data-testid="`toggle-active-${user.id}`"
                  @click="handleActiveChange(user, !user.isActive)"
                >
                  {{ user.isActive ? t('users.deactivate') : t('users.activate') }}
                </button>
                <button
                  v-if="user.mfaEnabled && !isSelf(user)"
                  type="button"
                  class="button"
                  :disabled="busy"
                  :data-testid="`reset-mfa-${user.id}`"
                  @click="handleResetMfa(user)"
                >
                  {{ t('users.resetMfa') }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <p class="form-field__help">{{ t('users.noDeleteNote') }}</p>
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

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
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
