<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import AsyncState from '@/components/common/AsyncState.vue'

// 一覧・詳細・復旧の承認と実行はT-10で実装する。
const { t } = useI18n()
</script>

<template>
  <div>
    <PageHeader :title="t('nav.incidents')" />
    <AsyncState :loading="false" empty />
  </div>
</template>
