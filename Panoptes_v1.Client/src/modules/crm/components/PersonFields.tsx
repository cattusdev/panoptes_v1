import { useTranslation } from 'react-i18next'
import { useFormState, useWatch, type Control } from 'react-hook-form'
import { FormField } from '@/components/form/FormField'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { usePersonDuplicates, type PersonInput, type PersonMatch } from '../api'
import { hasPhone, toPersonInput, type PersonValues } from '../forms'
import { ContactMethodsField } from './ContactFields'

// Τα στοιχεία ενός προσώπου: κοινά στη φόρμα "New contact" και στο "Add contact" του πελάτη
type WithPerson = PersonValues

export function PersonFields({ control, idPrefix, withNotes = true }: { control: Control<WithPerson>; idPrefix: string; withNotes?: boolean }) {
  const { t } = useTranslation('crm')
  return (
    <>
      <div className="grid gap-4 sm:grid-cols-2">
        <FormField control={control} name="firstName" id={`${idPrefix}-first-name`} label={t('people.form.firstName')}>
          {(field) => <Input autoFocus maxLength={100} {...field} />}
        </FormField>
        <FormField control={control} name="lastName" id={`${idPrefix}-last-name`} label={t('people.form.lastName')}>
          {(field) => <Input maxLength={100} {...field} />}
        </FormField>
      </div>
      <ContactMethodsField control={control} idPrefix={idPrefix} />
      <NoPhoneReasonField control={control} idPrefix={idPrefix} />
      {withNotes && (
        <FormField control={control} name="notes" id={`${idPrefix}-notes`} label={t('form.notes')}>
          {(field) => <Textarea rows={3} maxLength={4000} {...field} />}
        </FormField>
      )}
    </>
  )
}

// Χωρίς τηλέφωνο: ένα πεδίο "γιατί" (για όταν βιαζόμαστε). Εμφανίζεται μετά την πρώτη προσπάθεια αποθήκευσης
// χωρίς τηλέφωνο, ή όταν υπάρχει ήδη λόγος· φεύγει μόλις μπει τηλέφωνο.
function NoPhoneReasonField({ control, idPrefix }: { control: Control<WithPerson>; idPrefix: string }) {
  const { t } = useTranslation('crm')
  const [methods, reason] = useWatch({ control, name: ['contactMethods', 'noPhoneReason'] })
  const { submitCount } = useFormState({ control })
  if (hasPhone(methods) || (submitCount === 0 && !reason)) return null

  return (
    <FormField
      control={control}
      name="noPhoneReason"
      id={`${idPrefix}-no-phone-reason`}
      label={t('people.form.noPhoneReason')}
      description={t('people.form.noPhoneReasonHint')}
    >
      {(field) => <Input maxLength={300} placeholder={t('people.form.noPhoneReasonPlaceholder')} {...field} />}
    </FormField>
  )
}

// Πρόσωπα που ίσως υπάρχουν ήδη (ίδιο email/τηλέφωνο ή παρόμοιο όνομα). onPick: "χρησιμοποίησε αυτό" αντί για νέο.
export function PersonDuplicates({
  values,
  excludeId,
  onPick,
}: {
  values: PersonValues
  excludeId?: string
  onPick?: (match: PersonMatch) => void
}) {
  const { t } = useTranslation('crm')
  const checked = useDebouncedValue(JSON.stringify(toPersonInput(values)))
  const input = JSON.parse(checked) as PersonInput
  const duplicates = usePersonDuplicates(input, excludeId, input.firstName.length >= 2 || input.contactMethods.length > 0)
  const matches = duplicates.data ?? []
  if (matches.length === 0) return null

  return (
    <Alert variant="info" data-testid="person-duplicates">
      <AlertTitle>{t('people.duplicates.title')}</AlertTitle>
      <AlertDescription>
        <ul className="my-1 flex w-full flex-col gap-1">
          {matches.map((match) => (
            <li key={match.id} className="flex flex-wrap items-center gap-x-2">
              <span className="font-medium">{match.name}</span>
              {match.email && <span className="text-xs">{match.email}</span>}
              <span className="text-xs">· {match.reasons.map((r) => t(`duplicates.reasons.${r}`)).join(' · ')}</span>
              {onPick && (
                <button type="button" className="text-xs font-medium underline underline-offset-2" onClick={() => onPick(match)}>
                  {t('people.duplicates.use')}
                </button>
              )}
            </li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  )
}
