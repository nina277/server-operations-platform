<script setup lang="ts">
import { computed } from 'vue'

/**
 * 数値指標の表示。値が未取得のときは0ではなく「—」を出し、
 * 「取得できていない」と「0である」を区別できるようにする。
 */
const props = withDefaults(
  defineProps<{
    label: string
    value: number | string | null | undefined
    unit?: string
    /** 小数点以下の桁数。数値のときだけ使う。 */
    fractionDigits?: number
  }>(),
  { unit: undefined, fractionDigits: 0 },
)

const displayValue = computed(() => {
  if (props.value === null || props.value === undefined) {
    return '—'
  }
  if (typeof props.value === 'number') {
    return Number.isFinite(props.value) ? props.value.toFixed(props.fractionDigits) : '—'
  }
  return props.value
})

const hasValue = computed(() => displayValue.value !== '—')
</script>

<template>
  <div class="metric">
    <dt class="metric__label">{{ label }}</dt>
    <dd class="metric__value">
      <span class="metric__number">{{ displayValue }}</span>
      <span v-if="unit && hasValue" class="metric__unit">{{ unit }}</span>
    </dd>
  </div>
</template>

<style scoped>
.metric {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  padding: var(--spacing-md);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background-color: var(--color-surface);
}

.metric__label {
  font-size: 0.875rem;
  color: var(--color-text-muted);
}

.metric__value {
  display: flex;
  align-items: baseline;
  gap: 0.25em;
}

.metric__number {
  font-size: 1.75rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.metric__unit {
  font-size: 0.875rem;
  color: var(--color-text-muted);
}
</style>
