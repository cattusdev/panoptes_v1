import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { currentLanguage } from '@/i18n'
import { defaultGeneralSettings, useGeneralSettings, type GeneralSettings } from '@/lib/settings-api'

// Οι μορφές που δέχεται ο server (GeneralSettings.DateFormats). hint = key μετάφρασης (settings:general.formats.*) ή σκέτο κείμενο.
export const dateFormats = [
  { value: 'd MMM yyyy', hintKey: 'dayMonthYear' },
  { value: 'MMM d, yyyy', hintKey: 'monthDayYear' },
  { value: 'dd/MM/yyyy', hint: 'DD/MM/YYYY' },
  { value: 'MM/dd/yyyy', hint: 'MM/DD/YYYY' },
  { value: 'yyyy-MM-dd', hintKey: 'iso' },
] as const

// Όλες οι ζώνες ώρας του browser, με την τρέχουσα διαφορά από UTC (π.χ. "Europe/Athens · GMT+2")
export function timeZoneOptions() {
  const zones = Intl.supportedValuesOf('timeZone')
  if (!zones.includes('UTC')) zones.unshift('UTC')
  const now = new Date()
  return zones.map((zone) => {
    const offset = new Intl.DateTimeFormat('en-US', { timeZone: zone, timeZoneName: 'shortOffset' })
      .formatToParts(now)
      .find((part) => part.type === 'timeZoneName')?.value
    return { value: zone, label: offset ? `${zone.replaceAll('_', ' ')} · ${offset}` : zone }
  })
}

export type DateFormatter = {
  date: (value: string | Date) => string
  time: (value: string | Date) => string
  dateTime: (value: string | Date) => string
  // Ημερομηνία χωρίς ώρα (DateOnly του server, "yyyy-MM-dd"): ίδια μορφή, χωρίς μετατροπή ζώνης ώρας
  day: (value: string) => string
}

type DateSettings = Pick<GeneralSettings, 'dateFormat' | 'timeFormat' | 'timeZone'>

// Intl για τα κομμάτια (και τη ζώνη ώρας), η σειρά τους από τη ρύθμιση: χωρίς βιβλιοθήκη ημερομηνιών.
// Τα ονόματα μηνών και το π.μ./μ.μ. ακολουθούν τη γλώσσα.
export function createDateFormatter({ dateFormat, timeFormat, timeZone }: DateSettings, language: string = currentLanguage()): DateFormatter {
  let numeric: Intl.DateTimeFormat
  let month: Intl.DateTimeFormat
  try {
    numeric = new Intl.DateTimeFormat('en-US', {
      timeZone: timeZone ?? undefined,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    })
    month = new Intl.DateTimeFormat(language, { timeZone: timeZone ?? undefined, month: 'short' })
  } catch {
    // Άγνωστη ζώνη ώρας στον browser: η τοπική ώρα
    return createDateFormatter({ dateFormat, timeFormat, timeZone: null }, language)
  }

  const [am, pm] = [9, 15].map((hour) => dayPeriod(language, hour))

  function parts(value: string | Date) {
    const date = typeof value === 'string' ? new Date(value) : value
    const p = Object.fromEntries(numeric.formatToParts(date).map((part) => [part.type, part.value]))
    return { year: p.year, month: p.month, day: p.day, hour: Number(p.hour), minute: p.minute, monthName: month.format(date) }
  }

  function formatDate(value: string | Date) {
    const p = parts(value)
    switch (dateFormat) {
      case 'MMM d, yyyy':
        return `${p.monthName} ${Number(p.day)}, ${p.year}`
      case 'dd/MM/yyyy':
        return `${p.day}/${p.month}/${p.year}`
      case 'MM/dd/yyyy':
        return `${p.month}/${p.day}/${p.year}`
      case 'yyyy-MM-dd':
        return `${p.year}-${p.month}-${p.day}`
      default:
        return `${Number(p.day)} ${p.monthName} ${p.year}`
    }
  }

  function formatTime(value: string | Date) {
    const { hour, minute } = parts(value)
    if (timeFormat === '12h') return `${hour % 12 || 12}:${minute} ${hour < 12 ? am : pm}`
    return `${String(hour).padStart(2, '0')}:${minute}`
  }

  // Το DateOnly δεν είναι στιγμή: το διαβάζουμε ως μεσημέρι UTC και το γράφουμε σε UTC, ώστε να μη "γλιστράει" μέρα
  const utc = timeZone === 'UTC' ? null : createDateFormatter({ dateFormat, timeFormat, timeZone: 'UTC' }, language)
  const day = (value: string) => (utc ? utc.date(`${value}T12:00:00Z`) : formatDate(`${value}T12:00:00Z`))

  return {
    date: formatDate,
    time: formatTime,
    day,
    // Το ISO μένει "σκέτο" (2026-11-02 14:30), οι υπόλοιπες με κόμμα (2 Nov 2026, 14:30)
    dateTime: (value) => `${formatDate(value)}${dateFormat === 'yyyy-MM-dd' ? ' ' : ', '}${formatTime(value)}`,
  }
}

// "AM" / "PM" στη γλώσσα (π.χ. "π.μ." / "μ.μ.")
function dayPeriod(language: string, hour: number) {
  const date = new Date(2020, 0, 1, hour)
  const part = new Intl.DateTimeFormat(language, { hour: 'numeric', hour12: true }).formatToParts(date).find((p) => p.type === 'dayPeriod')
  return part?.value ?? (hour < 12 ? 'AM' : 'PM')
}

// Η μορφή ημερομηνίας της εφαρμογής (Settings → General) στην τρέχουσα γλώσσα. Για εμφάνιση προτίμησε το <DateTime>.
export function useDateFormat(): DateFormatter {
  const { data } = useGeneralSettings()
  const { i18n } = useTranslation()
  const settings = data ?? defaultGeneralSettings
  return useMemo(() => createDateFormatter(settings, i18n.language), [settings, i18n.language])
}
