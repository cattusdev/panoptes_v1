import type { ComponentType } from 'react'
import { NavLink } from 'react-router'
import { hasPermission, useSession } from '@/lib/auth'
import { cn } from '@/lib/utils'

export type SettingsNavItem = {
  to: string
  label: string
  icon: ComponentType<{ className?: string }>
  end?: boolean
  permission?: string
}

// Κάθετη λίστα ενοτήτων μιας σελίδας ρυθμίσεων (όπως GitHub / Linear / Stripe).
// Στο κινητό γίνεται οριζόντια σειρά που κάνει scroll.
export function SettingsNav({ label, items }: { label: string; items: SettingsNavItem[] }) {
  const { user } = useSession()
  const visible = items.filter((item) => !item.permission || hasPermission(user, item.permission))

  return (
    <nav aria-label={label} className="-mx-1 flex gap-1 overflow-x-auto px-1 pb-1 md:sticky md:top-24 md:mx-0 md:flex-col md:overflow-visible md:px-0">
      {visible.map((item) => {
        const Icon = item.icon
        return (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.end}
            className={({ isActive }) =>
              cn(
                'flex shrink-0 items-center gap-2.5 rounded-[10px] px-3 py-2 text-sm font-medium whitespace-nowrap text-muted-foreground transition-colors hover:bg-white/4 hover:text-foreground',
                'max-md:rounded-full max-md:border max-md:py-1.5',
                isActive && 'bg-sidebar-accent text-sidebar-accent-foreground max-md:border-transparent',
              )
            }
          >
            <Icon className="size-4 shrink-0" />
            {item.label}
          </NavLink>
        )
      })}
    </nav>
  )
}
