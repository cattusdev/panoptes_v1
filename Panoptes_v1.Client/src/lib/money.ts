import { currentLanguage } from '@/i18n'

// Ποσά: πάντα με νόμισμα, με Intl στη γλώσσα του χρήστη (€1,240.00 / 1.240,00 €). Δύο δεκαδικά, όπως ο server (numeric(18,2)).
export function formatMoney(amount: number, currency: string, language: string = currentLanguage()) {
  return new Intl.NumberFormat(language, { style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)
}

// Το ποσό χωρίς σύμβολο, για το πεδίο εισαγωγής (1,240.00 / 1.240,00)
export function formatAmount(amount: number, language: string = currentLanguage()) {
  return new Intl.NumberFormat(language, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)
}

// Το σύμβολο του νομίσματος στη γλώσσα (€, $, ...)
export function currencySymbol(currency: string, language: string = currentLanguage()) {
  return new Intl.NumberFormat(language, { style: 'currency', currency }).formatToParts(0).find((p) => p.type === 'currency')?.value ?? currency
}

function decimalSeparator(language: string) {
  return new Intl.NumberFormat(language).formatToParts(1.5).find((p) => p.type === 'decimal')?.value ?? '.'
}

// Ό,τι γράφει ο χρήστης -> αριθμός. Δέχεται και τις δύο μορφές ("1.234,50" και "1,234.50"), κενά και σύμβολα νομίσματος.
//   κενό -> null · μη έγκυρο ή πάνω από 2 δεκαδικά -> NaN
// Με ένα μόνο διαχωριστικό: αν είναι αυτό της γλώσσας είναι δεκαδικό, αλλιώς χιλιάδες μόνο αν ακολουθούν ακριβώς 3 ψηφία.
export function parseAmount(text: string, language: string = currentLanguage()): number | null {
  let value = text.replace(/[\s  €$£]/g, '').replace(/^[−–]/, '-')
  if (value === '') return null

  const negative = value.startsWith('-')
  if (negative) value = value.slice(1)
  if (!/^[\d.,]+$/.test(value) || !/\d/.test(value)) return NaN

  const lastDot = value.lastIndexOf('.')
  const lastComma = value.lastIndexOf(',')
  let decimal: '.' | ',' | null = null
  if (lastDot >= 0 && lastComma >= 0) {
    decimal = lastDot > lastComma ? '.' : ','
  } else if (lastDot >= 0 || lastComma >= 0) {
    const separator = lastDot >= 0 ? '.' : ','
    const count = value.split(separator).length - 1
    const digitsAfter = value.length - value.lastIndexOf(separator) - 1
    if (count === 1 && (separator === decimalSeparator(language) || digitsAfter !== 3)) decimal = separator
  }

  const [whole, fraction = ''] = decimal ? [value.slice(0, value.lastIndexOf(decimal)), value.slice(value.lastIndexOf(decimal) + 1)] : [value, '']
  const digits = whole.replace(/[.,]/g, '')
  // Οι χιλιάδες πρέπει να είναι ομάδες των 3 ψηφίων (όχι "1,2,3")
  if (/[.,]/.test(whole) && !/^\d{1,3}([.,]\d{3})+$/.test(whole)) return NaN
  if (fraction.length > 2 || /[.,]/.test(fraction) || (digits === '' && fraction === '')) return NaN

  const amount = Number(`${digits || '0'}.${fraction || '0'}`)
  return negative ? -amount : amount
}
