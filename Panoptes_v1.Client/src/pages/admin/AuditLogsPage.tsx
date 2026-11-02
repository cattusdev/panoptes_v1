import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronRight, NotebookText } from 'lucide-react'
import { SearchInput, TableEmpty } from '@/components/admin/Common'
import { ColumnsMenu, DataTable, TablePagination, type Column } from '@/components/admin/DataTable'
import { DateTime } from '@/components/admin/DateTime'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { PageHeader } from '@/components/admin/PageHeader'
import { StatusBadge } from '@/components/admin/StatusBadge'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useColumnVisibility } from '@/hooks/use-column-visibility'
import { useUrlFilters } from '@/hooks/use-url-filters'
import { useAuditLogs, useAuditTypeLabel, type AuditLogItem } from '@/lib/admin-api'
import { auditTypeTone } from '@/lib/status'

const filterTypes = ['Create', 'Update', 'Delete'] as const

export function AuditLogsPage() {
  const { t } = useTranslation('admin')
  const typeLabel = useAuditTypeLabel()
  const list = useUrlFilters(['search', 'type'], { defaultPageSize: 20, defaultSort: '-time' })
  const { search, type } = list.filters
  const columnVisibility = useColumnVisibility('audit-logs')
  const [selected, setSelected] = useState<AuditLogItem | null>(null)

  const logs = useAuditLogs({ search, type, sort: list.sort, page: list.page, pageSize: list.pageSize })
  const filtered = list.isFiltered
  const typeOptions = filterTypes.map((value) => ({ value, label: t(`audit.filter.${value}`) }))

  const columns: Column<AuditLogItem>[] = [
    {
      key: 'time',
      header: t('audit.columns.time'),
      sortable: true,
      cell: (l) => (
        <DateTime value={l.dateTime} mode="relative" className="whitespace-nowrap text-muted-foreground" />
      ),
    },
    {
      key: 'user',
      header: t('audit.columns.user'),
      label: t('audit.columns.user'),
      cell: (l) => (
        <div className="min-w-0">
          <div className="font-medium">{l.userEmail ?? t('shared.system')}</div>
          {!l.userEmail && l.source && <div className="font-mono text-xs text-muted-foreground">{l.source}</div>}
        </div>
      ),
    },
    {
      key: 'action',
      header: t('audit.columns.action'),
      label: t('audit.columns.action'),
      cell: (l) => <StatusBadge tone={auditTypeTone(l.type)}>{typeLabel(l.type)}</StatusBadge>,
    },
    {
      key: 'table',
      header: t('audit.columns.resource'),
      label: t('audit.columns.resource'),
      sortable: true,
      cell: (l) => (
        <div className="min-w-0">
          <div className="font-mono text-xs">{l.tableName}</div>
          {l.recordId && <div className="max-w-[260px] truncate font-mono text-xs text-muted-foreground">{l.recordId}</div>}
        </div>
      ),
    },
    {
      key: 'open',
      header: <span className="sr-only">{t('audit.columns.details')}</span>,
      className: 'w-10 text-right',
      cell: () => <ChevronRight className="size-4 text-muted-foreground" />,
    },
  ]

  return (
    <>
      <PageHeader title={t('audit.title')} description={t('audit.description')} />

      <div className="mb-3 flex flex-col gap-2 sm:flex-row">
        <SearchInput value={list.searchInput} onChange={list.setSearchInput} placeholder={t('audit.search')} />
        <OptionSelect
          ariaLabel={t('audit.filterByAction')}
          value={type}
          onChange={(v) => list.set('type', v)}
          emptyLabel={t('audit.allActions')}
          options={typeOptions}
          className="sm:ml-auto sm:w-40"
        />
        <ColumnsMenu columns={columns} visibility={columnVisibility.visibility} onToggle={columnVisibility.toggle} />
      </div>

      <DataTable
        columns={columns}
        rows={logs.data?.items}
        getRowId={(l) => l.id}
        rowLabel={(l) => t('audit.rowLabel', { type: typeLabel(l.type), table: l.tableName ?? '' })}
        isLoading={logs.isPending}
        onRowClick={setSelected}
        sort={list.sort}
        onSortChange={list.setSort}
        columnVisibility={columnVisibility.visibility}
        empty={
          <TableEmpty
            icon={NotebookText}
            title={filtered ? t('audit.empty.filteredTitle') : t('audit.empty.title')}
            description={filtered ? t('audit.empty.filteredDescription') : t('audit.empty.description')}
          />
        }
      />

      {logs.data && (
        <TablePagination
          page={logs.data.page}
          pageSize={logs.data.pageSize}
          total={logs.data.total}
          onPageChange={(p) => list.set('page', String(p))}
          onPageSizeChange={(size) => list.set('pageSize', String(size))}
        />
      )}

      <Sheet open={!!selected} onOpenChange={(open) => !open && setSelected(null)}>
        <SheetContent className="w-full gap-0 sm:max-w-xl">
          {selected && <AuditDetail log={selected} />}
        </SheetContent>
      </Sheet>
    </>
  )
}

function AuditDetail({ log }: { log: AuditLogItem }) {
  const { t } = useTranslation('admin')
  const typeLabel = useAuditTypeLabel()
  const showOld = log.type !== 'Create'
  const showNew = log.type !== 'Delete'

  return (
    <>
      <SheetHeader className="border-b">
        <div className="flex items-center gap-2">
          <StatusBadge tone={auditTypeTone(log.type)}>{typeLabel(log.type)}</StatusBadge>
          <span className="font-mono text-sm">{log.tableName}</span>
        </div>
        <SheetTitle className="sr-only">{t('audit.detail.title')}</SheetTitle>
        <SheetDescription>
          {log.userEmail ?? (log.source ? `${t('shared.system')} (${log.source})` : t('shared.system'))} · <DateTime value={log.dateTime} />
        </SheetDescription>
      </SheetHeader>

      <div className="overflow-y-auto p-4">
        {log.recordId && (
          <div className="mb-4 text-sm">
            <div className="text-xs text-muted-foreground">{t('audit.detail.record')}</div>
            <div className="font-mono text-xs break-all">{log.recordId}</div>
          </div>
        )}
        {log.traceId && (
          <div className="mb-4 text-sm">
            <div className="text-xs text-muted-foreground">{t('audit.detail.reference')}</div>
            <div className="font-mono text-xs break-all">{log.traceId}</div>
          </div>
        )}

        <div className="mb-2 text-xs font-semibold tracking-[0.08em] text-muted-foreground uppercase">{t('audit.detail.changes')}</div>
        {log.changes.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('audit.detail.noChanges')}</p>
        ) : (
          <div className="overflow-hidden rounded-lg border">
            <Table className="table-fixed">
              <TableHeader>
                <TableRow className="hover:bg-transparent">
                  <TableHead className="w-1/4">{t('audit.detail.field')}</TableHead>
                  {showOld && <TableHead>{t('audit.detail.before')}</TableHead>}
                  {showNew && <TableHead>{t('audit.detail.after')}</TableHead>}
                </TableRow>
              </TableHeader>
              <TableBody>
                {log.changes.map((change) => (
                  <TableRow key={change.field} className="hover:bg-transparent">
                    <TableCell className="align-top text-xs font-medium whitespace-normal">{change.field}</TableCell>
                    {showOld && <ValueCell value={change.oldValue} tone="old" />}
                    {showNew && <ValueCell value={change.newValue} tone="new" />}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </div>
    </>
  )
}

function ValueCell({ value, tone }: { value: string | null; tone: 'old' | 'new' }) {
  return (
    <TableCell className="align-top font-mono text-xs break-all whitespace-normal">
      {value === null ? (
        <span className="text-muted-foreground">—</span>
      ) : (
        <span className={tone === 'old' ? 'text-destructive/90' : 'text-success'}>{value}</span>
      )}
    </TableCell>
  )
}
