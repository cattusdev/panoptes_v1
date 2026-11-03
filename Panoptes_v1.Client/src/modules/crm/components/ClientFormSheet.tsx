import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { Building2, ExternalLink, User } from 'lucide-react'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { FormField } from '@/components/form/FormField'
import { TagsInput } from '@/components/form/TagsInput'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { FieldGroup, FieldSeparator } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Spinner } from '@/components/ui/spinner'
import { Textarea } from '@/components/ui/textarea'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { ApiError, problemMessage } from '@/lib/api'
import i18n from '@/i18n'
import { applyServerErrors, useZodForm } from '@/lib/forms'
import {
  duplicateTaxIdMatches,
  useClientDuplicates,
  useClientTags,
  useCreateClient,
  useUpdateClient,
  type Client,
  type ClientInput,
  type ClientMatch,
} from '../api'
import {
  addressSchema,
  contactMethodSchema,
  fromAddresses,
  fromContactMethods,
  refineContactDetails,
  refineEmailOrPhone,
  toAddresses,
  toContactMethods,
} from '../forms'
import { AddressesField, ContactMethodsField } from './ContactFields'

const schema = z
  .object({
    kind: z.enum(['Organization', 'Individual']),
    displayName: z.string().trim().max(200),
    firstName: z.string().trim().max(100),
    lastName: z.string().trim().max(100),
    legalName: z.string().trim().max(300),
    taxId: z.string().trim().max(30),
    taxOffice: z.string().trim().max(100),
    status: z.enum(['Active', 'Inactive']),
    defaultCurrency: z
      .string()
      .trim()
      .regex(/^[A-Za-z]{3}$/),
    tags: z.array(z.string()),
    contactMethods: z.array(contactMethodSchema),
    addresses: z.array(addressSchema),
    notes: z.string().trim().max(4000),
  })
  .superRefine((values, ctx) => {
    // Εταιρεία: ένα όνομα. Ιδιώτης: όνομα και επώνυμο.
    const required = (field: 'displayName' | 'firstName' | 'lastName') => {
      if (!values[field].trim()) ctx.addIssue({ code: 'custom', path: [field], message: i18n.t('validation.required') })
    }
    if (values.kind === 'Individual') {
      required('firstName')
      required('lastName')
    } else required('displayName')
    refineContactDetails(values, ctx)
    refineEmailOrPhone(values, ctx)
  })

type Values = z.input<typeof schema>

const empty: Values = {
  kind: 'Organization',
  displayName: '',
  firstName: '',
  lastName: '',
  legalName: '',
  taxId: '',
  taxOffice: '',
  status: 'Active',
  defaultCurrency: 'EUR',
  tags: [],
  contactMethods: [
    { kind: 'Email', value: '', label: '' },
    { kind: 'Phone', value: '', label: '' },
  ],
  addresses: [],
  notes: '',
}

function toValues(client: Client): Values {
  return {
    kind: client.kind,
    displayName: client.displayName,
    // Ιδιώτης από πριν υπάρξουν όνομα/επώνυμο: το όνομά του στο "Όνομα", για να συμπληρωθεί το επώνυμο
    firstName: client.firstName ?? (client.kind === 'Individual' ? client.displayName : ''),
    lastName: client.lastName ?? '',
    legalName: client.legalName ?? '',
    taxId: client.taxId ?? '',
    taxOffice: client.taxOffice ?? '',
    status: client.status,
    defaultCurrency: client.defaultCurrency,
    tags: client.tags,
    contactMethods: fromContactMethods(client.contactMethods),
    addresses: fromAddresses(client.addresses),
    notes: client.notes ?? '',
  }
}

// Νέος πελάτης ή αλλαγή στοιχείων, σε πλαϊνό panel (όπως Attio / HubSpot: η λίστα μένει πίσω του).
// Όσο γράφει ο χρήστης ελέγχονται τα διπλότυπα. Ίδιος ΑΦΜ με άλλον πελάτη: το Save ζητά αιτιολογία ("Save anyway").
export function ClientFormSheet({ open, onOpenChange, client }: { open: boolean; onOpenChange: (open: boolean) => void; client?: Client }) {
  const { t } = useTranslation('crm')
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="w-full gap-0 data-[side=right]:sm:max-w-xl">
        <SheetHeader className="border-b">
          <SheetTitle>{client ? t('form.editTitle') : t('form.createTitle')}</SheetTitle>
          <SheetDescription>{client ? t('form.editDescription') : t('form.createDescription')}</SheetDescription>
        </SheetHeader>
        {open && <ClientForm client={client} onDone={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  )
}

function ClientForm({ client, onDone }: { client?: Client; onDone: () => void }) {
  const { t } = useTranslation('crm')
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const form = useZodForm(schema, client ? toValues(client) : empty)
  const create = useCreateClient()
  const update = useUpdateClient(client?.id ?? '')
  const tags = useClientTags()
  // Μετά από 409 crm.duplicateTaxId: ποιοι πελάτες έχουν τον ίδιο ΑΦΜ (για ποιον ΑΦΜ) και η αιτιολογία του χρήστη
  const [conflict, setConflict] = useState<{ taxId: string; matches: ClientMatch[] } | null>(null)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState(false)

  const [kind, typedName, firstName, lastName, legalName, taxId, contactMethods] = form.watch([
    'kind',
    'displayName',
    'firstName',
    'lastName',
    'legalName',
    'taxId',
    'contactMethods',
  ])
  const displayName = kind === 'Individual' ? `${firstName.trim()} ${lastName.trim()}` : typedName
  // Άλλαξε ο ΑΦΜ μετά τη σύγκρουση: δεν ισχύει πια
  const taxIdConflict = conflict && conflict.taxId === taxId.trim() ? conflict.matches : null
  const checked = useDebouncedValue(
    JSON.stringify({ displayName: displayName.trim(), legalName: legalName.trim(), taxId: taxId.trim(), contactMethods: toContactMethods(contactMethods) }),
  )
  const query = JSON.parse(checked) as { displayName: string; legalName: string; taxId: string; contactMethods: ReturnType<typeof toContactMethods> }
  const duplicates = useClientDuplicates(
    { ...query, excludeId: client?.id },
    query.displayName.length >= 3 || query.legalName.length >= 3 || query.taxId.length >= 5 || query.contactMethods.length > 0,
  )

  const onSubmit = form.handleSubmit(async (values) => {
    if (taxIdConflict && !reason.trim()) {
      setReasonError(true)
      return
    }
    const input: ClientInput = {
      kind: values.kind,
      displayName: values.kind === 'Organization' ? values.displayName : null,
      firstName: values.kind === 'Individual' ? values.firstName : null,
      lastName: values.kind === 'Individual' ? values.lastName : null,
      legalName: values.legalName || null,
      taxId: values.taxId || null,
      taxOffice: values.taxOffice || null,
      status: values.status,
      defaultCurrency: values.defaultCurrency.toUpperCase(),
      tags: values.tags,
      contactMethods: toContactMethods(values.contactMethods),
      addresses: toAddresses(values.addresses),
      notes: values.notes || null,
      version: client?.version,
      duplicateReason: taxIdConflict ? reason.trim() : null,
    }
    try {
      const saved = client ? await update.mutateAsync(input) : await create.mutateAsync(input)
      toast.success(client ? t('form.saved', { name: saved.displayName }) : t('form.created', { name: saved.displayName, ref: saved.ref }))
      onDone()
      if (!client) navigate(`/clients/${saved.id}`)
    } catch (error) {
      const matches = duplicateTaxIdMatches(error)
      if (matches) {
        setConflict({ taxId: values.taxId, matches })
        return
      }
      // Κάποιος άλλος άλλαξε τον πελάτη: τα στοιχεία της φόρμας μένουν, το Reload φέρνει το νέο version
      if (client && error instanceof ApiError && error.status === 409) {
        toast.error(problemMessage(error), {
          action: { label: t('form.reload'), onClick: () => queryClient.invalidateQueries({ queryKey: ['crm', 'client', client.id] }) },
        })
        return
      }
      if (!applyServerErrors(form, error)) toast.error(problemMessage(error))
    }
  })

  // Ο ΑΦΜ ελέγχεται μόνο όταν αλλάζει (όπως στον server): ένα παλιό, ήδη επιβεβαιωμένο διπλότυπο είναι απλή πληροφορία
  const taxIdChanged = !client || taxId.trim() !== (client.taxId ?? '')
  const matches = duplicates.data?.matches ?? []
  const isHard = (m: ClientMatch) => taxIdChanged && m.reasons.includes('taxId')
  const soft = matches.filter((m) => !isHard(m))
  const sameTaxId = taxIdConflict ?? matches.filter(isHard)

  return (
    <form onSubmit={onSubmit} noValidate className="flex min-h-0 flex-1 flex-col">
      <div className="flex-1 overflow-y-auto p-4">
        <FieldGroup>
          <FormField control={form.control} name="kind" label={t('form.kind')}>
            {(field) => (
              <ToggleGroup
                type="single"
                variant="outline"
                id={field.id}
                value={field.value}
                onValueChange={(value) => value && field.onChange(value)}
                className="w-fit"
              >
                <ToggleGroupItem value="Organization" className="gap-1.5 px-3">
                  <Building2 /> {t('kinds.Organization')}
                </ToggleGroupItem>
                <ToggleGroupItem value="Individual" className="gap-1.5 px-3">
                  <User /> {t('kinds.Individual')}
                </ToggleGroupItem>
              </ToggleGroup>
            )}
          </FormField>

          {kind === 'Individual' ? (
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField control={form.control} name="firstName" label={t('form.firstName')}>
                {(field) => <Input autoFocus={!client} maxLength={100} autoComplete="off" {...field} />}
              </FormField>
              <FormField control={form.control} name="lastName" label={t('form.lastName')}>
                {(field) => <Input maxLength={100} autoComplete="off" {...field} />}
              </FormField>
            </div>
          ) : (
            <FormField control={form.control} name="displayName" label={t('form.displayName')} description={t('form.displayNameHint')}>
              {(field) => <Input autoFocus={!client} maxLength={200} {...field} />}
            </FormField>
          )}

          <FormField control={form.control} name="legalName" label={t('form.legalName')}>
            {(field) => <Input maxLength={300} {...field} />}
          </FormField>

          <div className="grid gap-4 sm:grid-cols-2">
            <FormField control={form.control} name="taxId" label={t('form.taxId')}>
              {(field) => <Input maxLength={30} inputMode="text" {...field} />}
            </FormField>
            <FormField control={form.control} name="taxOffice" label={t('form.taxOffice')}>
              {(field) => <Input maxLength={100} {...field} />}
            </FormField>
          </div>

          <DuplicateWarnings
            sameTaxId={sameTaxId}
            soft={soft}
            needsReason={!!taxIdConflict}
            reason={reason}
            reasonError={reasonError}
            onReasonChange={(value) => {
              setReason(value)
              setReasonError(false)
            }}
          />

          <FieldSeparator />
          <ContactMethodsField control={form.control} idPrefix="client" />
          <AddressesField control={form.control} idPrefix="client" />
          <FieldSeparator />

          <FormField control={form.control} name="tags" label={t('form.tags')} description={t('form.tagsHint')}>
            {(field) => <TagsInput {...field} suggestions={tags.data} />}
          </FormField>

          <div className="grid gap-4 sm:grid-cols-2">
            {client && (
              <FormField control={form.control} name="status" label={t('form.status')}>
                {(field) => (
                  <OptionSelect
                    id={field.id}
                    value={field.value}
                    onChange={field.onChange}
                    options={(['Active', 'Inactive'] as const).map((value) => ({ value, label: t(`status.${value === 'Active' ? 'active' : 'inactive'}`) }))}
                  />
                )}
              </FormField>
            )}
            <FormField control={form.control} name="defaultCurrency" label={t('form.currency')} description={t('form.currencyHint')}>
              {(field) => <Input maxLength={3} className="uppercase" {...field} />}
            </FormField>
          </div>

          <FormField control={form.control} name="notes" label={t('form.notes')}>
            {(field) => <Textarea rows={3} maxLength={4000} {...field} />}
          </FormField>
        </FieldGroup>
      </div>

      <SheetFooter className="border-t sm:flex-row sm:justify-end">
        <Button type="button" variant="outline" onClick={onDone}>
          {t('actions.cancel', { ns: 'common' })}
        </Button>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting && <Spinner />}
          {taxIdConflict ? t('duplicates.saveAnyway') : client ? t('form.save') : t('form.create')}
        </Button>
      </SheetFooter>
    </form>
  )
}

// Πιθανά διπλότυπα κάτω από τα στοιχεία ταυτότητας: ίδιος ΑΦΜ = προειδοποίηση (και αιτιολογία μετά το Save),
// παρόμοιο όνομα / ίδιο email ή τηλέφωνο = πληροφορία. Κάθε πελάτης ανοίγει σε νέα καρτέλα (η φόρμα μένει).
function DuplicateWarnings({
  sameTaxId,
  soft,
  needsReason,
  reason,
  reasonError,
  onReasonChange,
}: {
  sameTaxId: ClientMatch[]
  soft: ClientMatch[]
  needsReason: boolean
  reason: string
  reasonError: boolean
  onReasonChange: (value: string) => void
}) {
  const { t } = useTranslation('crm')
  if (sameTaxId.length === 0 && soft.length === 0) return null

  return (
    <div className="flex flex-col gap-3" data-testid="client-duplicates">
      {sameTaxId.length > 0 && (
        <Alert variant="warning">
          <AlertTitle>{t('duplicates.taxIdTitle')}</AlertTitle>
          <AlertDescription>
            <MatchList matches={sameTaxId} />
            <p>{t('duplicates.taxIdDescription')}</p>
            {needsReason && (
              <div className="mt-2 flex w-full flex-col gap-1.5">
                <label htmlFor="duplicate-reason" className="font-medium text-foreground">
                  {t('duplicates.reason')}
                </label>
                <Textarea
                  id="duplicate-reason"
                  rows={2}
                  maxLength={500}
                  value={reason}
                  aria-invalid={reasonError || undefined}
                  onChange={(e) => onReasonChange(e.target.value)}
                  placeholder={t('duplicates.reasonPlaceholder')}
                  className="text-foreground"
                />
                {reasonError && <span className="text-destructive">{t('duplicates.reasonRequired')}</span>}
              </div>
            )}
          </AlertDescription>
        </Alert>
      )}
      {soft.length > 0 && (
        <Alert variant="info">
          <AlertTitle>{t('duplicates.similarTitle')}</AlertTitle>
          <AlertDescription>
            <MatchList matches={soft} />
            <p>{t('duplicates.similarDescription')}</p>
          </AlertDescription>
        </Alert>
      )}
    </div>
  )
}

function MatchList({ matches }: { matches: ClientMatch[] }) {
  const { t } = useTranslation('crm')
  return (
    <ul className="my-1 flex w-full flex-col gap-1">
      {matches.map((match) => (
        <li key={match.id} className="flex flex-wrap items-center gap-x-2">
          <Link to={`/clients/${match.id}`} target="_blank" className="inline-flex items-center gap-1 font-medium underline underline-offset-2">
            {match.displayName} <ExternalLink className="size-3" aria-hidden />
          </Link>
          <span className="font-mono text-xs">{match.ref}</span>
          <span className="text-xs">
            · {match.reasons.map((r) => t(`duplicates.reasons.${r}`)).join(' · ')}
            {match.isArchived && ` · ${t('status.archived')}`}
          </span>
        </li>
      ))}
    </ul>
  )
}
