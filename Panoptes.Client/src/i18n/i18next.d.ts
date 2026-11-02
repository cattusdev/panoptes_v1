import 'i18next'
import type { Resources } from './locales/en'

// Typed keys: ένα λάθος ή άγνωστο key στο t() είναι λάθος του TypeScript
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'common'
    resources: Resources
    returnNull: false
  }
}
