import type { ReactNode } from 'react'
import { Clock } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { toneVariant, type StatusTone } from '@/lib/status'

// Το κοινό status badge: χρώμα από τη σημασία (lib/status.ts), κείμενο πάντα. Το "historical" έχει και ρολόι.
export function StatusBadge({ tone, children, className }: { tone: StatusTone; children: ReactNode; className?: string }) {
  return (
    <Badge variant={toneVariant[tone]} data-tone={tone} className={className}>
      {tone === 'historical' && <Clock data-icon="inline-start" aria-hidden />}
      {children}
    </Badge>
  )
}
