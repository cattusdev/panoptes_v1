import { useTranslation } from 'react-i18next'
import { Link, Outlet } from 'react-router'
import { ShieldX } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { homePath, useCan, useSession } from '@/lib/auth'

// Route guard: η σελίδα ανοίγει μόνο με το permission της. Είναι UX (όχι ασφάλεια, αυτήν την κάνει ο server):
// αντί για μισοφορτωμένη σελίδα με 403, ένα καθαρό "δεν έχετε πρόσβαση" μέσα στο console, στο ίδιο URL.
export function RequirePermission({ permission }: { permission: string }) {
  return useCan(permission) ? <Outlet /> : <NoAccess />
}

export function NoAccess() {
  const { t } = useTranslation()
  const { user } = useSession()

  return (
    <Empty className="min-h-[60vh]" data-testid="no-access">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <ShieldX />
        </EmptyMedia>
        <EmptyTitle role="heading" aria-level={1} className="text-base">
          {t('noAccess.title')}
        </EmptyTitle>
        <EmptyDescription>{t('noAccess.text')}</EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Button asChild variant="outline">
          <Link to={homePath(user)}>{t('noAccess.home')}</Link>
        </Button>
      </EmptyContent>
    </Empty>
  )
}
