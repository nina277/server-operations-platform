<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import AsyncState from '@/components/common/AsyncState.vue'

// 自動復旧の有効化・コンテナ許可リスト・通知・AI・バックアップの設定はT-10で実装する。
const { t } = useI18n()
</script>

<template>
  <div>
    <PageHeader :title="t('nav.settings')" />
    <AsyncState :loading="false" empty />
  </div>
</template>
