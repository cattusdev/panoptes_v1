import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { History } from 'lucide-react'
import { DateTime } from '@/components/admin/DateTime'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { initials } from '@/lib/format'

export type TimelineItem = {
  id: string
  actor: { id: string; email: string } | null
  source: string | null
  occurredAt: string
}

// Το ιστορικό δραστηριότητας μιας εγγραφής (ARCHITECTURE §9.2), το πιο πρόσφατο πρώτο. Το κείμενο κάθε γραμμής
// το δίνει το module (describe), γιατί μόνο αυτό ξέρει τι σημαίνει το "client.archived" ή το "task.statusChanged".
export function ActivityTimeline<T extends TimelineItem>({
  items,
  isLoading,
  describe,
  emptyTitle,
  emptyDescription,
}: {
  items: T[] | undefined
  isLoading?: boolean
  describe: (item: T, actor: string) => ReactNode
  emptyTitle: string
  emptyDescription: string
}) {
  const { t } = useTranslation()

  if (isLoading || !items) {
    return (
      <div className="flex flex-col gap-4" aria-busy>
        {Array.from({ length: 3 }, (_, i) => (
          <div key={i} className="flex gap-3">
            <Skeleton className="size-7 rounded-full" />
            <div className="flex flex-1 flex-col gap-1.5">
              <Skeleton className="h-4 w-3/4" />
              <Skeleton className="h-3 w-24" />
            </div>
          </div>
        ))}
      </div>
    )
  }

  if (items.length === 0) {
    return (
      <Empty className="py-10">
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <History />
          </EmptyMedia>
          <EmptyTitle>{emptyTitle}</EmptyTitle>
          <EmptyDescription>{emptyDescription}</EmptyDescription>
        </EmptyHeader>
      </Empty>
    )
  }

  return (
    <ol className="relative flex flex-col gap-5 before:absolute before:top-2 before:bottom-2 before:left-3.5 before:w-px before:bg-border">
      {items.map((item) => {
        const actor = item.actor?.email || t('activity.system')
        return (
          <li key={item.id} className="relative flex gap-3">
            <span
              className="z-10 inline-flex size-7 shrink-0 items-center justify-center rounded-full border bg-card text-[11px] font-semibold text-muted-foreground"
              aria-hidden
            >
              {item.actor ? initials(item.actor.email) : <History className="size-3.5" />}
            </span>
            <div className="min-w-0 pt-0.5">
              <div className="text-sm break-words">{describe(item, actor)}</div>
              <DateTime value={item.occurredAt} mode="relative" className="text-xs text-muted-foreground" />
            </div>
          </li>
        )
      })}
    </ol>
  )
}
