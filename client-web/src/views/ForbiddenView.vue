<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'

const { t } = useI18n()
</script>

<template>
  <div>
    <PageHeader :title="t('common.forbidden')" />
    <p role="alert">{{ t('common.forbidden') }}</p>
  </div>
</template>
