import { createI18n } from 'vue-i18n'
import ja from '@/locales/ja'
import en from '@/locales/en'

/**
 * テスト用のi18nインスタンス。
 * アプリ本体の単一インスタンスを共有すると、言語切り替えの検証が他のテストへ影響するため分ける。
 */
export function createTestI18n(locale: 'ja' | 'en' = 'ja') {
  return createI18n({
    legacy: false,
    locale,
    fallbackLocale: 'ja',
    messages: { ja, en },
  })
}
