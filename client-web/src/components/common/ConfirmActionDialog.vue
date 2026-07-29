<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import StatusBadge from './StatusBadge.vue'

/**
 * 復旧操作など、影響のある操作を実行する前の確認ダイアログ。
 * 対象・操作・危険度を必ず示し、危険度Highでは対象名の入力を求める。
 */
const props = withDefaults(
  defineProps<{
    open: boolean
    title: string
    /** 操作対象の名前。危険度Highのときは入力確認にも使う。 */
    targetName: string
    /** 実行する操作の表示名。 */
    actionLabel: string
    risk: 'Low' | 'Medium' | 'High'
    /** 実行中は操作を受け付けない。 */
    busy?: boolean
  }>(),
  { busy: false },
)

const emit = defineEmits<{ confirm: []; cancel: [] }>()

const { t } = useI18n()

const typedName = ref('')
const dialogRef = ref<HTMLElement | null>(null)
const nameInputRef = ref<HTMLInputElement | null>(null)
const cancelButtonRef = ref<HTMLButtonElement | null>(null)
/** ダイアログを開く直前にフォーカスがあった要素。閉じたら戻す。 */
let previouslyFocused: HTMLElement | null = null

const riskTone = computed(() => {
  switch (props.risk) {
    case 'High':
      return 'critical' as const
    case 'Medium':
      return 'medium' as const
    default:
      return 'low' as const
  }
})

const riskLabel = computed(() => {
  switch (props.risk) {
    case 'High':
      return t('severity.high')
    case 'Medium':
      return t('severity.medium')
    default:
      return t('severity.low')
  }
})

/** 危険度Highでは対象名の入力が一致するまで実行できない。 */
const needsNameConfirmation = computed(() => props.risk === 'High')
const nameMatches = computed(() => typedName.value.trim() === props.targetName)
const canConfirm = computed(
  () => !props.busy && (!needsNameConfirmation.value || nameMatches.value),
)

watch(
  () => props.open,
  async (open) => {
    if (open) {
      previouslyFocused = document.activeElement as HTMLElement | null
      typedName.value = ''
      await nextTick()
      // 危険度Highでは入力欄、それ以外は取り消しへ最初のフォーカスを置く
      ;(nameInputRef.value ?? cancelButtonRef.value)?.focus()
    } else {
      previouslyFocused?.focus()
      previouslyFocused = null
    }
  },
)

function handleConfirm(): void {
  if (canConfirm.value) {
    emit('confirm')
  }
}

/** Escで閉じ、Tabはダイアログ内に閉じ込める。 */
function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.stopPropagation()
    emit('cancel')
    return
  }

  if (event.key !== 'Tab' || dialogRef.value === null) {
    return
  }

  const focusable = dialogRef.value.querySelectorAll<HTMLElement>(
    'button:not([disabled]), input:not([disabled]), a[href], select, textarea',
  )
  if (focusable.length === 0) {
    return
  }

  const first = focusable.item(0)
  const last = focusable.item(focusable.length - 1)
  if (first === null || last === null) {
    return
  }

  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first.focus()
  }
}
</script>

<template>
  <div v-if="open" class="confirm-backdrop" @keydown="handleKeydown">
    <div
      ref="dialogRef"
      class="confirm"
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-title"
    >
      <h2 id="confirm-title" class="confirm__title">{{ title }}</h2>

      <dl class="confirm__details">
        <dt>{{ t('confirmDialog.target') }}</dt>
        <dd data-testid="confirm-target">{{ targetName }}</dd>
        <dt>{{ t('confirmDialog.action') }}</dt>
        <dd>{{ actionLabel }}</dd>
        <dt>{{ t('confirmDialog.risk') }}</dt>
        <dd><StatusBadge :tone="riskTone" :label="riskLabel" /></dd>
      </dl>

      <p v-if="needsNameConfirmation" class="confirm__warning" role="alert">
        {{ t('confirmDialog.highRiskWarning') }}
      </p>

      <div v-if="needsNameConfirmation" class="confirm__field">
        <label :for="'confirm-name'">
          {{ t('confirmDialog.typeToConfirm', { name: targetName }) }}
        </label>
        <input
          id="confirm-name"
          ref="nameInputRef"
          v-model="typedName"
          type="text"
          autocomplete="off"
          :aria-invalid="typedName.length > 0 && !nameMatches"
          aria-describedby="confirm-name-error"
        />
        <p
          id="confirm-name-error"
          class="confirm__error"
          :class="{ 'confirm__error--hidden': typedName.length === 0 || nameMatches }"
        >
          {{ typedName.length > 0 && !nameMatches ? t('confirmDialog.nameMismatch') : '' }}
        </p>
      </div>

      <div class="confirm__actions">
        <button
          ref="cancelButtonRef"
          type="button"
          class="confirm__button"
          :disabled="busy"
          @click="emit('cancel')"
        >
          {{ t('common.cancel') }}
        </button>
        <button
          type="button"
          class="confirm__button confirm__button--primary"
          data-testid="confirm-execute"
          :disabled="!canConfirm"
          @click="handleConfirm"
        >
          {{ t('common.execute') }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.confirm-backdrop {
  position: fixed;
  inset: 0;
  z-index: 20;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: var(--spacing-md);
  background-color: rgb(0 0 0 / 50%);
}

.confirm {
  width: min(32rem, 100%);
  max-height: 90vh;
  overflow-y: auto;
  padding: var(--spacing-lg);
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-surface);
}

.confirm__title {
  font-size: 1.125rem;
  font-weight: 600;
  margin-bottom: var(--spacing-md);
}

.confirm__details {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: var(--spacing-xs) var(--spacing-md);
  margin-bottom: var(--spacing-md);
}

.confirm__details dt {
  color: var(--color-text-muted);
}

.confirm__warning {
  padding: var(--spacing-sm);
  margin-bottom: var(--spacing-md);
  border: 1px solid var(--color-critical);
  border-radius: var(--radius);
  background-color: var(--color-critical-bg);
  color: var(--color-critical);
}

.confirm__field {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  margin-bottom: var(--spacing-md);
}

.confirm__field input {
  padding: 0.4em 0.6em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-bg);
}

.confirm__error {
  min-height: 1.5em;
  font-size: 0.875rem;
  color: var(--color-critical);
}

.confirm__error--hidden {
  visibility: hidden;
}

.confirm__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--spacing-sm);
}

.confirm__button {
  padding: 0.45em 1.2em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-surface);
  color: var(--color-text);
  cursor: pointer;
}

.confirm__button--primary {
  border-color: var(--color-accent);
  background-color: var(--color-accent);
  color: var(--color-text-inverse);
}

.confirm__button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
