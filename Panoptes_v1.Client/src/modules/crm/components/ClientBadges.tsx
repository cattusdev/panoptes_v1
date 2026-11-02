import { useTranslation } from 'react-i18next'
import { Building2, User } from 'lucide-react'
import { StatusBadge } from '@/components/admin/StatusBadge'
import { clientStatus } from '@/lib/status'
import type { ClientKind } from '../api'

export function ClientStatusBadge({ client }: { client: { status: string; isArchived?: boolean; archivedAt?: string | null } }) {
  const { t } = useTranslation('crm')
  const status = clientStatus(client)
  return <StatusBadge tone={status.tone}>{t(`status.${status.key}`)}</StatusBadge>
}

// Εταιρεία ή ιδιώτης: εικονίδιο με κείμενο για τους screen readers
export function KindIcon({ kind, className }: { kind: ClientKind; className?: string }) {
  const { t } = useTranslation('crm')
  const Icon = kind === 'Individual' ? User : Building2
  return (
    <span className={className ?? 'inline-flex size-8 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground'} title={t(`kinds.${kind}`)}>
      <Icon className="size-4" aria-hidden />
      <span className="sr-only">{t(`kinds.${kind}`)}</span>
    </span>
  )
}
