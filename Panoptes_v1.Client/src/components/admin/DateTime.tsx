import { useDateFormat } from '@/lib/dates'
import { timeAgo } from '@/lib/format'

// Ημερομηνία με τη μορφή της εφαρμογής. "relative" = "5m ago" με την ακριβή ώρα στο tooltip.
export function DateTime({
  value,
  mode = 'dateTime',
  className,
}: {
  value: string
  mode?: 'date' | 'dateTime' | 'relative'
  className?: string
}) {
  const format = useDateFormat()
  const exact = format.dateTime(value)
  const text = mode === 'relative' ? timeAgo(value) : mode === 'date' ? format.date(value) : exact

  return (
    <time dateTime={value} title={mode === 'dateTime' ? undefined : exact} className={className}>
      {text}
    </time>
  )
}
