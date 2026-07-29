<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import AsyncState from '@/components/common/AsyncState.vue'
import SecretField from '@/components/common/SecretField.vue'
import { extractErrorMessage } from '@/api/http'
import { createTarget, fetchAdapterTemplates } from '@/api/operations'
import { useAsyncData } from '@/composables/useAsyncData'

const { t } = useI18n()
const router = useRouter()

const {
  data: templates,
  loading,
  error,
  forbidden,
  load,
} = useAsyncData(fetchAdapterTemplates, t('common.error'))

const name = ref('')
const description = ref('')
const templateId = ref('')
const settings = ref<Record<string, string>>({})
const credentials = ref<Record<string, string | null>>({})

const submitting = ref(false)
const submitError = ref<string | null>(null)

const selectedTemplate = computed(
  () => templates.value?.find((x) => x.id === templateId.value) ?? null,
)

const plainInputs = computed(() => selectedTemplate.value?.inputs.filter((i) => !i.secret) ?? [])
const secretInputs = computed(() => selectedTemplate.value?.inputs.filter((i) => i.secret) ?? [])

onMounted(load)

// テンプレートを変えたら入力欄も作り直す(前のテンプレートの値を持ち越さない)
watch(selectedTemplate, (template) => {
  settings.value = {}
  credentials.value = {}

  for (const input of template?.inputs ?? []) {
    if (input.secret) {
      credentials.value[input.key] = null
    } else {
      settings.value[input.key] = input.defaultValue ?? ''
    }
  }
})

async function handleSubmit(): Promise<void> {
  submitting.value = true
  submitError.value = null

  try {
    const credentialValues: Record<string, string> = {}
    for (const [key, value] of Object.entries(credentials.value)) {
      if (value !== null && value.length > 0) {
        credentialValues[key] = value
      }
    }

    const created = await createTarget({
      name: name.value,
      templateId: templateId.value,
      description: description.value.length > 0 ? description.value : null,
      settings: settings.value,
      credentials: credentialValues,
    })

    await router.replace({ name: 'target-detail', params: { id: created.id } })
  } catch (e) {
    submitError.value = extractErrorMessage(e, t('common.error'))
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div>
    <PageHeader :title="t('targets.add')" />

    <AsyncState
      :loading="loading"
      :error="error"
      :forbidden="forbidden"
      :empty="templates !== null && templates.length === 0"
      @retry="load"
    >
      <form v-if="templates" @submit.prevent="handleSubmit">
        <p v-if="submitError" role="alert" class="message message--error">{{ submitError }}</p>

        <div class="form-field">
          <label for="new-template">{{ t('targets.template') }}</label>
          <select id="new-template" v-model="templateId" required>
            <option value="" disabled>—</option>
            <option v-for="template in templates" :key="template.id" :value="template.id">
              {{ template.name }}
            </option>
          </select>
          <p v-if="selectedTemplate" class="form-field__help">
            {{ selectedTemplate.description }}
          </p>
        </div>

        <div class="form-field">
          <label for="new-name">{{ t('targets.name') }}</label>
          <input id="new-name" v-model="name" type="text" required maxlength="100" />
        </div>

        <div class="form-field">
          <label for="new-description">{{ t('targets.description') }}</label>
          <input id="new-description" v-model="description" type="text" maxlength="500" />
        </div>

        <template v-if="selectedTemplate">
          <div v-for="input in plainInputs" :key="input.key" class="form-field">
            <label :for="`new-setting-${input.key}`">{{ input.label }}</label>
            <input
              :id="`new-setting-${input.key}`"
              v-model="settings[input.key]"
              type="text"
              :required="input.required"
            />
            <p class="form-field__help">{{ input.description }}</p>
          </div>

          <SecretField
            v-for="input in secretInputs"
            :key="input.key"
            :id="`new-credential-${input.key}`"
            :label="input.label"
            :configured="false"
            :required="input.required"
            :model-value="credentials[input.key] ?? null"
            class="form-field"
            @update:model-value="(value) => (credentials[input.key] = value)"
          />

          <p class="form-field__help">
            {{ t('targets.autoRecoveryHelp') }}
          </p>
        </template>

        <button
          type="submit"
          class="button button--primary"
          :disabled="submitting || templateId === ''"
        >
          {{ t('common.save') }}
        </button>
      </form>
    </AsyncState>
  </div>
</template>

<style scoped>
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
</style>
