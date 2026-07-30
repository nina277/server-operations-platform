<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { setLocale, type AppLocale } from '@/locales'
import type { UserRole } from '@/types/auth'

/**
 * 画面全体の骨組み。ヘッダー・主要メニュー・本文を配置する。
 * Desktopは左固定メニュー、Mobileは開閉式メニューへ切り替える。
 */
const { t, locale } = useI18n()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

interface NavItem {
  name: string
  labelKey: string
  /** 表示を許可する役割。未指定なら全役割に表示する。 */
  roles?: UserRole[]
}

const navItems: NavItem[] = [
  { name: 'dashboard', labelKey: 'nav.dashboard' },
  { name: 'targets', labelKey: 'nav.targets' },
  { name: 'incidents', labelKey: 'nav.incidents' },
  { name: 'rules', labelKey: 'nav.rules' },
  { name: 'notifications', labelKey: 'nav.notifications' },
  { name: 'settings', labelKey: 'nav.settings', roles: ['OperatorAdmin'] },
  { name: 'audit-logs', labelKey: 'nav.auditLogs', roles: ['OperatorAdmin'] },
  { name: 'account', labelKey: 'account.title' },
]

// 権限のない項目は最初から見せない(押せない項目を並べても混乱を招くため)
const visibleNavItems = computed(() =>
  navItems.filter(
    (item) => item.roles === undefined || (auth.role !== null && item.roles.includes(auth.role)),
  ),
)

const menuOpen = ref(false)

// 画面遷移したらMobileの開閉メニューを閉じる
watch(
  () => route.fullPath,
  () => {
    menuOpen.value = false
  },
)

function changeLocale(event: Event): void {
  setLocale((event.target as HTMLSelectElement).value as AppLocale)
}

async function handleLogout(): Promise<void> {
  await auth.logout()
  await router.push({ name: 'login' })
}
</script>

<template>
  <div class="shell">
    <a class="shell__skip" href="#main-content">{{ t('nav.skipToContent') }}</a>

    <header class="shell__header">
      <button
        type="button"
        class="shell__menu-toggle"
        :aria-expanded="menuOpen"
        aria-controls="main-navigation"
        @click="menuOpen = !menuOpen"
      >
        <span aria-hidden="true">☰</span>
        <span class="shell__sr-only">{{
          menuOpen ? t('common.closeMenu') : t('common.openMenu')
        }}</span>
      </button>

      <p class="shell__brand">{{ t('app.shortName') }}</p>

      <div class="shell__header-end">
        <label class="shell__locale">
          <span class="shell__sr-only">{{ t('common.language') }}</span>
          <select :value="locale" @change="changeLocale">
            <option value="ja">{{ t('common.japanese') }}</option>
            <option value="en">{{ t('common.english') }}</option>
          </select>
        </label>

        <RouterLink v-if="auth.currentUser" :to="{ name: 'account' }" class="shell__user">
          <span class="shell__user-name">{{ auth.currentUser.username }}</span>
          <span class="shell__user-role">{{ t(`role.${auth.currentUser.role}`) }}</span>
        </RouterLink>

        <button type="button" class="shell__logout" @click="handleLogout">
          {{ t('nav.logout') }}
        </button>
      </div>
    </header>

    <div class="shell__body">
      <nav
        id="main-navigation"
        class="shell__nav"
        :class="{ 'shell__nav--open': menuOpen }"
        :aria-label="t('nav.mainNavigation')"
      >
        <ul>
          <li v-for="item in visibleNavItems" :key="item.name">
            <RouterLink :to="{ name: item.name }" class="shell__nav-link">
              {{ t(item.labelKey) }}
            </RouterLink>
          </li>
        </ul>
      </nav>

      <main id="main-content" class="shell__main" tabindex="-1">
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
.shell {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.shell__sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip-path: inset(50%);
  white-space: nowrap;
}

/* フォーカスが当たったときだけ現れる本文へのスキップリンク */
.shell__skip {
  position: absolute;
  left: var(--spacing-sm);
  top: -3rem;
  z-index: 10;
  padding: var(--spacing-sm) var(--spacing-md);
  background-color: var(--color-surface);
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  color: var(--color-text);
}

.shell__skip:focus {
  top: var(--spacing-sm);
}

.shell__header {
  display: flex;
  align-items: center;
  gap: var(--spacing-md);
  padding: var(--spacing-sm) var(--spacing-md);
  border-bottom: 1px solid var(--color-border);
  background-color: var(--color-surface);
}

.shell__brand {
  font-weight: 600;
}

.shell__header-end {
  display: flex;
  align-items: center;
  gap: var(--spacing-md);
  margin-left: auto;
}

.shell__user {
  display: flex;
  flex-direction: column;
  line-height: 1.2;
  color: var(--color-text);
  text-decoration: none;
}

.shell__user:hover {
  text-decoration: underline;
}

.shell__user-role {
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.shell__locale select,
.shell__logout {
  padding: 0.3em 0.7em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-surface);
  cursor: pointer;
}

.shell__body {
  flex: 1;
  display: flex;
  min-height: 0;
}

.shell__nav {
  flex: 0 0 auto;
  width: 14rem;
  padding: var(--spacing-md) var(--spacing-sm);
  border-right: 1px solid var(--color-border);
  background-color: var(--color-surface);
}

.shell__nav ul {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.shell__nav-link {
  display: block;
  padding: 0.5em 0.75em;
  border-radius: var(--radius);
  color: var(--color-text);
  text-decoration: none;
}

.shell__nav-link:hover {
  background-color: var(--color-surface-alt);
}

/* 現在地は色だけでなく左端の線と太字でも示す */
.shell__nav-link.router-link-active {
  background-color: var(--color-surface-alt);
  border-left: 3px solid var(--color-accent);
  font-weight: 600;
}

.shell__main {
  flex: 1;
  min-width: 0;
  padding: var(--spacing-lg) var(--spacing-md);
}

.shell__menu-toggle {
  display: none;
  padding: 0.3em 0.6em;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius);
  background-color: var(--color-surface);
  cursor: pointer;
}

/* Tablet: メニュー幅を詰めて本文の領域を確保する */
@media (max-width: 1279px) {
  .shell__nav {
    width: 11rem;
  }
}

/* Mobile: メニューは開閉式にする */
@media (max-width: 767px) {
  .shell__menu-toggle {
    display: inline-flex;
  }

  .shell__body {
    flex-direction: column;
  }

  .shell__nav {
    display: none;
    width: 100%;
    border-right: none;
    border-bottom: 1px solid var(--color-border);
  }

  .shell__nav--open {
    display: block;
  }

  .shell__user {
    display: none;
  }
}
</style>
