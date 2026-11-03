import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { phoneHref } from '@/lib/phone'
import { Link, Outlet, useParams } from 'react-router'
import { toast } from 'sonner'
import { Archive, ArchiveRestore, ArrowLeft, ChevronDown, Copy, EllipsisVertical, Mail, Pencil, Phone, Plus, SearchX, Upload, UserPlus } from 'lucide-react'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { useDateFormat } from '@/lib/dates'
import { RouteTabs } from '@/components/admin/PageHeader'
import { Alert, AlertAction, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { useMediaQuery } from '@/hooks/use-media-query'
import { ApiError, problemMessage } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import { Permissions } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useArchiveClient, useClient, useClientContacts, type Client, type ClientContact } from '../api'
import { ClientAbout } from '../components/ClientAbout'
import { ClientStatusBadge, KindIcon } from '../components/ClientBadges'
import { ClientFormSheet } from '../components/ClientFormSheet'
import type { ClientOutletContext } from './client-context'
import { ClientUploadDialog } from '../components/ClientFiles'
import { AddContactDialog } from '../components/ContactDialogs'
import { useClientFileAbilities } from '../file-abilities'


// Client 360° (docs/ux/client-360.md): κεφαλίδα, About (επαφές κ.λπ.) και καρτέλες ως routes.
// ≥ 1280px: το About δεξιά (320px). 1024–1279: πάνω από τις καρτέλες, κλειστό/ανοιχτό. Κινητό: accordion + σταθερή μπάρα ενεργειών.
// Οι καρτέλες και τα highlights των επόμενων modules (Projects, Work, Services, Finance, Notes) μπαίνουν όταν υπάρξουν.
export function ClientLayout() {
  const { t } = useTranslation('crm')
  const { id = '' } = useParams()
  const client = useClient(id)
  const contacts = useClientContacts(id)
  const wide = useMediaQuery('(min-width: 1280px)')
  const [aboutOpen, setAboutOpen] = useState(false)
  const [editOpen, setEditOpen] = useState(false)
  const [addContactOpen, setAddContactOpen] = useState(false)
  const [uploadOpen, setUploadOpen] = useState(false)
  const { user } = useSession()

  if (client.isPending) return <ClientSkeleton />
  if (client.isError) {
    return client.error instanceof ApiError && client.error.status === 404 ? (
      <ClientNotFound />
    ) : (
      <Alert variant="error">
        <AlertDescription>{problemMessage(client.error)}</AlertDescription>
        <AlertAction>
          <Button size="sm" variant="outline" onClick={() => client.refetch()}>
            {t('actions.retry', { ns: 'common' })}
          </Button>
        </AlertAction>
      </Alert>
    )
  }

  const c = client.data
  const about = (
    <ClientAbout
      client={c}
      contacts={contacts.data}
      contactsLoading={contacts.isPending}
      onAddContact={() => setAddContactOpen(true)}
    />
  )
  const tabs = [
    { to: `/clients/${c.id}`, label: t('tabs.overview'), end: true },
    // Τα αρχεία κατεβαίνουν μόνο με Media.View: χωρίς αυτό η καρτέλα δεν εμφανίζεται
    ...(hasPermission(user, Permissions.Media.View) ? [{ to: `/clients/${c.id}/files`, label: t('tabs.files') }] : []),
    { to: `/clients/${c.id}/activity`, label: t('tabs.activity') },
  ]

  return (
    <div className="pb-20 md:pb-0">
      <ClientHeader
        client={c}
        contacts={contacts.data}
        onEdit={() => setEditOpen(true)}
        onAddContact={() => setAddContactOpen(true)}
        onUpload={() => setUploadOpen(true)}
      />
      {c.archivedAt && <ArchivedBanner client={c} />}

      <div className={cn(wide && 'grid grid-cols-[minmax(0,1fr)_320px] gap-6')}>
        <div className="min-w-0">
          {!wide && (
            <Card className="mb-4 gap-0 py-0">
              <button
                type="button"
                className="flex w-full items-center justify-between px-4 py-3 text-sm font-medium"
                aria-expanded={aboutOpen}
                onClick={() => setAboutOpen((v) => !v)}
              >
                {t('about.title')}
                <ChevronDown className={cn('size-4 transition-transform', aboutOpen && 'rotate-180')} aria-hidden />
              </button>
              {aboutOpen && <div className="border-t p-4">{about}</div>}
            </Card>
          )}
          <RouteTabs label={t('tabs.label')} tabs={tabs} />
          <div className="pt-5">
            <Outlet
              context={
                {
                  client: c,
                  contacts: contacts.data,
                  openAddContact: () => setAddContactOpen(true),
                  openUpload: () => setUploadOpen(true),
                } satisfies ClientOutletContext
              }
            />
          </div>
        </div>
        {wide && (
          <aside aria-label={t('about.title')} className="border-l pl-6">
            {about}
          </aside>
        )}
      </div>

      <AddContactDialog
        open={addContactOpen}
        onOpenChange={setAddContactOpen}
        clientId={c.id}
        clientName={c.displayName}
        hasPrimary={(contacts.data ?? []).some((contact) => contact.isPrimary)}
      />
      <ClientFormSheet open={editOpen} onOpenChange={setEditOpen} client={c} />
      <ClientUploadDialog client={c} open={uploadOpen} onOpenChange={setUploadOpen} />
    </div>
  )
}

function ClientHeader({
  client,
  contacts,
  onEdit,
  onAddContact,
  onUpload,
}: {
  client: Client
  contacts: ClientContact[] | undefined
  onEdit: () => void
  onAddContact: () => void
  onUpload: () => void
}) {
  const { t } = useTranslation('crm')
  const files = useClientFileAbilities(client)
  const archive = useArchiveClient(client.id)
  const [confirmArchive, setConfirmArchive] = useState(false)
  const primary = contacts?.find((c) => c.isPrimary)
  const primaryEmail = primary?.contactMethods.find((m) => m.kind === 'Email')?.value
  const primaryPhone = primary?.contactMethods.find((m) => m.kind === 'Mobile' || m.kind === 'Phone')?.value

  async function copy(text: string, message: string) {
    try {
      await navigator.clipboard.writeText(text)
      toast.success(message)
    } catch {
      toast.error(t('header.copyFailed'))
    }
  }

  async function unarchive() {
    try {
      await archive.mutateAsync({ archive: false, version: client.version })
      toast.success(t('archive.unarchived', { name: client.displayName }))
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  // Μόνο ό,τι επιτρέπεται στον χρήστη (Project, Task, Ticket, Service, Note έρχονται με τα modules τους)
  const newItems = [
    ...(client.canEdit ? [{ key: 'contact', icon: UserPlus, label: t('header.newContact'), onSelect: onAddContact }] : []),
    ...(files.canUpload ? [{ key: 'file', icon: Upload, label: t('header.uploadFile'), onSelect: onUpload }] : []),
  ]

  const newMenu = newItems.length > 0 && (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button>
          <Plus /> {t('header.new')} <ChevronDown />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {newItems.map((item) => (
          <DropdownMenuItem key={item.key} onSelect={item.onSelect}>
            <item.icon /> {item.label}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )

  return (
    <header className="mb-5" data-testid="client-header">
      <Link to="/clients" className="mb-3 inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="size-4" /> {t('header.back')}
      </Link>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex min-w-0 items-start gap-3">
          <KindIcon kind={client.kind} className="mt-0.5 hidden size-10 shrink-0 items-center justify-center rounded-lg bg-muted text-muted-foreground sm:inline-flex" />
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <h1 className="max-w-full truncate text-2xl font-semibold tracking-tight" title={client.displayName}>
                {client.displayName}
              </h1>
              <span className="font-mono text-sm text-muted-foreground">{client.ref}</span>
              <ClientStatusBadge client={client} />
            </div>
            <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted-foreground">
              {client.legalName && client.legalName !== client.displayName && <span>{client.legalName}</span>}
              {client.taxId && (
                <button
                  type="button"
                  className="inline-flex items-center gap-1 hover:text-foreground"
                  onClick={() => copy(client.taxId!, t('header.taxIdCopied'))}
                  title={t('header.copyTaxId')}
                >
                  {t('header.taxId', { value: client.taxId })} <Copy className="size-3" aria-hidden />
                </button>
              )}
              {client.taxOffice && <span>{t('header.taxOffice', { value: client.taxOffice })}</span>}
              {primary && (
                <span className="inline-flex items-center gap-1.5">
                  <span className="text-foreground">{primary.name}</span>
                  {primary.role && <span>({primary.role})</span>}
                  {primaryPhone && (
                    <a href={phoneHref(primaryPhone)} aria-label={t('header.call', { name: primary.name })} className="hover:text-foreground">
                      <Phone className="size-3.5" />
                    </a>
                  )}
                  {primaryEmail && (
                    <a href={`mailto:${primaryEmail}`} aria-label={t('header.email', { name: primary.name })} className="hover:text-foreground">
                      <Mail className="size-3.5" />
                    </a>
                  )}
                </span>
              )}
            </div>
          </div>
        </div>

        <div className="hidden shrink-0 items-center gap-2 md:flex">
          {client.canEdit && (
            <Button variant="outline" onClick={onEdit}>
              <Pencil /> {t('header.edit')}
            </Button>
          )}
          {newMenu}
          <MoreMenu client={client} onCopyRef={() => copy(client.ref, t('header.refCopied'))} onArchive={() => setConfirmArchive(true)} onUnarchive={unarchive} />
        </div>
        <div className="md:hidden">
          <MoreMenu client={client} onCopyRef={() => copy(client.ref, t('header.refCopied'))} onArchive={() => setConfirmArchive(true)} onUnarchive={unarchive} />
        </div>
      </div>

      {/* Κινητό: οι κύριες ενέργειες σε σταθερή μπάρα στο κάτω μέρος (μεγάλοι στόχοι) */}
      {(client.canEdit || newItems.length > 0) && (
        <div className="fixed inset-x-0 bottom-0 z-20 flex gap-2 border-t bg-background/95 p-3 backdrop-blur md:hidden">
          {newMenu && <div className="flex-1 [&_button]:w-full">{newMenu}</div>}
          {client.canEdit && (
            <Button variant="outline" className="flex-1" onClick={onEdit}>
              <Pencil /> {t('header.edit')}
            </Button>
          )}
        </div>
      )}

      <ConfirmDialog
        open={confirmArchive}
        onOpenChange={setConfirmArchive}
        title={t('archive.confirmTitle', { name: client.displayName })}
        description={t('archive.confirmDescription')}
        confirmLabel={t('archive.archive')}
        onConfirm={async () => {
          try {
            await archive.mutateAsync({ archive: true, version: client.version })
            toast.success(t('archive.archived', { name: client.displayName }))
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />
    </header>
  )
}

function MoreMenu({ client, onCopyRef, onArchive, onUnarchive }: { client: Client; onCopyRef: () => void; onArchive: () => void; onUnarchive: () => void }) {
  const { t } = useTranslation('crm')
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="icon" aria-label={t('header.more')}>
          <EllipsisVertical />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem onSelect={onCopyRef}>
          <Copy /> {t('header.copyRef')}
        </DropdownMenuItem>
        {client.canArchive && (
          <>
            <DropdownMenuSeparator />
            {client.archivedAt ? (
              <DropdownMenuItem onSelect={onUnarchive}>
                <ArchiveRestore /> {t('archive.unarchive')}
              </DropdownMenuItem>
            ) : (
              <DropdownMenuItem onSelect={onArchive}>
                <Archive /> {t('archive.archive')}
              </DropdownMenuItem>
            )}
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function ArchivedBanner({ client }: { client: Client }) {
  const { t } = useTranslation('crm')
  const format = useDateFormat()
  const archive = useArchiveClient(client.id)
  return (
    <Alert variant="warning" className="mb-5" data-testid="archived-banner">
      <Archive />
      <AlertDescription>{t('archive.banner', { date: format.date(client.archivedAt!) })}</AlertDescription>
      {client.canArchive && (
        <AlertAction>
          <Button
            size="sm"
            variant="outline"
            disabled={archive.isPending}
            onClick={async () => {
              try {
                await archive.mutateAsync({ archive: false, version: client.version })
                toast.success(t('archive.unarchived', { name: client.displayName }))
              } catch (error) {
                toast.error(problemMessage(error))
              }
            }}
          >
            <ArchiveRestore /> {t('archive.unarchive')}
          </Button>
        </AlertAction>
      )}
    </Alert>
  )
}

function ClientSkeleton() {
  return (
    <div aria-busy>
      <Skeleton className="mb-4 h-4 w-20" />
      <Skeleton className="h-8 w-72" />
      <Skeleton className="mt-2 h-4 w-96 max-w-full" />
      <Skeleton className="mt-8 h-9 w-64" />
      <Skeleton className="mt-6 h-40 w-full" />
    </div>
  )
}

// Δεν υπάρχει ή δεν επιτρέπεται: ίδιο μήνυμα (χωρίς διαρροή)
function ClientNotFound() {
  const { t } = useTranslation('crm')
  return (
    <Empty className="py-20">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <SearchX />
        </EmptyMedia>
        <EmptyTitle>{t('notFound.title')}</EmptyTitle>
        <EmptyDescription>{t('notFound.description')}</EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Button asChild variant="outline">
          <Link to="/clients">
            <ArrowLeft /> {t('header.back')}
          </Link>
        </Button>
      </EmptyContent>
    </Empty>
  )
}
