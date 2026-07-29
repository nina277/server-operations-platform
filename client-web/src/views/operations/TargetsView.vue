<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import PageHeader from '@/components/common/PageHeader.vue'
import AsyncState from '@/components/common/AsyncState.vue'

// 一覧・登録・接続試験はT-10で実装する。
const { t } = useI18n()
</script>

<template>
  <div>
    <PageHeader :title="t('nav.targets')" />
    <AsyncState :loading="false" empty />
  </div>
</template>
