import { createI18n } from 'vue-i18n'
import ja from './ja'
import en from './en'

const STORAGE_KEY = 'sop.locale'

export type AppLocale = 'ja' | 'en'

function resolveInitialLocale(): AppLocale {
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'ja' || stored === 'en') {
    return stored
  }
  return navigator.language.startsWith('ja') ? 'ja' : 'en'
}

export const i18n = createI18n({
  legacy: false,
  locale: resolveInitialLocale(),
  fallbackLocale: 'ja',
  messages: { ja, en },
})

export function setLocale(locale: AppLocale): void {
  i18n.global.locale.value = locale
  localStorage.setItem(STORAGE_KEY, locale)
  document.documentElement.lang = locale
}
