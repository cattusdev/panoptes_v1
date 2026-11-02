import i18n, { currentLanguage } from '@/i18n'

// "Just now", "5m ago", "3h ago", "2d ago" (στα ελληνικά "5 λ. πριν" κ.λπ.)
export function timeAgo(isoDate: string) {
  const seconds = (Date.now() - new Date(isoDate).getTime()) / 1000
  if (seconds < 60) return i18n.t('time.justNow')
  const format = new Intl.RelativeTimeFormat(currentLanguage(), { style: 'narrow', numeric: 'always' })
  if (seconds < 3600) return format.format(-Math.floor(seconds / 60), 'minute')
  if (seconds < 86_400) return format.format(-Math.floor(seconds / 3600), 'hour')
  return format.format(-Math.floor(seconds / 86_400), 'day')
}

export function initials(email: string) {
  const name = email.split('@')[0] ?? ''
  const parts = name.split(/[._-]/).filter(Boolean)
  return parts.length >= 2 ? `${parts[0][0]}${parts[1][0]}`.toUpperCase() : name.slice(0, 2).toUpperCase()
}
