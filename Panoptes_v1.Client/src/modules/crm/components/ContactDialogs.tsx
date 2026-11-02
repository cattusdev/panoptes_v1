import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { z } from 'zod'
import { Check, Search, UserPlus, Users } from 'lucide-react'
import { FormField } from '@/components/form/FormField'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { problemMessage } from '@/lib/api'
import { applyServerErrors, useZodForm } from '@/lib/forms'
import { cn } from '@/lib/utils'
import { useAddContact, usePeople, useUpdateContact, type ClientContact } from '../api'
import { emptyPerson, personSchema, toPersonInput } from '../forms'
import { PersonDuplicates, PersonFields } from './PersonFields'

// Προτάσεις ρόλων (ελεύθερο κείμενο, ο browser τις προτείνει)
const roleSuggestions = ['owner', 'ceo', 'accounting', 'it', 'marketing', 'projectManager'] as const

function RoleInput({ id, value, onChange }: { id: string; value: string; onChange: (value: string) => void }) {
  const { t } = useTranslation('crm')
  return (
    <>
      <Input id={id} list={`${id}-suggestions`} maxLength={100} value={value} onChange={(e) => onChange(e.target.value)} />
      <datalist id={`${id}-suggestions`}>
        {roleSuggestions.map((role) => (
          <option key={role} value={t(`contacts.roles.${role}`)} />
        ))}
      </datalist>
    </>
  )
}

function Flags({
  isPrimary,
  isBillingContact,
  onChange,
  idPrefix,
}: {
  isPrimary: boolean
  isBillingContact: boolean
  onChange: (flags: { isPrimary: boolean; isBillingContact: boolean }) => void
  idPrefix: string
}) {
  const { t } = useTranslation('crm')
  return (
    <div className="flex flex-col gap-3">
      <Field orientation="horizontal">
        <Checkbox
          id={`${idPrefix}-primary`}
          checked={isPrimary}
          onCheckedChange={(checked) => onChange({ isPrimary: checked === true, isBillingContact })}
        />
        <FieldLabel htmlFor={`${idPrefix}-primary`} className="font-normal">
          {t('contacts.makePrimaryHint')}
        </FieldLabel>
      </Field>
      <Field orientation="horizontal">
        <Checkbox
          id={`${idPrefix}-billing`}
          checked={isBillingContact}
          onCheckedChange={(checked) => onChange({ isPrimary, isBillingContact: checked === true })}
        />
        <FieldLabel htmlFor={`${idPrefix}-billing`} className="font-normal">
          {t('contacts.billingHint')}
        </FieldLabel>
      </Field>
    </div>
  )
}

const newPersonSchema = personSchema

// "Add contact": ένα πρόσωπο που υπάρχει ήδη (π.χ. είναι επαφή και σε άλλον πελάτη) ή ένα νέο.
// Αν το νέο μοιάζει με υπάρχον, προτείνεται να χρησιμοποιηθεί εκείνο (ένα πρόσωπο, πολλοί πελάτες, CRM-01).
export function AddContactDialog({
  open,
  onOpenChange,
  clientId,
  clientName,
  hasPrimary,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  clientId: string
  clientName: string
  hasPrimary: boolean
}) {
  const { t } = useTranslation('crm')
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t('contacts.add.title')}</DialogTitle>
          <DialogDescription>{t('contacts.add.description', { client: clientName })}</DialogDescription>
        </DialogHeader>
        {open && <AddContactForm clientId={clientId} hasPrimary={hasPrimary} onDone={() => onOpenChange(false)} />}
      </DialogContent>
    </Dialog>
  )
}

function AddContactForm({ clientId, hasPrimary, onDone }: { clientId: string; hasPrimary: boolean; onDone: () => void }) {
  const { t } = useTranslation('crm')
  const add = useAddContact(clientId)
  const [mode, setMode] = useState<'new' | 'existing'>('new')
  const [search, setSearch] = useState('')
  const debouncedSearch = useDebouncedValue(search.trim())
  const [picked, setPicked] = useState<{ id: string; name: string } | null>(null)
  const [role, setRole] = useState('')
  const [flags, setFlags] = useState({ isPrimary: !hasPrimary, isBillingContact: false })
  const [pickError, setPickError] = useState(false)
  const form = useZodForm(newPersonSchema, emptyPerson)
  const values = form.watch()
  const people = usePeople({ search: debouncedSearch, page: 1, pageSize: 8 }, mode === 'existing')

  async function save(personInput: ReturnType<typeof toPersonInput> | null) {
    try {
      await add.mutateAsync({
        personId: personInput ? null : picked?.id,
        person: personInput,
        role: role.trim() || null,
        ...flags,
      })
      toast.success(t('contacts.add.added', { name: personInput ? `${personInput.firstName} ${personInput.lastName ?? ''}`.trim() : picked?.name }))
      onDone()
    } catch (error) {
      if (!personInput || !applyServerErrors(form, error)) toast.error(problemMessage(error))
    }
  }

  const onSubmit =
    mode === 'new'
      ? form.handleSubmit((submitted) => save(toPersonInput(submitted)))
      : (event: React.FormEvent) => {
          event.preventDefault()
          if (!picked) {
            setPickError(true)
            return
          }
          void save(null)
        }

  return (
    <form onSubmit={onSubmit} noValidate>
      <FieldGroup>
        <ToggleGroup
          type="single"
          variant="outline"
          value={mode}
          onValueChange={(value) => value && setMode(value as 'new' | 'existing')}
          className="w-fit"
          aria-label={t('contacts.add.mode')}
        >
          <ToggleGroupItem value="new" className="gap-1.5 px-3">
            <UserPlus /> {t('contacts.add.newPerson')}
          </ToggleGroupItem>
          <ToggleGroupItem value="existing" className="gap-1.5 px-3">
            <Users /> {t('contacts.add.existingPerson')}
          </ToggleGroupItem>
        </ToggleGroup>

        {mode === 'new' ? (
          <>
            <PersonFields control={form.control} idPrefix="add-contact" withNotes={false} />
            <PersonDuplicates
              values={values}
              onPick={(match) => {
                setPicked({ id: match.id, name: match.name })
                setSearch(match.name)
                setMode('existing')
              }}
            />
          </>
        ) : (
          <Field data-invalid={pickError || undefined}>
            <FieldLabel htmlFor="add-contact-search">{t('contacts.add.searchLabel')}</FieldLabel>
            <div className="relative">
              <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                id="add-contact-search"
                type="search"
                autoFocus
                className="pl-9"
                value={search}
                placeholder={t('contacts.add.searchPlaceholder')}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
            <ul className="flex max-h-60 flex-col overflow-y-auto rounded-lg border" role="listbox" aria-label={t('contacts.add.results')}>
              {(people.data?.items ?? []).map((person) => {
                const selected = picked?.id === person.id
                return (
                  <li key={person.id} role="option" aria-selected={selected}>
                    <button
                      type="button"
                      className={cn('flex w-full items-center gap-3 px-3 py-2 text-left text-sm hover:bg-muted', selected && 'bg-muted')}
                      onClick={() => {
                        setPicked({ id: person.id, name: person.name })
                        setPickError(false)
                      }}
                    >
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium">{person.name}</span>
                        <span className="block truncate text-xs text-muted-foreground">
                          {[person.email, person.clients.map((c) => c.displayName).join(', ')].filter(Boolean).join(' · ')}
                        </span>
                      </span>
                      {selected && <Check className="size-4 shrink-0" aria-hidden />}
                    </button>
                  </li>
                )
              })}
              {people.data && people.data.items.length === 0 && (
                <li className="px-3 py-4 text-center text-sm text-muted-foreground">{t('contacts.add.noResults')}</li>
              )}
            </ul>
            {pickError && <span className="text-sm text-destructive">{t('contacts.add.pickPerson')}</span>}
          </Field>
        )}

        <Field>
          <FieldLabel htmlFor="add-contact-role">{t('contacts.role')}</FieldLabel>
          <RoleInput id="add-contact-role" value={role} onChange={setRole} />
        </Field>
        <Flags {...flags} onChange={setFlags} idPrefix="add-contact" />
      </FieldGroup>

      <DialogFooter className="mt-6">
        <Button type="button" variant="outline" onClick={onDone}>
          {t('actions.cancel', { ns: 'common' })}
        </Button>
        <Button type="submit" disabled={add.isPending}>
          {add.isPending && <Spinner />}
          {t('contacts.add.submit')}
        </Button>
      </DialogFooter>
    </form>
  )
}

const relationSchema = z.object({ role: z.string().trim().max(100), isPrimary: z.boolean(), isBillingContact: z.boolean() })

// Ρόλος και σημάνσεις της επαφής σε αυτόν τον πελάτη (τα στοιχεία του προσώπου αλλάζουν από τις Επαφές)
export function EditContactDialog({
  contact,
  clientId,
  onOpenChange,
}: {
  contact: ClientContact | null
  clientId: string
  onOpenChange: (open: boolean) => void
}) {
  const { t } = useTranslation('crm')
  return (
    <Dialog open={!!contact} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t('contacts.edit.title', { name: contact?.name })}</DialogTitle>
          <DialogDescription>{t('contacts.edit.description')}</DialogDescription>
        </DialogHeader>
        {contact && <EditContactForm contact={contact} clientId={clientId} onDone={() => onOpenChange(false)} />}
      </DialogContent>
    </Dialog>
  )
}

function EditContactForm({ contact, clientId, onDone }: { contact: ClientContact; clientId: string; onDone: () => void }) {
  const { t } = useTranslation('crm')
  const update = useUpdateContact(clientId)
  const form = useZodForm(relationSchema, { role: contact.role ?? '', isPrimary: contact.isPrimary, isBillingContact: contact.isBillingContact })
  const [isPrimary, isBillingContact] = form.watch(['isPrimary', 'isBillingContact'])

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await update.mutateAsync({ id: contact.id, role: values.role || null, isPrimary: values.isPrimary, isBillingContact: values.isBillingContact, version: contact.version })
      toast.success(t('contacts.edit.saved', { name: contact.name }))
      onDone()
    } catch (error) {
      if (!applyServerErrors(form, error)) toast.error(problemMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <FieldGroup>
        <FormField control={form.control} name="role" id="edit-contact-role" label={t('contacts.role')}>
          {(field) => <RoleInput id={field.id} value={field.value} onChange={field.onChange} />}
        </FormField>
        <Flags
          isPrimary={isPrimary}
          isBillingContact={isBillingContact}
          onChange={(flags) => {
            form.setValue('isPrimary', flags.isPrimary, { shouldDirty: true })
            form.setValue('isBillingContact', flags.isBillingContact, { shouldDirty: true })
          }}
          idPrefix="edit-contact"
        />
      </FieldGroup>
      <DialogFooter className="mt-6">
        <Button type="button" variant="outline" onClick={onDone}>
          {t('actions.cancel', { ns: 'common' })}
        </Button>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting && <Spinner />}
          {t('form.save')}
        </Button>
      </DialogFooter>
    </form>
  )
}
