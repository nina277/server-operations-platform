<script setup lang="ts">
import { computed, onMounted, ref, useTemplateRef } from 'vue'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import StatusBadge from '@/components/common/StatusBadge.vue'
import { extractErrorMessage } from '@/api/http'
import { changePassword, setupMfa, verifyMfa } from '@/api/auth'
import { useAuthStore } from '@/stores/auth'

/**
 * 自分のアカウント設定。MFAの有効化とパスワードの変更を行う。
 *
 * 管理操作(設定変更・復旧・監査参照)はMFAの直近認証を要求するため、
 * MFAを設定できないと管理者でも何もできない。この画面がその入口になる。
 */
const { t } = useI18n()
const auth = useAuthStore()

const qrCanvas = useTemplateRef<HTMLCanvasElement>('qrCanvas')

// --- MFA ---

/** セットアップで受け取ったシークレット。この応答でのみ返り、以後は再表示されない。 */
const mfaSecret = ref<string | null>(null)
const mfaTotpCode = ref('')
const mfaBusy = ref(false)
const mfaError = ref<string | null>(null)
const mfaMessage = ref<string | null>(null)

const mfaEnabled = computed(() => auth.mfaEnabled)

onMounted(async () => {
  // 役割の表示などに使うため、最新の状態を取り直す
  try {
    await auth.loadCurrentUser()
  } catch {
    // 取得できなくても画面は開く
  }
})

async function handleSetupMfa(): Promise<void> {
  mfaBusy.value = true
  mfaError.value = null
  mfaMessage.value = null

  try {
    const result = await setupMfa()
    mfaSecret.value = result.secret

    // 認証アプリへ手入力させるのは間違いやすいため、QRを出す。
    // 生成は端末内で行い、シークレットを外部へ送らない。
    const { toCanvas } = await import('qrcode')
    if (qrCanvas.value) {
      await toCanvas(qrCanvas.value, result.otpAuthUri, { width: 200 })
    }
  } catch (e) {
    mfaError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    mfaBusy.value = false
  }
}

async function handleVerifyMfa(): Promise<void> {
  mfaBusy.value = true
  mfaError.value = null
  mfaMessage.value = null

  try {
    await verifyMfa(mfaTotpCode.value)
    // 確認できたらシークレットは画面から消す
    mfaSecret.value = null
    mfaTotpCode.value = ''
    await auth.loadCurrentUser()
    mfaMessage.value = t('account.mfaEnabled')
  } catch (e) {
    mfaError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    mfaBusy.value = false
  }
}

// --- パスワード ---

const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const passwordBusy = ref(false)
const passwordError = ref<string | null>(null)
const passwordMessage = ref<string | null>(null)

const MIN_PASSWORD_LENGTH = 12

const passwordsMatch = computed(
  () => confirmPassword.value.length === 0 || newPassword.value === confirmPassword.value,
)

const canChangePassword = computed(
  () =>
    !passwordBusy.value &&
    currentPassword.value.length > 0 &&
    newPassword.value.length >= MIN_PASSWORD_LENGTH &&
    newPassword.value === confirmPassword.value,
)

async function handleChangePassword(): Promise<void> {
  passwordBusy.value = true
  passwordError.value = null
  passwordMessage.value = null

  try {
    await changePassword({
      currentPassword: currentPassword.value,
      newPassword: newPassword.value,
    })

    // 入力欄に値を残さない
    currentPassword.value = ''
    newPassword.value = ''
    confirmPassword.value = ''
    passwordMessage.value = t('account.passwordChanged')
  } catch (e) {
    passwordError.value = extractErrorMessage(e, t('common.error'))
    currentPassword.value = ''
  } finally {
    passwordBusy.value = false
  }
}
</script>

<template>
  <div>
    <PageHeader :title="t('account.title')" :description="auth.currentUser?.username" />

    <section aria-labelledby="mfa-heading" class="section">
      <h2 id="mfa-heading" class="section__title">{{ t('account.mfa') }}</h2>

      <p class="state">
        <StatusBadge
          :tone="mfaEnabled ? 'low' : 'high'"
          :label="mfaEnabled ? t('account.mfaOn') : t('account.mfaOff')"
        />
      </p>

      <p class="form-field__help">{{ t('account.mfaRequiredForAdmin') }}</p>

      <p v-if="mfaError" role="alert" class="message message--error" data-testid="mfa-error">
        {{ mfaError }}
      </p>
      <p v-if="mfaMessage" role="status" class="message message--ok" data-testid="mfa-message">
        {{ mfaMessage }}
      </p>

      <button
        v-if="mfaSecret === null"
        type="button"
        class="button"
        :disabled="mfaBusy"
        data-testid="mfa-setup"
        @click="handleSetupMfa"
      >
        {{ mfaEnabled ? t('account.mfaReconfigure') : t('account.mfaSetup') }}
      </button>

      <div v-if="mfaSecret !== null" class="mfa-setup" data-testid="mfa-setup-panel">
        <p>{{ t('account.mfaScan') }}</p>
        <canvas ref="qrCanvas" class="mfa-setup__qr" :aria-label="t('account.mfaQrLabel')"></canvas>

        <div class="form-field">
          <label for="mfa-secret">{{ t('account.mfaSecret') }}</label>
          <input
            id="mfa-secret"
            :value="mfaSecret"
            type="text"
            readonly
            class="mfa-setup__secret"
            aria-describedby="mfa-secret-help"
          />
          <p id="mfa-secret-help" class="form-field__help">{{ t('account.mfaSecretHelp') }}</p>
        </div>

        <div class="form-field">
          <label for="mfa-code">{{ t('auth.totpCode') }}</label>
          <input
            id="mfa-code"
            v-model="mfaTotpCode"
            type="text"
            inputmode="numeric"
            autocomplete="one-time-code"
            maxlength="8"
            aria-describedby="mfa-code-help"
          />
          <p id="mfa-code-help" class="form-field__help">{{ t('auth.totpHint') }}</p>
        </div>

        <button
          type="button"
          class="button button--primary"
          :disabled="mfaBusy || mfaTotpCode.length === 0"
          data-testid="mfa-verify"
          @click="handleVerifyMfa"
        >
          {{ t('account.mfaVerify') }}
        </button>
      </div>
    </section>

    <section aria-labelledby="password-heading" class="section">
      <h2 id="password-heading" class="section__title">{{ t('account.password') }}</h2>

      <p
        v-if="passwordError"
        role="alert"
        class="message message--error"
        data-testid="password-error"
      >
        {{ passwordError }}
      </p>
      <p
        v-if="passwordMessage"
        role="status"
        class="message message--ok"
        data-testid="password-message"
      >
        {{ passwordMessage }}
      </p>

      <form @submit.prevent="handleChangePassword">
        <div class="form-field">
          <label for="current-password">{{ t('account.currentPassword') }}</label>
          <input
            id="current-password"
            v-model="currentPassword"
            type="password"
            autocomplete="current-password"
            required
          />
        </div>

        <div class="form-field">
          <label for="new-password">{{ t('account.newPassword') }}</label>
          <input
            id="new-password"
            v-model="newPassword"
            type="password"
            autocomplete="new-password"
            :minlength="MIN_PASSWORD_LENGTH"
            required
            aria-describedby="new-password-help"
          />
          <p id="new-password-help" class="form-field__help">
            {{ t('account.passwordRule', { min: MIN_PASSWORD_LENGTH }) }}
          </p>
        </div>

        <div class="form-field">
          <label for="confirm-password">{{ t('account.confirmPassword') }}</label>
          <input
            id="confirm-password"
            v-model="confirmPassword"
            type="password"
            autocomplete="new-password"
            required
            :aria-invalid="!passwordsMatch"
            aria-describedby="confirm-password-error"
          />
          <p id="confirm-password-error" class="form-field__error">
            {{ passwordsMatch ? '' : t('account.passwordMismatch') }}
          </p>
        </div>

        <p class="form-field__help">{{ t('account.passwordChangeWarning') }}</p>

        <button type="submit" class="button button--primary" :disabled="!canChangePassword">
          {{ t('common.save') }}
        </button>
      </form>
    </section>
  </div>
</template>

<style scoped>
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

.state {
  margin-bottom: var(--spacing-sm);
}

.mfa-setup {
  margin-top: var(--spacing-md);
  padding: var(--spacing-md);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background-color: var(--color-surface);
  max-width: 26rem;
}

.mfa-setup__qr {
  display: block;
  margin: var(--spacing-md) 0;
  background-color: #ffffff;
  padding: var(--spacing-sm);
  border-radius: var(--radius);
}

.mfa-setup__secret {
  font-family: ui-monospace, monospace;
  word-break: break-all;
}

.form-field__error {
  min-height: 1.5em;
  font-size: 0.875rem;
  color: var(--color-critical);
}

.message {
  padding: var(--spacing-sm);
  margin-bottom: var(--spacing-md);
  border: 1px solid currentColor;
  border-radius: var(--radius);
  max-width: 40rem;
}

.message--error {
  color: var(--color-critical);
  background-color: var(--color-critical-bg);
}

.message--ok {
  color: var(--color-low);
  background-color: var(--color-low-bg);
}

form {
  max-width: 26rem;
}
</style>
