import { getCountries, getCountryCallingCode, isValidPhoneNumber, parsePhoneNumberFromString, type CountryCode } from 'libphonenumber-js/max'

// Τηλέφωνα σε όλη την εφαρμογή: πάντα με κωδικό χώρας, ελεγμένα με το libphonenumber (metadata "max", όσο αυστηρό
// είναι και το libphonenumber-csharp του server) και αποθηκευμένα σε E.164 ("+302101234567"). Ελλάδα προεπιλογή.
export const defaultPhoneCountry: CountryCode = 'GR'

export type { CountryCode }

// Η τιμή του PhoneInput (E.164, ή ό,τι έγραψε ο χρήστης όσο δεν διαβάζεται): έγκυρος αριθμός;
export function isValidPhone(value: string) {
  return isValidPhoneNumber(value, defaultPhoneCountry)
}

// Ό,τι έγραψε ο χρήστης, με τη χώρα που διάλεξε -> E.164 (ή το κείμενο όπως είναι, για να το απορρίψει το schema)
export function toE164(text: string, country: CountryCode) {
  const trimmed = text.trim()
  if (!trimmed) return ''
  return parsePhoneNumberFromString(trimmed, country)?.number ?? trimmed
}

export function parsePhone(value: string | null | undefined) {
  return value ? parsePhoneNumberFromString(value, defaultPhoneCountry) : undefined
}

// Για εμφάνιση: "+30 210 123 4567". Ό,τι δεν διαβάζεται (παλιές τιμές) μένει όπως είναι.
export function formatPhone(value: string) {
  const phone = parsePhone(value)
  return phone?.isValid() ? phone.formatInternational() : value
}

// Για tel: links
export function phoneHref(value: string) {
  return `tel:${parsePhone(value)?.number ?? value.replace(/[^\d+]/g, '')}`
}

export function callingCode(country: CountryCode) {
  return `+${getCountryCallingCode(country)}`
}

// Οι χώρες με το όνομά τους στη γλώσσα του χρήστη, αλφαβητικά, με την Ελλάδα πρώτη
export function phoneCountries(language: string) {
  const names = new Intl.DisplayNames([language], { type: 'region' })
  const collator = new Intl.Collator(language)
  const all = getCountries().map((code) => ({ code, name: names.of(code) ?? code, callingCode: callingCode(code) }))
  all.sort((a, b) => (a.code === defaultPhoneCountry ? -1 : b.code === defaultPhoneCountry ? 1 : collator.compare(a.name, b.name)))
  return all
}
