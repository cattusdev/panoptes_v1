import type { ComponentType } from 'react'
import { cn } from '@/lib/utils'

const tints = {
  blue: 'border-info/35 bg-info/12 text-info',
  green: 'border-success/35 bg-success/12 text-success',
  purple: 'border-purple/35 bg-purple/14 text-purple',
  amber: 'border-warning/35 bg-warning/12 text-warning',
} as const

export type Tint = keyof typeof tints

// Τετράγωνο εικονίδιο με απαλό χρώμα (λίστες ρόλων κ.λπ.)
export function IconTile({ icon: Icon, tint, className }: { icon: ComponentType<{ className?: string }>; tint: Tint; className?: string }) {
  return (
    <span className={cn('inline-flex size-11 shrink-0 items-center justify-center rounded-xl border', tints[tint], className)}>
      <Icon className="size-5" />
    </span>
  )
}
