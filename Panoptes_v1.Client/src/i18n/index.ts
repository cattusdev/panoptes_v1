import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en, { type Resources } from './locales/en'

// Οι γλώσσες της εφαρμογής. Τα αγγλικά είναι μέσα στο bundle (και fallback), οι υπόλοιπες φορτώνονται όταν χρειαστούν.
export const languages = [
  { code: 'en', name: 'English' },
  { code: 'el', name: 'Ελληνικά' },
] as const

export type Language = (typeof languages)[number]['code']

export const defaultLanguage: Language = 'en'

const loaders: Record<Language, () => Promise<Resources>> = {
  en: async () => en,
  el: async () => (await import('./locales/el')).default,
}

export function isLanguage(value: unknown): value is Language {
  return languages.some((language) => language.code === value)
}

// Η τελευταία γλώσσα που δείξαμε: με αυτήν ξεκινάει η επόμενη φόρτωση (χωρίς "αναβόσβημα")
const lastKey = 'panoptes-v1.language'
// Η επιλογή επισκέπτη χωρίς λογαριασμό (footer των δημόσιων σελίδων)
const choiceKey = 'panoptes-v1.language.choice'

function read(key: string) {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function write(key: string, value: string | null) {
  try {
    if (value === null) localStorage.removeItem(key)
    else localStorage.setItem(key, value)
  } catch {
    // private mode κ.λπ.: απλά δεν το θυμόμαστε
  }
}

export function visitorChoice(): Language | null {
  const value = read(choiceKey)
  return isLanguage(value) ? value : null
}

export function setVisitorChoice(language: Language | null) {
  write(choiceKey, language)
}

// Σειρά: η προτίμηση του χρήστη, η επιλογή του επισκέπτη, η προεπιλογή της εφαρμογής (Settings → General)
export function resolveLanguage(userLanguage: string | null | undefined, appDefault: string | null | undefined): Language {
  if (isLanguage(userLanguage)) return userLanguage
  return visitorChoice() ?? (isLanguage(appDefault) ? appDefault : defaultLanguage)
}

async function ensureLoaded(language: Language) {
  if (i18n.hasResourceBundle(language, 'common')) return
  const resources = await loaders[language]()
  for (const [namespace, bundle] of Object.entries(resources)) i18n.addResourceBundle(language, namespace, bundle, true, true)
}

export async function setLanguage(language: Language) {
  await ensureLoaded(language)
  await i18n.changeLanguage(language)
  write(lastKey, language)
}

export async function initI18n() {
  const stored = read(lastKey)
  const initial: Language = isLanguage(stored) ? stored : defaultLanguage

  i18n.on('languageChanged', (language) => {
    document.documentElement.lang = language
  })

  await i18n.use(initReactI18next).init({
    resources: { en },
    lng: defaultLanguage,
    fallbackLng: defaultLanguage,
    ns: Object.keys(en),
    defaultNS: 'common',
    interpolation: { escapeValue: false }, // Το React κάνει ήδη escape
    returnNull: false,
  })
  if (initial !== defaultLanguage) await setLanguage(initial)
  else document.documentElement.lang = initial
}

// Η τρέχουσα γλώσσα για Intl (ημερομηνίες, αριθμοί) και για το Accept-Language των requests
export function currentLanguage(): Language {
  return isLanguage(i18n.language) ? i18n.language : defaultLanguage
}

export default i18n
