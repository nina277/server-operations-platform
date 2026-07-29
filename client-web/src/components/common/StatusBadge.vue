<script setup lang="ts">
import { computed } from 'vue'

/**
 * 状態表示。色・文字・アイコンの3要素で表すため、色だけに依存しない。
 */
const props = withDefaults(
  defineProps<{
    /** 状態の種類。配色とアイコンを決める。 */
    tone?: 'critical' | 'high' | 'medium' | 'low' | 'neutral'
    /** 表示する文字。必須(色だけで意味を伝えない)。 */
    label: string
  }>(),
  { tone: 'neutral' },
)

/** アイコンは記号で表し、画像に依存しない。 */
const icon = computed(() => {
  switch (props.tone) {
    case 'critical':
      return '✖' // ✖
    case 'high':
      return '⚠' // ⚠
    case 'medium':
      return '▲' // ▲
    case 'low':
      return '✔' // ✔
    default:
      return '●' // ●
  }
})
</script>

<template>
  <span :class="['status-badge', `status-badge--${tone}`]">
    <span class="status-badge__icon" aria-hidden="true">{{ icon }}</span>
    <span class="status-badge__label">{{ label }}</span>
  </span>
</template>

<style scoped>
.status-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.35em;
  padding: 0.15em 0.6em;
  border: 1px solid currentColor;
  border-radius: var(--radius);
  font-size: 0.875rem;
  font-weight: 600;
  white-space: nowrap;
}

.status-badge__icon {
  font-size: 0.9em;
  line-height: 1;
}

.status-badge--critical {
  color: var(--color-critical);
  background-color: var(--color-critical-bg);
}

.status-badge--high {
  color: var(--color-high);
  background-color: var(--color-high-bg);
}

.status-badge--medium {
  color: var(--color-medium);
  background-color: var(--color-medium-bg);
}

.status-badge--low {
  color: var(--color-low);
  background-color: var(--color-low-bg);
}

.status-badge--neutral {
  color: var(--color-neutral);
  background-color: var(--color-neutral-bg);
}
</style>
