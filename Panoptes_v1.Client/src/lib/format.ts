import i18n, { currentLanguage } from '@/i18n'

// "Just now", "5m ago", "3h ago", "2d ago" (στα ελληνικά "5 λ. πριν" κ.λπ.)
// Και για το μέλλον ("in 5 min", π.χ. πότε θα τρέξει μια δουλειά): ίδια βήματα, θετικό πρόσημο
export function timeAgo(isoDate: string) {
  const seconds = (Date.now() - new Date(isoDate).getTime()) / 1000
  const elapsed = Math.abs(seconds)
  if (elapsed < 60) return i18n.t('time.justNow')
  const sign = seconds < 0 ? 1 : -1
  const format = new Intl.RelativeTimeFormat(currentLanguage(), { style: 'narrow', numeric: 'always' })
  if (elapsed < 3600) return format.format(sign * Math.floor(elapsed / 60), 'minute')
  if (elapsed < 86_400) return format.format(sign * Math.floor(elapsed / 3600), 'hour')
  return format.format(sign * Math.floor(elapsed / 86_400), 'day')
}

export function initials(email: string) {
  const name = email.split('@')[0] ?? ''
  const parts = name.split(/[._-]/).filter(Boolean)
  return parts.length >= 2 ? `${parts[0][0]}${parts[1][0]}`.toUpperCase() : name.slice(0, 2).toUpperCase()
}
