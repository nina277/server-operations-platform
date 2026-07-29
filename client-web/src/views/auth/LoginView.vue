<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { extractErrorCode, extractErrorMessage } from '@/api/http'
import { setLocale, type AppLocale } from '@/locales'

const { t, locale } = useI18n()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const username = ref('')
const password = ref('')
const totpCode = ref('')
/** MFAが必要と分かってから認証コード欄を出す(不要な利用者には見せない)。 */
const mfaRequested = ref(false)
const errorMessage = ref<string | null>(null)
const submitting = ref(false)

const sessionExpiredMessage = computed(() =>
  auth.isSessionExpired ? t('auth.sessionExpired') : null,
)

function changeLocale(event: Event): void {
  setLocale((event.target as HTMLSelectElement).value as AppLocale)
}

async function handleSubmit(): Promise<void> {
  submitting.value = true
  errorMessage.value = null

  try {
    await auth.login({
      username: username.value,
      password: password.value,
      totpCode: totpCode.value.length > 0 ? totpCode.value : undefined,
    })

    // 元の遷移先はアプリ内の絶対パスのときだけ使う(外部への誘導を避ける)
    const redirect = route.query.redirect
    const target = typeof redirect === 'string' && redirect.startsWith('/') ? redirect : null
    await router.replace(target ?? { name: 'dashboard' })
  } catch (error) {
    const code = extractErrorCode(error)
    if (code === 'mfa_required') {
      mfaRequested.value = true
      errorMessage.value = t('auth.mfaRequired')
    } else {
      // 認証コードが違う場合も入力欄は出したままにする
      errorMessage.value = extractErrorMessage(error, t('auth.loginFailed'))
    }
    password.value = ''
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="login">
    <form class="login__card" @submit.prevent="handleSubmit">
      <h1 class="login__title">{{ t('app.name') }}</h1>
      <p class="login__subtitle">{{ t('app.description') }}</p>

      <p v-if="sessionExpiredMessage" class="login__notice" role="status">
        {{ sessionExpiredMessage }}
      </p>

      <p v-if="errorMessage" class="login__error" role="alert" data-testid="login-error">
        {{ errorMessage }}
      </p>

      <div class="login__field">
        <label for="username">{{ t('auth.username') }}</label>
        <input
          id="username"
          v-model="username"
          type="text"
          required
          autocomplete="username"
          :disabled="submitting"
        />
      </div>

      <div class="login__field">
        <label for="password">{{ t('auth.password') }}</label>
        <input
          id="password"
          v-model="password"
          type="password"
          required
          autocomplete="current-password"
          :disabled="submitting"
        />
      </div>

      <div v-if="mfaRequested" class="login__field" data-testid="totp-field">
        <label for="totpCode">{{ t('auth.totpCode') }}</label>
        <input
          id="totpCode"
          v-model="totpCode"
          type="text"
          inputmode="numeric"
          autocomplete="one-time-code"
          maxlength="8"
          aria-describedby="totp-hint"
          :disabled="submitting"
        />
        <p id="totp-hint" class="login__hint">{{ t('auth.totpHint') }}</p>
      </div>

      <button type="submit" class="login__submit" :disabled="submitting">
        {{ submitting ? t('auth.loggingIn') : t('auth.login') }}
      </button>

      <label class="login__locale">
        <span>{{ t('common.language') }}</span>
        <select :value="locale" @change="changeLocale">
          <option value="ja">{{ t('common.japanese') }}</option>
          <option value="en">{{ t('common.english') }}</option>
        </select>
      </label>
    </form>
  </main>
</template>

<style scoped>
.login {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: var(--spacing-md);
}

.login__card {
  width: min(24rem, 100%);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
  padding: var(--spacing-xl) var(--spacing-lg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background-color: var(--color-surface);
}

.login__title {
  font-size: 1.25rem;
  font-weight: 600;
}

.login__subtitle {
  margin-top: calc(var(--spacing-md) * -1 + var(--spacing-xs));
  color: var(--color-text-muted);
  font-size: 0.875rem;
}

.login__notice,
.login__error {
  padding: var(--spacing-sm);
  border-radius: var(--radius);
  border: 1px solid currentColor;
}

.login__notice {
  color: var(--color-medium);
  background-color: var(--color-medium-bg);
}

.login__error {
  color: var(--color-critical);
  background-color: var(--color-critical-bg);
}

.login__field {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.login__field input {
  padding: 0.5em 0.6em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-bg);
}

.login__hint {
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.login__submit {
  padding: 0.6em 1em;
  border: 1px solid var(--color-accent);
  border-radius: var(--radius);
  background-color: var(--color-accent);
  color: var(--color-text-inverse);
  font-weight: 600;
  cursor: pointer;
}

.login__submit:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.login__locale {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-sm);
  font-size: 0.875rem;
  color: var(--color-text-muted);
}

.login__locale select {
  padding: 0.3em 0.5em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-surface);
}
</style>
