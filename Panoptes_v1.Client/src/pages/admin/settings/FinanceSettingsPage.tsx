import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { z } from 'zod'
import { TriangleAlert } from 'lucide-react'
import { SettingsCard } from '@/components/admin/Common'
import { DatePicker } from '@/components/form/DatePicker'
import { FormField } from '@/components/form/FormField'
import { PercentInput } from '@/components/form/PercentInput'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { FieldGroup } from '@/components/ui/field'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Textarea } from '@/components/ui/textarea'
import i18n from '@/i18n'
import { problemMessage } from '@/lib/api'
import { useCan } from '@/lib/auth'
import { formatPercentNumber } from '@/lib/money'
import { useDateFormat } from '@/lib/dates'
import { applyServerErrors, schemas, useZodForm } from '@/lib/forms'
import { Permissions } from '@/lib/permissions'
import { useFinanceSettings, useUpdateFinanceSettings, type FinanceSettings, type FinanceSettingsInput } from '@/lib/settings-api'
import { ReadOnlyNotice } from './ReadOnlyNotice'

// Κείμενο που δεν είναι αριθμός (NaN από το PercentInput): δικό του μήνυμα, όχι το "Enter an amount".
// Τα όρια (min/max) κρατούν τα κοινά μηνύματα (undefined = το global).
const vatSchema = z.object({
  defaultVatRate: schemas.required(
    z
      .number({ error: (issue) => (issue.code === 'invalid_type' ? i18n.t('finance.vat.invalid', { ns: 'settings' }) : undefined) })
      .min(0)
      .max(100),
  ),
})

const trackingSchema = z.object({
  trackingStartDate: schemas.required(schemas.date()),
  reason: z.string().trim().max(500),
})

// ΦΠΑ και ημερομηνία έναρξης της οικονομικής καταγραφής (ARCHITECTURE §5.4, §5.9). Όπως το "Tax rates" του Stripe:
// η προεπιλογή ισχύει μόνο για ό,τι δημιουργείται από εδώ και πέρα. Μία κάρτα ανά θέμα με τη δική της αποθήκευση (General).
// Η ημερομηνία έναρξης αλλάζει μόνο με αιτιολογία, με προειδοποίηση για τις συνέπειες (όπως τα "dangerous" settings του GitHub).
export function FinanceSettingsPage() {
  const { t } = useTranslation('settings')
  const settings = useFinanceSettings()
  const canEdit = useCan(Permissions.Finance.Settings)

  if (settings.isError) return <p className="text-sm text-destructive">{problemMessage(settings.error)}</p>
  if (!settings.data) return <Skeleton className="h-80 rounded-[14px]" />

  const key = JSON.stringify(settings.data)
  return (
    <div className="grid gap-4">
      {!canEdit && <ReadOnlyNotice message={t('finance.readOnly')} />}
      <VatForm key={`vat-${key}`} settings={settings.data} canEdit={canEdit} />
      <TrackingStartForm key={`start-${key}`} settings={settings.data} canEdit={canEdit} />
    </div>
  )
}

type FormProps = { settings: FinanceSettings; canEdit: boolean }

// Στέλνει όλη την ενότητα, με τις αλλαγές της κάρτας
function useSaveFinance(settings: FinanceSettings) {
  const update = useUpdateFinanceSettings()
  return (changes: Partial<FinanceSettingsInput>) => update.mutateAsync({ ...settings, reason: null, ...changes })
}

function SaveButton({ dirty, pending }: { dirty: boolean; pending: boolean }) {
  const { t } = useTranslation()
  return (
    <Button type="submit" disabled={!dirty || pending}>
      {pending && <Spinner />}
      {t('actions.saveChanges')}
    </Button>
  )
}

function VatForm({ settings, canEdit }: FormProps) {
  const { t, i18n: translation } = useTranslation('settings')
  const save = useSaveFinance(settings)
  const form = useZodForm(vatSchema, { defaultVatRate: settings.defaultVatRate })

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const saved = await save(values)
      toast.success(t('finance.vat.saved'), { description: t('finance.vat.savedDescription', { rate: formatPercentNumber(saved.defaultVatRate, translation.language) }) })
    } catch (error) {
      if (!applyServerErrors(form, error)) toast.error(problemMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <SettingsCard
        title={t('finance.vat.title')}
        description={t('finance.vat.description')}
        footer={canEdit && <SaveButton dirty={form.formState.isDirty} pending={form.formState.isSubmitting} />}
      >
        <FieldGroup>
          <FormField control={form.control} name="defaultVatRate" id="default-vat-rate" label={t('finance.vat.rate')} description={t('finance.vat.rateHint')}>
            {(field) => <PercentInput {...field} disabled={!canEdit} className="max-w-36" />}
          </FormField>
        </FieldGroup>
      </SettingsCard>
    </form>
  )
}

function TrackingStartForm({ settings, canEdit }: FormProps) {
  const { t } = useTranslation('settings')
  const format = useDateFormat()
  const save = useSaveFinance(settings)
  const form = useZodForm(trackingSchema, { trackingStartDate: settings.trackingStartDate, reason: '' })
  const changed = form.watch('trackingStartDate') !== settings.trackingStartDate

  const onSubmit = form.handleSubmit(async (values) => {
    if (!values.reason) {
      form.setError('reason', { type: 'required', message: t('finance.trackingStart.reasonRequired') }, { shouldFocus: true })
      return
    }
    try {
      const saved = await save(values)
      toast.success(t('finance.trackingStart.saved'), {
        description: t('finance.trackingStart.savedDescription', { date: format.day(saved.trackingStartDate) }),
      })
    } catch (error) {
      if (!applyServerErrors(form, error)) toast.error(problemMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <SettingsCard
        title={t('finance.trackingStart.title')}
        description={t('finance.trackingStart.description')}
        footer={canEdit && <SaveButton dirty={changed} pending={form.formState.isSubmitting} />}
      >
        <FieldGroup>
          <FormField
            control={form.control}
            name="trackingStartDate"
            id="tracking-start-date"
            label={t('finance.trackingStart.date')}
            description={t('finance.trackingStart.dateHint')}
          >
            {(field) => <DatePicker {...field} disabled={!canEdit} />}
          </FormField>

          {changed && (
            <>
              <Alert variant="warning">
                <TriangleAlert />
                <AlertTitle>{t('finance.trackingStart.changeTitle')}</AlertTitle>
                <AlertDescription>{t('finance.trackingStart.changeText')}</AlertDescription>
              </Alert>
              <FormField
                control={form.control}
                name="reason"
                id="tracking-start-reason"
                label={t('finance.trackingStart.reason')}
                description={t('finance.trackingStart.reasonHint')}
              >
                {(field) => <Textarea rows={3} maxLength={500} placeholder={t('finance.trackingStart.reasonPlaceholder')} {...field} />}
              </FormField>
            </>
          )}
        </FieldGroup>
      </SettingsCard>
    </form>
  )
}
