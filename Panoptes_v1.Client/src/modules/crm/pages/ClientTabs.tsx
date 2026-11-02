import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { ArrowRight, CircleCheck, Circle } from 'lucide-react'
import { SettingsCard } from '@/components/admin/Common'
import { Button } from '@/components/ui/button'
import { Alert, AlertAction, AlertDescription } from '@/components/ui/alert'
import { problemMessage } from '@/lib/api'
import { useClientActivity } from '../api'
import { ClientActivityList } from '../components/ClientActivity'
import { ClientFilesList } from '../components/ClientFiles'
import { useClientContext } from './client-context'

// Overview: τα πρώτα βήματα για νέο πελάτη και η πρόσφατη δραστηριότητα. Τα "Needs attention", projects και services
// μπαίνουν εδώ μαζί με τα modules τους.
export function ClientOverviewTab() {
  const { t } = useTranslation('crm')
  const { client, contacts, openAddContact } = useClientContext()
  const activity = useClientActivity(client.id, 1)

  // Μόνο ό,τι υπάρχει ήδη στην εφαρμογή (χωρίς βήματα που δεν οδηγούν πουθενά)
  const steps = [{ key: 'contact' as const, done: (contacts?.length ?? 0) > 0, action: client.canEdit ? openAddContact : undefined }]
  const showChecklist = contacts !== undefined && steps.some((step) => !step.done)

  return (
    <div className="flex flex-col gap-6">
      {showChecklist && (
        <SettingsCard title={t('overview.getStarted')} description={t('overview.getStartedDescription')}>
          <ul className="flex flex-col gap-2">
            {steps.map((step) => (
              <li key={step.key} className="flex items-center justify-between gap-3">
                <span className="flex items-center gap-2 text-sm">
                  {step.done ? <CircleCheck className="size-4 text-success" aria-hidden /> : <Circle className="size-4 text-muted-foreground" aria-hidden />}
                  {t(`overview.steps.${step.key}`)}
                </span>
                {!step.done && step.action && (
                  <Button size="sm" variant="outline" onClick={step.action}>
                    {t(`overview.stepActions.${step.key}`)}
                  </Button>
                )}
              </li>
            ))}
          </ul>
        </SettingsCard>
      )}

      <section>
        <div className="mb-3 flex items-center justify-between gap-2">
          <h2 className="text-sm font-medium">{t('overview.recentActivity')}</h2>
          <Button asChild variant="ghost" size="sm">
            <Link to={`/clients/${client.id}/activity`}>
              {t('overview.viewAll')} <ArrowRight />
            </Link>
          </Button>
        </div>
        {activity.isError ? (
          <ActivityError error={activity.error} onRetry={() => activity.refetch()} />
        ) : (
          <ClientActivityList items={activity.data?.items.slice(0, 8)} isLoading={activity.isPending} />
        )}
      </section>
    </div>
  )
}

// Τα αρχεία του πελάτη (DOC-01): ανέβασμα, σύνδεση από τη βιβλιοθήκη, αποσύνδεση
export function ClientFilesTab() {
  const { client, openUpload } = useClientContext()
  return <ClientFilesList client={client} onUpload={openUpload} />
}

// Όλο το ιστορικό, 20 τη φορά ("Load older")
export function ClientActivityTab() {
  const { client } = useClientContext()
  const [pages, setPages] = useState(1)

  return (
    <div className="flex flex-col gap-5">
      {Array.from({ length: pages }, (_, index) => (
        <ActivityPage key={index} clientId={client.id} page={index + 1} isLast={index === pages - 1} onMore={() => setPages((p) => p + 1)} />
      ))}
    </div>
  )
}

function ActivityPage({ clientId, page, isLast, onMore }: { clientId: string; page: number; isLast: boolean; onMore: () => void }) {
  const { t } = useTranslation('crm')
  const activity = useClientActivity(clientId, page)
  if (activity.isError) return <ActivityError error={activity.error} onRetry={() => activity.refetch()} />
  // Μετά την πρώτη σελίδα: όχι "κενό" μήνυμα, απλά τίποτα
  if (page > 1 && activity.data?.items.length === 0) return null
  const hasMore = !!activity.data && activity.data.page * activity.data.pageSize < activity.data.total

  return (
    <>
      <ClientActivityList items={activity.data?.items} isLoading={activity.isPending} />
      {isLast && hasMore && (
        <Button variant="outline" size="sm" className="self-start" onClick={onMore}>
          {t('activity.loadOlder')}
        </Button>
      )}
    </>
  )
}

function ActivityError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  const { t } = useTranslation('crm')
  return (
    <Alert variant="error">
      <AlertDescription>{t('activity.loadFailed', { message: problemMessage(error) })}</AlertDescription>
      <AlertAction>
        <Button size="sm" variant="outline" onClick={onRetry}>
          {t('actions.retry', { ns: 'common' })}
        </Button>
      </AlertAction>
    </Alert>
  )
}
