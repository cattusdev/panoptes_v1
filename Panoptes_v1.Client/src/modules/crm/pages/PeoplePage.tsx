import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'
import { Contact, Pencil, Plus, Star } from 'lucide-react'
import { SearchInput, TableEmpty } from '@/components/admin/Common'
import { ColumnsMenu, DataTable, TablePagination, type Column } from '@/components/admin/DataTable'
import { PageHeader } from '@/components/admin/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { useColumnVisibility } from '@/hooks/use-column-visibility'
import { useUrlFilters } from '@/hooks/use-url-filters'
import { problemMessage } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import { Permissions } from '@/lib/permissions'
import { usePeople, usePerson, type PersonListItem } from '../api'
import { ContactMethodLinks } from '../components/ContactMethodLinks'
import { PersonFormDialog } from '../components/PersonFormDialog'

// Οι επαφές (πρόσωπα) και οι πελάτες τους. Ένα πρόσωπο ανοίγει σε πλαϊνό panel (?person=id, μοιράζεται ως link).
export function PeoplePage() {
  const { t } = useTranslation('crm')
  const { user } = useSession()
  const list = useUrlFilters(['search'], { defaultSort: 'name' })
  const columnVisibility = useColumnVisibility('people')
  const [params, setParams] = useSearchParams()
  const selectedId = params.get('person')
  const [createOpen, setCreateOpen] = useState(false)

  const people = usePeople({ search: list.filters.search, sort: list.sort, page: list.page, pageSize: list.pageSize })
  const canEdit = hasPermission(user, Permissions.Clients.Edit)

  function select(id: string | null) {
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (id) next.set('person', id)
        else next.delete('person')
        return next
      },
      { replace: true },
    )
  }

  const columns: Column<PersonListItem>[] = [
    { key: 'name', header: t('people.columns.name'), sortable: true, cell: (p) => <span className="font-medium">{p.name}</span> },
    {
      key: 'email',
      header: t('people.columns.email'),
      label: t('people.columns.email'),
      cell: (p) => <span className="text-sm text-muted-foreground">{p.email ?? '—'}</span>,
    },
    {
      key: 'phone',
      header: t('people.columns.phone'),
      label: t('people.columns.phone'),
      cell: (p) => <span className="text-sm text-muted-foreground">{p.phone ?? '—'}</span>,
    },
    {
      key: 'clients',
      header: t('people.columns.clients'),
      label: t('people.columns.clients'),
      cell: (p) => (
        <div className="flex flex-wrap gap-1">
          {p.clients.map((c) => (
            <Badge key={c.id} variant="muted">
              {c.displayName}
            </Badge>
          ))}
        </div>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title={t('people.title')}
        description={t('people.description')}
        actions={
          canEdit && (
            <Button onClick={() => setCreateOpen(true)}>
              <Plus /> {t('people.newPerson')}
            </Button>
          )
        }
      />

      <div className="mb-3 flex flex-col gap-2 sm:flex-row">
        <SearchInput value={list.searchInput} onChange={list.setSearchInput} placeholder={t('people.search')} />
        <div className="flex gap-2 sm:ml-auto">
          <ColumnsMenu columns={columns} visibility={columnVisibility.visibility} onToggle={columnVisibility.toggle} />
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={people.data?.items}
        getRowId={(p) => p.id}
        rowLabel={(p) => p.name}
        isLoading={people.isPending}
        onRowClick={(p) => select(p.id)}
        sort={list.sort}
        onSortChange={list.setSort}
        columnVisibility={columnVisibility.visibility}
        empty={
          <TableEmpty
            icon={Contact}
            title={list.isFiltered ? t('people.empty.filteredTitle') : t('people.empty.title')}
            description={list.isFiltered ? t('people.empty.filteredDescription') : t('people.empty.description')}
          />
        }
      />

      {people.data && (
        <TablePagination
          page={people.data.page}
          pageSize={people.data.pageSize}
          total={people.data.total}
          onPageChange={(p) => list.set('page', String(p))}
          onPageSizeChange={(size) => list.set('pageSize', String(size))}
        />
      )}

      <Sheet open={!!selectedId} onOpenChange={(open) => !open && select(null)}>
        <SheetContent className="w-full gap-0 sm:max-w-md">{selectedId && <PersonDetail id={selectedId} />}</SheetContent>
      </Sheet>

      <PersonFormDialog open={createOpen} onOpenChange={setCreateOpen} onSaved={(person) => select(person.id)} />
    </>
  )
}

function PersonDetail({ id }: { id: string }) {
  const { t } = useTranslation('crm')
  const person = usePerson(id)
  const [editOpen, setEditOpen] = useState(false)

  if (person.isPending) {
    return (
      <SheetHeader>
        <SheetTitle className="sr-only">{t('people.detail.title')}</SheetTitle>
        <Skeleton className="h-6 w-40" />
        <Skeleton className="mt-2 h-4 w-56" />
      </SheetHeader>
    )
  }
  if (person.isError) {
    return (
      <SheetHeader>
        <SheetTitle>{t('people.detail.title')}</SheetTitle>
        <SheetDescription className="text-destructive">{problemMessage(person.error)}</SheetDescription>
      </SheetHeader>
    )
  }

  const p = person.data
  return (
    <>
      <SheetHeader className="border-b">
        <SheetTitle>{p.name}</SheetTitle>
        <SheetDescription>{t('people.detail.clientCount', { count: p.clients.length })}</SheetDescription>
      </SheetHeader>
      <div className="flex flex-1 flex-col gap-6 overflow-y-auto p-4">
        {p.contactMethods.length > 0 ? (
          <ContactMethodLinks methods={p.contactMethods} />
        ) : (
          <p className="text-sm text-muted-foreground">{t('people.detail.noContactMethods')}</p>
        )}

        <section>
          <h3 className="mb-2 text-sm font-medium">{t('people.detail.clients')}</h3>
          {p.clients.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('people.detail.noClients')}</p>
          ) : (
            <ul className="flex flex-col divide-y rounded-lg border">
              {p.clients.map((c) => (
                <li key={c.contactId} className="flex items-center justify-between gap-3 px-3 py-2.5">
                  <div className="min-w-0">
                    <Link to={`/clients/${c.clientId}`} className="font-medium hover:underline">
                      {c.clientName}
                    </Link>
                    <div className="text-xs text-muted-foreground">
                      {[c.clientRef, c.role].filter(Boolean).join(' · ')}
                    </div>
                  </div>
                  <div className="flex shrink-0 gap-1">
                    {c.isPrimary && (
                      <Badge variant="info">
                        <Star data-icon="inline-start" aria-hidden /> {t('contacts.primary')}
                      </Badge>
                    )}
                    {c.isBillingContact && <Badge variant="muted">{t('contacts.billing')}</Badge>}
                    {c.clientArchived && <Badge variant="muted">{t('status.archived')}</Badge>}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>

        {p.notes && (
          <section>
            <h3 className="mb-2 text-sm font-medium">{t('form.notes')}</h3>
            <p className="text-sm whitespace-pre-line text-muted-foreground">{p.notes}</p>
          </section>
        )}
      </div>
      {p.canEdit && (
        <SheetFooter className="border-t sm:flex-row sm:justify-end">
          <Button variant="outline" onClick={() => setEditOpen(true)}>
            <Pencil /> {t('people.detail.edit')}
          </Button>
        </SheetFooter>
      )}
      <PersonFormDialog open={editOpen} onOpenChange={setEditOpen} person={p} />
    </>
  )
}
