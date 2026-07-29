<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import AsyncState from '@/components/common/AsyncState.vue'

// 監査ログの検索・絞り込み・詳細表示はT-10で実装する。
const { t } = useI18n()
</script>

<template>
  <div>
    <PageHeader :title="t('nav.auditLogs')" />
    <AsyncState :loading="false" empty />
  </div>
</template>
