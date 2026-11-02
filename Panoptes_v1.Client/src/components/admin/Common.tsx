import type { ComponentType, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Search } from 'lucide-react'
import { StatusBadge } from '@/components/admin/StatusBadge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Input } from '@/components/ui/input'
import { initials } from '@/lib/format'
import { userStatus } from '@/lib/status'
import { cn } from '@/lib/utils'

// Φωτογραφία προφίλ ή αρχικά (όταν δεν υπάρχει φωτογραφία)
export function UserAvatar({
  email,
  avatarUrl,
  active,
  size = 'default',
}: {
  email: string
  avatarUrl?: string | null
  active?: boolean
  size?: 'default' | 'lg' | 'xl'
}) {
  return (
    <span
      className={cn(
        'relative inline-flex shrink-0 items-center justify-center rounded-full bg-[#252240] font-semibold text-[#a5a3ff]',
        size === 'xl' ? 'size-20 text-2xl' : size === 'lg' ? 'size-14 text-lg' : 'size-10 text-sm',
      )}
    >
      {avatarUrl ? <img src={avatarUrl} alt="" className="size-full rounded-full object-cover" /> : initials(email)}
      {active !== undefined && (
        <span
          className={cn(
            'absolute -right-px -bottom-px size-[11px] rounded-full border-2 border-card',
            active ? 'bg-green-500' : 'bg-zinc-500',
          )}
        />
      )}
    </span>
  )
}

export function UserStatusBadge({ isLockedOut, emailConfirmed }: { isLockedOut: boolean; emailConfirmed: boolean }) {
  const { t } = useTranslation('admin')
  const status = userStatus({ isLockedOut, emailConfirmed })
  return <StatusBadge tone={status.tone}>{t(`status.${status.key}`)}</StatusBadge>
}

export function SearchInput({ value, onChange, placeholder }: { value: string; onChange: (value: string) => void; placeholder: string }) {
  return (
    <div className="relative w-full sm:w-72">
      <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
      <Input
        type="search"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label={placeholder}
        className="h-9 pl-9"
      />
    </div>
  )
}

export function TableEmpty({ icon: Icon, title, description }: { icon: ComponentType; title: string; description: string }) {
  return (
    <Empty className="py-12">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <Icon />
        </EmptyMedia>
        <EmptyTitle>{title}</EmptyTitle>
        <EmptyDescription>{description}</EmptyDescription>
      </EmptyHeader>
    </Empty>
  )
}

// Ενότητα ρυθμίσεων (Account, Security, ...)
// footer: γραμμή κάτω από την κάρτα (όπως στο Vercel): σημείωση αριστερά, κουμπιά δεξιά
export function SettingsCard({
  title,
  description,
  children,
  footer,
  className,
}: {
  title: string
  description?: string
  children: ReactNode
  footer?: ReactNode
  className?: string
}) {
  return (
    <Card className={cn('gap-0 rounded-[14px] py-0 ring-border', className)}>
      <CardHeader className="border-b px-5.5 py-4.5 [.border-b]:pb-4.5">
        <CardTitle>{title}</CardTitle>
        {description && <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent className="p-5.5">{children}</CardContent>
      {footer && (
        <div className="flex flex-wrap items-center justify-end gap-3 rounded-b-[14px] border-t bg-muted/30 px-5.5 py-3.5 text-sm text-muted-foreground">
          {footer}
        </div>
      )}
    </Card>
  )
}

// "Danger zone" (όπως GitHub / Vercel): μη αναστρέψιμες ενέργειες στο τέλος της σελίδας
export function DangerZone({ title, description, action }: { title: string; description: string; action: ReactNode }) {
  const { t } = useTranslation('admin')
  return (
    <Card className="gap-0 rounded-[14px] py-0 ring-destructive/30">
      <CardHeader className="border-b border-destructive/20 px-5.5 py-4.5 [.border-b]:pb-4.5">
        <CardTitle className="text-destructive">{t('shared.dangerZone')}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-wrap items-center justify-between gap-4 p-5.5">
        <div>
          <div className="font-medium">{title}</div>
          <div className="text-sm text-muted-foreground">{description}</div>
        </div>
        {action}
      </CardContent>
    </Card>
  )
}
