<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

/**
 * 秘密値の入力欄。
 * 保存済みの値はAPIから返らないため、この画面でも一切表示しない。
 * 「設定済み」かどうかだけを示し、変更するときだけ新しい値を入力させる。
 *
 * v-modelの値は「今回送信する新しい値」。nullなら変更しない。
 */
const props = withDefaults(
  defineProps<{
    /** 入力欄のid。ラベルとの関連付けに使う。 */
    id: string
    label: string
    /** 現在サーバー側に値が設定されているか。値そのものは受け取らない。 */
    configured: boolean
    modelValue: string | null
    disabled?: boolean
    /** 未設定のときに入力を必須とするか。 */
    required?: boolean
  }>(),
  { disabled: false, required: false },
)

const emit = defineEmits<{ 'update:modelValue': [value: string | null] }>()

const { t } = useI18n()

// 未設定なら最初から入力欄を出す。設定済みなら「変更する」を押したときだけ出す。
const editing = ref(!props.configured)
const revealed = ref(false)

const draft = computed({
  get: () => props.modelValue ?? '',
  set: (value: string) => emit('update:modelValue', value.length > 0 ? value : null),
})

watch(
  () => props.configured,
  (configured) => {
    if (!configured) {
      editing.value = true
    }
  },
)

function startEditing(): void {
  editing.value = true
}

function cancelEditing(): void {
  editing.value = false
  revealed.value = false
  emit('update:modelValue', null)
}
</script>

<template>
  <div class="secret-field">
    <label :for="id" class="secret-field__label">
      {{ label }}
      <span v-if="required && !configured" class="secret-field__required">{{
        t('common.required')
      }}</span>
    </label>

    <p class="secret-field__state" data-testid="secret-state">
      {{ configured ? t('secretField.stored') : t('secretField.notSet') }}
    </p>

    <div v-if="editing" class="secret-field__input-row">
      <input
        :id="id"
        v-model="draft"
        :type="revealed ? 'text' : 'password'"
        :disabled="disabled"
        autocomplete="new-password"
        spellcheck="false"
        class="secret-field__input"
      />
      <button
        type="button"
        class="secret-field__button"
        :aria-pressed="revealed"
        @click="revealed = !revealed"
      >
        {{ revealed ? t('secretField.hide') : t('secretField.show') }}
      </button>
      <button v-if="configured" type="button" class="secret-field__button" @click="cancelEditing">
        {{ t('secretField.keep') }}
      </button>
    </div>

    <button
      v-else
      type="button"
      class="secret-field__button"
      :disabled="disabled"
      @click="startEditing"
    >
      {{ t('secretField.replace') }}
    </button>

    <p v-if="editing" class="secret-field__note">{{ t('secretField.warning') }}</p>
  </div>
</template>

<style scoped>
.secret-field {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  align-items: flex-start;
}

.secret-field__label {
  font-weight: 600;
}

.secret-field__required {
  margin-left: var(--spacing-xs);
  font-size: 0.8125rem;
  font-weight: 400;
  color: var(--color-critical);
}

.secret-field__state,
.secret-field__note {
  font-size: 0.875rem;
  color: var(--color-text-muted);
}

.secret-field__input-row {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
  align-items: center;
}

.secret-field__input {
  min-width: 16rem;
  padding: 0.4em 0.6em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-bg);
  font-family: ui-monospace, monospace;
}

.secret-field__button {
  padding: 0.35em 0.9em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-surface);
  color: var(--color-text);
  cursor: pointer;
}

.secret-field__button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
