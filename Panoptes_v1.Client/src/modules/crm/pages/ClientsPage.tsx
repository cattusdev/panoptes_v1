import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { Building2, Plus } from 'lucide-react'
import { SearchInput, TableEmpty } from '@/components/admin/Common'
import { ColumnsMenu, DataTable, TablePagination, type Column } from '@/components/admin/DataTable'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { PageHeader } from '@/components/admin/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useColumnVisibility } from '@/hooks/use-column-visibility'
import { useUrlFilters } from '@/hooks/use-url-filters'
import { hasPermission, useSession } from '@/lib/auth'
import { Permissions } from '@/lib/permissions'
import { useClients, useClientTags, type ClientListItem } from '../api'
import { ClientStatusBadge, KindIcon } from '../components/ClientBadges'
import { ClientFormSheet } from '../components/ClientFormSheet'

const statusFilters = ['Active', 'Inactive', 'Archived', 'All'] as const

// Οι πελάτες: αναζήτηση (όνομα, επωνυμία, κωδικός, ΑΦΜ), φίλτρα στο URL. Οι αρχειοθετημένοι κρύβονται εκτός αν ζητηθούν.
export function ClientsPage() {
  const { t } = useTranslation('crm')
  const navigate = useNavigate()
  const { user } = useSession()
  const list = useUrlFilters(['search', 'status', 'kind', 'tag'], { defaultSort: 'name' })
  const { search, status, kind, tag } = list.filters
  const columnVisibility = useColumnVisibility('clients')
  const [createOpen, setCreateOpen] = useState(false)

  const clients = useClients({ search, status, kind, tag, sort: list.sort, page: list.page, pageSize: list.pageSize })
  const tags = useClientTags()
  const canCreate = hasPermission(user, Permissions.Clients.Create)

  const columns: Column<ClientListItem>[] = [
    {
      key: 'name',
      header: t('list.columns.client'),
      sortable: true,
      cell: (c) => (
        <div className="flex min-w-0 items-center gap-3">
          <KindIcon kind={c.kind} />
          <div className="min-w-0">
            <div className="truncate font-medium" title={c.displayName}>
              {c.displayName}
            </div>
            {c.legalName && c.legalName !== c.displayName && (
              <div className="truncate text-xs text-muted-foreground" title={c.legalName}>
                {c.legalName}
              </div>
            )}
          </div>
        </div>
      ),
    },
    {
      key: 'ref',
      header: t('list.columns.ref'),
      label: t('list.columns.ref'),
      sortable: true,
      className: 'w-28',
      cell: (c) => <span className="font-mono text-xs text-muted-foreground">{c.ref}</span>,
    },
    {
      key: 'contact',
      header: t('list.columns.primaryContact'),
      label: t('list.columns.primaryContact'),
      cell: (c) =>
        c.primaryContact ? (
          <div className="min-w-0 text-sm">
            <div className="truncate">
              {c.primaryContact.name}
              {c.primaryContact.role && <span className="text-muted-foreground"> · {c.primaryContact.role}</span>}
            </div>
            <div className="truncate text-xs text-muted-foreground">{c.primaryContact.email ?? c.primaryContact.phone}</div>
          </div>
        ) : (
          <span className="text-sm text-muted-foreground">—</span>
        ),
    },
    {
      key: 'tags',
      header: t('list.columns.tags'),
      label: t('list.columns.tags'),
      cell: (c) => (
        <div className="flex flex-wrap gap-1">
          {c.tags.slice(0, 3).map((tagName) => (
            <Badge key={tagName} variant="muted">
              {tagName}
            </Badge>
          ))}
          {c.tags.length > 3 && <Badge variant="muted">+{c.tags.length - 3}</Badge>}
        </div>
      ),
    },
    {
      key: 'status',
      header: t('list.columns.status'),
      label: t('list.columns.status'),
      className: 'w-32',
      cell: (c) => <ClientStatusBadge client={c} />,
    },
  ]

  return (
    <>
      <PageHeader
        title={t('list.title')}
        description={t('list.description')}
        actions={
          canCreate && (
            <Button onClick={() => setCreateOpen(true)}>
              <Plus /> {t('list.newClient')}
            </Button>
          )
        }
      />

      <div className="mb-3 flex flex-col gap-2 sm:flex-row">
        <SearchInput value={list.searchInput} onChange={list.setSearchInput} placeholder={t('list.search')} />
        <div className="flex flex-wrap gap-2 sm:ml-auto">
          <OptionSelect
            ariaLabel={t('list.filterByStatus')}
            value={status}
            onChange={(v) => list.set('status', v)}
            emptyLabel={t('list.statusCurrent')}
            options={statusFilters.map((value) => ({ value, label: t(`list.statusFilter.${value}`) }))}
            className="sm:w-40"
          />
          <OptionSelect
            ariaLabel={t('list.filterByKind')}
            value={kind}
            onChange={(v) => list.set('kind', v)}
            emptyLabel={t('list.allKinds')}
            options={(['Organization', 'Individual'] as const).map((value) => ({ value, label: t(`kinds.${value}`) }))}
            className="sm:w-40"
          />
          {(tags.data?.length ?? 0) > 0 && (
            <OptionSelect
              ariaLabel={t('list.filterByTag')}
              value={tag}
              onChange={(v) => list.set('tag', v)}
              emptyLabel={t('list.allTags')}
              options={(tags.data ?? []).map((value) => ({ value, label: value }))}
              className="sm:w-40"
            />
          )}
          <ColumnsMenu columns={columns} visibility={columnVisibility.visibility} onToggle={columnVisibility.toggle} />
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={clients.data?.items}
        getRowId={(c) => c.id}
        rowLabel={(c) => c.displayName}
        isLoading={clients.isPending}
        onRowClick={(c) => navigate(`/clients/${c.id}`)}
        sort={list.sort}
        onSortChange={list.setSort}
        columnVisibility={columnVisibility.visibility}
        empty={
          <TableEmpty
            icon={Building2}
            title={list.isFiltered ? t('list.empty.filteredTitle') : t('list.empty.title')}
            description={list.isFiltered ? t('list.empty.filteredDescription') : t('list.empty.description')}
          />
        }
      />

      {clients.data && (
        <TablePagination
          page={clients.data.page}
          pageSize={clients.data.pageSize}
          total={clients.data.total}
          onPageChange={(p) => list.set('page', String(p))}
          onPageSizeChange={(size) => list.set('pageSize', String(size))}
        />
      )}

      <ClientFormSheet open={createOpen} onOpenChange={setCreateOpen} />
    </>
  )
}
