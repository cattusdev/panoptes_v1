import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ChevronRight, CircleAlert, RotateCcw, ServerCog } from 'lucide-react'
import { SearchInput, TableEmpty } from '@/components/admin/Common'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { ColumnsMenu, DataTable, TablePagination, type Column } from '@/components/admin/DataTable'
import { DateTime } from '@/components/admin/DateTime'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { PageHeader } from '@/components/admin/PageHeader'
import { StatusBadge } from '@/components/admin/StatusBadge'
import { Alert, AlertAction, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { useColumnVisibility } from '@/hooks/use-column-visibility'
import { useUrlFilters } from '@/hooks/use-url-filters'
import { jobStatuses, useJob, useJobAction, useJobSummary, useJobs, useRetryJobs, type JobItem } from '@/lib/admin-api'
import { problemMessage } from '@/lib/api'
import { jobStatusTone } from '@/lib/status'

const retryable = (job: JobItem) => job.status === 'Failed' || job.status === 'Cancelled'

// Οι δουλειές του παρασκηνίου (emails, αργότερα υπενθυμίσεις και ανανεώσεις). Όπως τα "webhook deliveries" του Stripe
// και τα runs του GitHub Actions: οι αποτυχίες φαίνονται αμέσως (ειδοποίηση πάνω από τη λίστα), η λεπτομέρεια δείχνει
// το λάθος και το payload, και η επανάληψη γίνεται για μία ή για πολλές μαζί (π.χ. μετά από διακοπή του SMTP).
export function JobsPage() {
  const { t } = useTranslation('admin')
  const list = useUrlFilters(['search', 'status', 'type'], { defaultSort: '-created' })
  const { search, status, type } = list.filters
  const columnVisibility = useColumnVisibility('jobs')
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const jobs = useJobs({ search, status, type, sort: list.sort, page: list.page, pageSize: list.pageSize })
  const summary = useJobSummary()
  const retryMany = useRetryJobs()
  const failed = summary.data?.counts.Failed ?? 0

  const statusOptions = jobStatuses.map((value) => ({
    value,
    label: summary.data ? t('jobs.statusCount', { status: t(`jobs.status.${value}`), count: summary.data.counts[value] }) : t(`jobs.status.${value}`),
  }))
  const typeOptions = (summary.data?.types ?? []).map((value) => ({ value, label: value }))

  async function retrySelected(ids: string[], clear: () => void) {
    try {
      const result = await retryMany.mutateAsync(ids)
      toast.success(t('jobs.retriedMany', { count: result.retried }), { description: t('jobs.retriedDescription') })
      clear()
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  const columns: Column<JobItem>[] = [
    {
      key: 'type',
      header: t('jobs.columns.job'),
      cell: (j) => (
        <div className="min-w-0">
          <div className="font-mono text-xs font-medium">{j.type}</div>
          <div className="max-w-[320px] truncate font-mono text-xs text-muted-foreground">{j.idempotencyKey}</div>
        </div>
      ),
    },
    {
      key: 'status',
      header: t('jobs.columns.status'),
      label: t('jobs.columns.status'),
      cell: (j) => <StatusBadge tone={jobStatusTone(j.status)}>{t(`jobs.status.${j.status}`)}</StatusBadge>,
    },
    {
      key: 'attempts',
      header: t('jobs.columns.attempts'),
      label: t('jobs.columns.attempts'),
      className: 'tabular-nums',
      cell: (j) => t('jobs.attemptsOf', { attempts: j.attempts, max: j.maxAttempts }),
    },
    {
      key: 'error',
      header: t('jobs.columns.lastError'),
      label: t('jobs.columns.lastError'),
      cell: (j) =>
        j.lastError ? (
          <span className="block max-w-[280px] truncate text-xs text-destructive" title={j.lastError}>
            {j.lastError}
          </span>
        ) : (
          <span className="text-muted-foreground">—</span>
        ),
    },
    {
      key: 'runAt',
      header: t('jobs.columns.runAt'),
      label: t('jobs.columns.runAt'),
      sortable: true,
      cell: (j) => <DateTime value={j.runAt} mode="relative" className="whitespace-nowrap text-muted-foreground" />,
    },
    {
      key: 'created',
      header: t('jobs.columns.created'),
      label: t('jobs.columns.created'),
      sortable: true,
      cell: (j) => <DateTime value={j.createdAt} mode="relative" className="whitespace-nowrap text-muted-foreground" />,
    },
    {
      key: 'open',
      header: <span className="sr-only">{t('jobs.columns.details')}</span>,
      className: 'w-10 text-right',
      cell: () => <ChevronRight className="size-4 text-muted-foreground" />,
    },
  ]

  return (
    <>
      <PageHeader title={t('jobs.title')} description={t('jobs.description')} />

      {failed > 0 && status !== 'Failed' && (
        <Alert variant="error" className="mb-4">
          <CircleAlert />
          <AlertTitle>{t('jobs.failedAlert.title', { count: failed })}</AlertTitle>
          <AlertDescription>{t('jobs.failedAlert.text')}</AlertDescription>
          <AlertAction>
            <Button size="sm" variant="outline" onClick={() => list.set('status', 'Failed')}>
              {t('jobs.failedAlert.show')}
            </Button>
          </AlertAction>
        </Alert>
      )}

      <div className="mb-3 flex flex-col gap-2 sm:flex-row">
        <SearchInput value={list.searchInput} onChange={list.setSearchInput} placeholder={t('jobs.search')} />
        <OptionSelect
          ariaLabel={t('jobs.filterByStatus')}
          value={status}
          onChange={(v) => list.set('status', v)}
          emptyLabel={t('jobs.allStatuses')}
          options={statusOptions}
          className="sm:ml-auto sm:w-44"
        />
        <OptionSelect
          ariaLabel={t('jobs.filterByType')}
          value={type}
          onChange={(v) => list.set('type', v)}
          emptyLabel={t('jobs.allTypes')}
          options={typeOptions}
          className="sm:w-44"
        />
        <ColumnsMenu columns={columns} visibility={columnVisibility.visibility} onToggle={columnVisibility.toggle} />
      </div>

      <DataTable
        columns={columns}
        rows={jobs.data?.items}
        getRowId={(j) => j.id}
        rowLabel={(j) => t('jobs.rowLabel', { type: j.type, key: j.idempotencyKey })}
        isLoading={jobs.isPending}
        onRowClick={(j) => setSelectedId(j.id)}
        sort={list.sort}
        onSortChange={list.setSort}
        columnVisibility={columnVisibility.visibility}
        bulkActions={(ids, clear) => {
          const rows = (jobs.data?.items ?? []).filter((j) => ids.includes(j.id))
          const canRetry = rows.length > 0 && rows.every(retryable)
          return (
            <>
              {!canRetry && <span className="text-xs text-muted-foreground">{t('jobs.retryOnlyFailed')}</span>}
              <Button size="sm" disabled={!canRetry || retryMany.isPending} onClick={() => void retrySelected(ids, clear)}>
                {retryMany.isPending ? <Spinner /> : <RotateCcw />}
                {t('jobs.retrySelected')}
              </Button>
            </>
          )
        }}
        empty={
          <TableEmpty
            icon={ServerCog}
            title={list.isFiltered ? t('jobs.empty.filteredTitle') : t('jobs.empty.title')}
            description={list.isFiltered ? t('jobs.empty.filteredDescription') : t('jobs.empty.description')}
          />
        }
      />

      {jobs.data && (
        <TablePagination
          page={jobs.data.page}
          pageSize={jobs.data.pageSize}
          total={jobs.data.total}
          onPageChange={(p) => list.set('page', String(p))}
          onPageSizeChange={(size) => list.set('pageSize', String(size))}
        />
      )}

      <Sheet open={!!selectedId} onOpenChange={(open) => !open && setSelectedId(null)}>
        <SheetContent className="w-full gap-0 data-[side=right]:sm:max-w-xl">{selectedId && <JobDetail id={selectedId} />}</SheetContent>
      </Sheet>
    </>
  )
}

function JobDetail({ id }: { id: string }) {
  const { t } = useTranslation('admin')
  const job = useJob(id)
  const action = useJobAction()
  const [confirmCancel, setConfirmCancel] = useState(false)

  async function retry() {
    try {
      await action.mutateAsync({ id, action: 'retry' })
      toast.success(t('jobs.retried'), { description: t('jobs.retriedDescription') })
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  if (job.isError) {
    return (
      <SheetHeader>
        <SheetTitle>{t('jobs.detail.title')}</SheetTitle>
        <SheetDescription className="text-destructive">{problemMessage(job.error)}</SheetDescription>
      </SheetHeader>
    )
  }
  if (!job.data) {
    return (
      <SheetHeader>
        <SheetTitle className="sr-only">{t('jobs.detail.title')}</SheetTitle>
        <Skeleton className="h-40" />
      </SheetHeader>
    )
  }

  const j = job.data
  return (
    <>
      <SheetHeader className="border-b">
        <div className="flex items-center gap-2">
          <StatusBadge tone={jobStatusTone(j.status)}>{t(`jobs.status.${j.status}`)}</StatusBadge>
          <SheetTitle className="font-mono text-sm">{j.type}</SheetTitle>
        </div>
        <SheetDescription className="font-mono text-xs break-all">{j.idempotencyKey}</SheetDescription>
      </SheetHeader>

      <div className="grid gap-5 overflow-y-auto p-4">
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
          <Detail label={t('jobs.detail.attempts')}>{t('jobs.attemptsOf', { attempts: j.attempts, max: j.maxAttempts })}</Detail>
          <Detail label={t('jobs.detail.runAt')}>
            <DateTime value={j.runAt} />
          </Detail>
          <Detail label={t('jobs.detail.created')}>
            <DateTime value={j.createdAt} />
          </Detail>
          <Detail label={t('jobs.detail.completed')}>{j.completedAt ? <DateTime value={j.completedAt} /> : '—'}</Detail>
          <Detail label={t('jobs.detail.id')}>
            <span className="font-mono text-xs break-all">{j.id}</span>
          </Detail>
        </dl>

        {j.lastError && (
          <section>
            <h3 className="mb-2 text-xs font-semibold tracking-[0.08em] text-muted-foreground uppercase">{t('jobs.detail.lastError')}</h3>
            <pre data-testid="job-error" className="rounded-lg border border-destructive/30 bg-destructive/5 p-3 font-mono text-xs whitespace-pre-wrap text-destructive">
              {j.lastError}
            </pre>
          </section>
        )}

        <section>
          <h3 className="mb-2 text-xs font-semibold tracking-[0.08em] text-muted-foreground uppercase">{t('jobs.detail.payload')}</h3>
          <pre data-testid="job-payload" className="max-h-80 overflow-auto rounded-lg border bg-muted/30 p-3 font-mono text-xs whitespace-pre-wrap break-all">
            {prettyJson(j.payload ?? '')}
          </pre>
        </section>
      </div>

      {(retryable(j) || j.status === 'Pending') && (
        <SheetFooter className="border-t sm:flex-row sm:justify-end">
          {j.status === 'Pending' && (
            <Button variant="outline" onClick={() => setConfirmCancel(true)}>
              {t('jobs.cancel')}
            </Button>
          )}
          {retryable(j) && (
            <Button onClick={() => void retry()} disabled={action.isPending}>
              {action.isPending ? <Spinner /> : <RotateCcw />}
              {t('jobs.retry')}
            </Button>
          )}
        </SheetFooter>
      )}

      <ConfirmDialog
        open={confirmCancel}
        onOpenChange={setConfirmCancel}
        title={t('jobs.cancelDialog.title')}
        description={t('jobs.cancelDialog.description')}
        confirmLabel={t('jobs.cancelDialog.confirm')}
        destructive
        onConfirm={async () => {
          try {
            await action.mutateAsync({ id, action: 'cancel' })
            toast.success(t('jobs.cancelled'))
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />
    </>
  )
}

function Detail({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="min-w-0">{children}</dd>
    </>
  )
}

// Το payload είναι JSON: με εσοχές για να διαβάζεται (ό,τι δεν είναι JSON εμφανίζεται όπως είναι)
function prettyJson(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}
