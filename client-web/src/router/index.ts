import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import AppShell from '@/components/common/AppShell.vue'
import { useAuthStore } from '@/stores/auth'
import type { UserRole } from '@/types/auth'

declare module 'vue-router' {
  interface RouteMeta {
    /** 未ログインでも開ける画面か。 */
    public?: boolean
    /** 開くために必要な役割。未指定ならログイン済みなら誰でも開ける。 */
    roles?: UserRole[]
  }
}

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/auth/LoginView.vue'),
    meta: { public: true },
  },
  {
    path: '/',
    component: AppShell,
    children: [
      {
        path: '',
        name: 'dashboard',
        component: () => import('@/views/operations/DashboardView.vue'),
      },
      {
        path: 'targets',
        name: 'targets',
        component: () => import('@/views/operations/TargetsView.vue'),
      },
      {
        path: 'targets/new',
        name: 'target-new',
        component: () => import('@/views/operations/TargetNewView.vue'),
        meta: { roles: ['OperatorAdmin'] },
      },
      {
        path: 'targets/:id(\\d+)',
        name: 'target-detail',
        component: () => import('@/views/operations/TargetDetailView.vue'),
      },
      {
        path: 'incidents',
        name: 'incidents',
        component: () => import('@/views/operations/IncidentsView.vue'),
      },
      {
        path: 'incidents/:id(\\d+)',
        name: 'incident-detail',
        component: () => import('@/views/operations/IncidentDetailView.vue'),
      },
      {
        path: 'rules',
        name: 'rules',
        component: () => import('@/views/operations/RulesView.vue'),
      },
      {
        path: 'notifications',
        name: 'notifications',
        component: () => import('@/views/operations/NotificationsView.vue'),
      },
      {
        path: 'settings',
        name: 'settings',
        component: () => import('@/views/settings/SettingsView.vue'),
        meta: { roles: ['OperatorAdmin'] },
      },
      {
        path: 'audit-logs',
        name: 'audit-logs',
        component: () => import('@/views/settings/AuditLogsView.vue'),
        meta: { roles: ['OperatorAdmin'] },
      },
      {
        path: 'forbidden',
        name: 'forbidden',
        component: () => import('@/views/ForbiddenView.vue'),
      },
    ],
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/views/NotFoundView.vue'),
    meta: { public: true },
  },
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
})

/**
 * 画面の出し分けは利用者の利便性のためであり、権限の担保はAPI側で行う。
 * ここで通しても、サーバーが403を返せば操作は成立しない。
 */
router.beforeEach(async (to) => {
  const auth = useAuthStore()

  if (to.meta.public === true) {
    // ログイン済みでログイン画面へ来たらダッシュボードへ送る
    if (to.name === 'login' && auth.isAuthenticated) {
      return { name: 'dashboard' }
    }
    return true
  }

  if (!auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  // 再読み込み直後は利用者情報が未取得のため、役割判定の前に読み込む
  if (auth.currentUser === null) {
    try {
      await auth.loadCurrentUser()
    } catch {
      auth.clearTokens()
      return { name: 'login', query: { redirect: to.fullPath } }
    }
  }

  const requiredRoles = to.meta.roles
  if (requiredRoles !== undefined && (auth.role === null || !requiredRoles.includes(auth.role))) {
    return { name: 'forbidden' }
  }

  return true
})

export default router
