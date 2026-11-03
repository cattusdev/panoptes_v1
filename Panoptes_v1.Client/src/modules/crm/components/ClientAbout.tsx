import { useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { ArrowRight, EllipsisVertical, Pencil, Plus, Star, Trash2, UserCog } from 'lucide-react'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { DateTime } from '@/components/admin/DateTime'
import { StatusBadge } from '@/components/admin/StatusBadge'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'
import { problemMessage } from '@/lib/api'
import { usePerson, useRemoveContact, useUpdateContact, type Client, type ClientContact } from '../api'
import { ContactMethodLinks } from './ContactMethodLinks'
import { EditContactDialog } from './ContactDialogs'
import { PersonFormDialog } from './PersonFormDialog'

const visibleContacts = 5

function Section({ title, action, children }: { title: string; action?: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-2">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</h2>
        {action}
      </div>
      {children}
    </section>
  )
}

// Το "About" του Client 360°: επαφές (με το "Also contact for"), τρόποι επικοινωνίας, διευθύνσεις, ετικέτες, προέλευση.
// Ένα πρόσωπο σε άλλον πελάτη συνδέει μόνο ανθρώπους: τα υπόλοιπα δεν ενώνονται ποτέ (CRM-02).
export function ClientAbout({
  client,
  contacts,
  contactsLoading,
  onAddContact,
}: {
  client: Client
  contacts: ClientContact[] | undefined
  contactsLoading: boolean
  onAddContact: () => void
}) {
  const { t } = useTranslation('crm')
  const [showAll, setShowAll] = useState(false)
  const [editing, setEditing] = useState<ClientContact | null>(null)
  const [editingPersonId, setEditingPersonId] = useState<string | null>(null)
  const [removing, setRemoving] = useState<ClientContact | null>(null)
  const remove = useRemoveContact(client.id)
  const update = useUpdateContact(client.id)
  const person = usePerson(editingPersonId)

  const list = contacts ?? []
  const shown = showAll ? list : list.slice(0, visibleContacts)

  async function makePrimary(contact: ClientContact) {
    try {
      await update.mutateAsync({ id: contact.id, role: contact.role, isPrimary: true, isBillingContact: contact.isBillingContact, version: contact.version })
      toast.success(t('contacts.madePrimary', { name: contact.name }))
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <Section
        title={t('about.contacts')}
        action={
          client.canEdit && (
            <Button variant="ghost" size="sm" onClick={onAddContact}>
              <Plus /> {t('contacts.add.button')}
            </Button>
          )
        }
      >
        {contactsLoading ? (
          <Skeleton className="h-16 w-full" />
        ) : list.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('about.noContacts')}</p>
        ) : (
          <ul className="flex flex-col gap-3" aria-label={t('about.contacts')}>
            {shown.map((contact) => (
              <li key={contact.id} className="rounded-lg border p-3" data-testid="client-contact">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-1.5">
                      <Link to={`/contacts?person=${contact.personId}`} className="font-medium hover:underline">
                        {contact.name}
                      </Link>
                      {contact.isPrimary && (
                        <Badge variant="info">
                          <Star data-icon="inline-start" aria-hidden /> {t('contacts.primary')}
                        </Badge>
                      )}
                      {contact.isBillingContact && <Badge variant="muted">{t('contacts.billing')}</Badge>}
                    </div>
                    {contact.role && <div className="text-xs text-muted-foreground">{contact.role}</div>}
                  </div>
                  {client.canEdit && (
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon" className="-mt-1 -mr-1 size-8" aria-label={t('contacts.actionsFor', { name: contact.name })}>
                          <EllipsisVertical />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onSelect={() => setEditing(contact)}>
                          <UserCog /> {t('contacts.editRelation')}
                        </DropdownMenuItem>
                        <DropdownMenuItem onSelect={() => setEditingPersonId(contact.personId)}>
                          <Pencil /> {t('contacts.editPerson')}
                        </DropdownMenuItem>
                        {!contact.isPrimary && (
                          <DropdownMenuItem onSelect={() => makePrimary(contact)}>
                            <Star /> {t('contacts.makePrimary')}
                          </DropdownMenuItem>
                        )}
                        <DropdownMenuSeparator />
                        <DropdownMenuItem variant="destructive" onSelect={() => setRemoving(contact)}>
                          <Trash2 /> {t('contacts.remove')}
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  )}
                </div>
                <ContactMethodLinks methods={contact.contactMethods} className="mt-2 flex flex-col gap-0.5" />
                {contact.noPhoneReason && (
                  <p className="mt-1 text-xs text-muted-foreground">{t('people.noPhone', { reason: contact.noPhoneReason })}</p>
                )}
                {contact.alsoContactFor.length > 0 && (
                  <div className="mt-2 text-xs text-muted-foreground">
                    {t('contacts.alsoContactFor')}{' '}
                    {contact.alsoContactFor.map((other, index) => (
                      <span key={other.id}>
                        {index > 0 && ', '}
                        <Link to={`/clients/${other.id}`} className="inline-flex items-center gap-0.5 text-foreground hover:underline">
                          {other.displayName} <ArrowRight className="size-3" aria-hidden />
                        </Link>
                      </span>
                    ))}
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
        {list.length > visibleContacts && (
          <Button variant="link" size="sm" className="self-start px-0" onClick={() => setShowAll((v) => !v)}>
            {showAll ? t('about.showFewer') : t('about.showAll', { count: list.length })}
          </Button>
        )}
      </Section>

      {client.contactMethods.length > 0 && (
        <Section title={t('about.reachClient')}>
          <ContactMethodLinks methods={client.contactMethods} />
        </Section>
      )}

      <Section title={t('about.addresses')}>
        {client.addresses.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('about.noAddresses')}</p>
        ) : (
          <ul className="flex flex-col gap-3">
            {client.addresses.map((address, index) => (
              <li key={index} className="text-sm">
                <div className="text-xs text-muted-foreground">{t(`addressKinds.${address.kind}`)}</div>
                <address className="not-italic">
                  {[address.line1, address.line2, [address.postalCode, address.city].filter(Boolean).join(' '), address.region, address.country]
                    .filter(Boolean)
                    .map((line) => (
                      <div key={line}>{line}</div>
                    ))}
                </address>
              </li>
            ))}
          </ul>
        )}
      </Section>

      {client.tags.length > 0 && (
        <Section title={t('about.tags')}>
          <div className="flex flex-wrap gap-1">
            {client.tags.map((tag) => (
              <Link key={tag} to={`/clients?tag=${encodeURIComponent(tag)}`}>
                <Badge variant="muted">{tag}</Badge>
              </Link>
            ))}
          </div>
        </Section>
      )}

      <Section title={t('about.details')}>
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm">
          <dt className="text-muted-foreground">{t('about.currency')}</dt>
          <dd>{client.defaultCurrency}</dd>
          <dt className="text-muted-foreground">{t('about.created')}</dt>
          <dd>
            <DateTime value={client.createdAt} mode="date" />
          </dd>
          {client.origin !== 'Live' && (
            <>
              <dt className="text-muted-foreground">{t('about.origin')}</dt>
              <dd>
                <StatusBadge tone="historical">{t(`origin.${client.origin}`)}</StatusBadge>
              </dd>
            </>
          )}
        </dl>
        {client.notes && <p className="mt-1 text-sm whitespace-pre-line text-muted-foreground">{client.notes}</p>}
      </Section>

      <EditContactDialog contact={editing} clientId={client.id} onOpenChange={(open) => !open && setEditing(null)} />
      {person.data && (
        <PersonFormDialog open={!!editingPersonId} onOpenChange={(open) => !open && setEditingPersonId(null)} person={person.data} />
      )}
      <ConfirmDialog
        open={!!removing}
        onOpenChange={(open) => !open && setRemoving(null)}
        title={t('contacts.removeConfirm.title')}
        description={
          <Trans
            t={t}
            i18nKey="contacts.removeConfirm.description"
            values={{ name: removing?.name, client: client.displayName }}
            components={{ strong: <strong /> }}
          />
        }
        confirmLabel={t('contacts.remove')}
        destructive
        onConfirm={async () => {
          if (!removing) return
          try {
            await remove.mutateAsync(removing.id)
            toast.success(t('contacts.removed', { name: removing.name }))
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />
    </div>
  )
}
