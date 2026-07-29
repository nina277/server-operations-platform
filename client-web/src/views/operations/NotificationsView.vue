<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import AsyncState from '@/components/common/AsyncState.vue'

// 通知一覧・既読管理・購読設定はT-10で実装する。
const { t } = useI18n()
</script>

<template>
  <div>
    <PageHeader :title="t('nav.notifications')" />
    <AsyncState :loading="false" empty />
  </div>
</template>
