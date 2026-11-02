import { useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { SettingsCard } from '@/components/admin/Common'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { Button } from '@/components/ui/button'
import { Field, FieldContent, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { languages } from '@/i18n'
import { fieldErrors, problemMessage } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import { createDateFormatter, dateFormats, timeZoneOptions } from '@/lib/dates'
import { Permissions } from '@/lib/permissions'
import { useGeneralSettings, useUpdateGeneralSettings, type GeneralSettings } from '@/lib/settings-api'
import { ReadOnlyNotice } from './ReadOnlyNotice'

// Γλώσσα και μορφή ημερομηνίας / ώρας για όλη την εφαρμογή (όπως "Language & region" στο Notion / Slack).
// Μία κάρτα ανά θέμα, η καθεμία με τη δική της αποθήκευση (όπως στο Vercel).
export function GeneralSettingsPage() {
  const settings = useGeneralSettings()
  const { user } = useSession()
  const canEdit = hasPermission(user, Permissions.Settings.Edit)

  // Το placeholder (από localStorage) δεν αρκεί για φόρμα: περιμένουμε τις πραγματικές τιμές
  if (settings.isError) return <p className="text-sm text-destructive">{problemMessage(settings.error)}</p>
  if (!settings.data || settings.isPlaceholderData) return <Skeleton className="h-80 rounded-[14px]" />

  const key = JSON.stringify(settings.data)
  return (
    <div className="grid gap-4">
      {!canEdit && <ReadOnlyNotice />}
      <LanguageForm key={`language-${key}`} settings={settings.data} canEdit={canEdit} />
      <DateTimeForm key={`date-${key}`} settings={settings.data} canEdit={canEdit} />
    </div>
  )
}

type FormProps = { settings: GeneralSettings; canEdit: boolean }

// Αποθήκευση μιας κάρτας: στέλνει όλες τις γενικές ρυθμίσεις, με τις αλλαγές της κάρτας
function useSaveGeneral(settings: GeneralSettings) {
  const update = useUpdateGeneralSettings()
  const [errors, setErrors] = useState<Record<string, string>>({})

  async function save(changes: Partial<GeneralSettings>, success: () => void) {
    setErrors({})
    try {
      await update.mutateAsync({ ...settings, ...changes })
      success()
    } catch (error) {
      const byField = fieldErrors(error)
      if (Object.keys(byField).length > 0) setErrors(byField)
      else toast.error(problemMessage(error))
    }
  }

  return { save, errors, pending: update.isPending }
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

function LanguageForm({ settings, canEdit }: FormProps) {
  const { t } = useTranslation('settings')
  const { save, errors, pending } = useSaveGeneral(settings)
  const [defaultLanguage, setDefaultLanguage] = useState(settings.defaultLanguage)

  function submit(e: FormEvent) {
    e.preventDefault()
    void save({ defaultLanguage }, () =>
      toast.success(t('general.language.saved'), { description: t('general.language.savedDescription') }),
    )
  }

  return (
    <form onSubmit={submit}>
      <SettingsCard
        title={t('general.language.title')}
        description={t('general.language.description')}
        footer={canEdit && <SaveButton dirty={defaultLanguage !== settings.defaultLanguage} pending={pending} />}
      >
        <FieldGroup>
          <Field orientation="responsive" data-invalid={!!errors.defaultLanguage || undefined}>
            <FieldContent>
              <FieldLabel htmlFor="default-language">{t('general.language.default')}</FieldLabel>
              <FieldDescription>{t('general.language.defaultHint')}</FieldDescription>
            </FieldContent>
            <div className="@md/field-group:w-72! @md/field-group:shrink-0">
              <OptionSelect
                id="default-language"
                value={defaultLanguage}
                onChange={setDefaultLanguage}
                options={languages.map((language) => ({ value: language.code, label: language.name }))}
                disabled={!canEdit}
              />
              <FieldError>{errors.defaultLanguage}</FieldError>
            </div>
          </Field>
        </FieldGroup>
      </SettingsCard>
    </form>
  )
}

function DateTimeForm({ settings, canEdit }: FormProps) {
  const { t, i18n } = useTranslation('settings')
  const { save, errors, pending } = useSaveGeneral(settings)
  const [dateFormat, setDateFormat] = useState(settings.dateFormat)
  const [timeFormat, setTimeFormat] = useState(settings.timeFormat)
  const [timeZone, setTimeZone] = useState(settings.timeZone ?? '')

  const dirty = dateFormat !== settings.dateFormat || timeFormat !== settings.timeFormat || (timeZone || null) !== settings.timeZone

  const [now] = useState(() => new Date())
  const zones = useMemo(() => timeZoneOptions(), [])
  const preview = createDateFormatter({ dateFormat, timeFormat, timeZone: timeZone || null }, i18n.language)
  const formatOptions = dateFormats.map((format) => ({
    value: format.value,
    label: `${createDateFormatter({ dateFormat: format.value, timeFormat, timeZone: timeZone || null }, i18n.language).date(now)}  ·  ${
      'hintKey' in format ? t(`general.formats.${format.hintKey}`) : format.hint
    }`,
  }))

  function submit(e: FormEvent) {
    e.preventDefault()
    void save({ dateFormat, timeFormat, timeZone: timeZone || null }, () =>
      toast.success(t('general.dateTime.saved'), { description: t('general.dateTime.savedDescription') }),
    )
  }

  return (
    <form onSubmit={submit}>
      <SettingsCard
        title={t('general.dateTime.title')}
        description={t('general.dateTime.description')}
        footer={
          <>
            <span className="mr-auto" data-testid="date-preview">
              {t('general.dateTime.preview')} <span className="font-medium text-foreground">{preview.dateTime(now)}</span>
            </span>
            {canEdit && <SaveButton dirty={dirty} pending={pending} />}
          </>
        }
      >
        <FieldGroup>
          <Field orientation="responsive" data-invalid={!!errors.dateFormat || undefined}>
            <FieldContent>
              <FieldLabel htmlFor="date-format">{t('general.dateTime.dateFormat')}</FieldLabel>
              <FieldDescription>{t('general.dateTime.dateFormatHint')}</FieldDescription>
            </FieldContent>
            <div className="@md/field-group:w-72! @md/field-group:shrink-0">
              <OptionSelect id="date-format" value={dateFormat} onChange={setDateFormat} options={formatOptions} disabled={!canEdit} />
              <FieldError>{errors.dateFormat}</FieldError>
            </div>
          </Field>

          <Field orientation="responsive" data-invalid={!!errors.timeFormat || undefined}>
            <FieldContent>
              <FieldLabel id="time-format-label">{t('general.dateTime.timeFormat')}</FieldLabel>
              <FieldDescription>{t('general.dateTime.timeFormatHint')}</FieldDescription>
            </FieldContent>
            <div className="@md/field-group:w-72! @md/field-group:shrink-0">
              <ToggleGroup
                type="single"
                variant="outline"
                value={timeFormat}
                onValueChange={(value) => value && setTimeFormat(value)}
                disabled={!canEdit}
                aria-labelledby="time-format-label"
                className="w-full"
              >
                <ToggleGroupItem value="24h" className="flex-1">
                  {t('general.dateTime.hours24')}
                </ToggleGroupItem>
                <ToggleGroupItem value="12h" className="flex-1">
                  {t('general.dateTime.hours12')}
                </ToggleGroupItem>
              </ToggleGroup>
              <FieldError>{errors.timeFormat}</FieldError>
            </div>
          </Field>

          <Field orientation="responsive" data-invalid={!!errors.timeZone || undefined}>
            <FieldContent>
              <FieldLabel htmlFor="time-zone">{t('general.dateTime.timeZone')}</FieldLabel>
              <FieldDescription>{t('general.dateTime.timeZoneHint')}</FieldDescription>
            </FieldContent>
            <div className="@md/field-group:w-72! @md/field-group:shrink-0">
              <OptionSelect
                id="time-zone"
                value={timeZone}
                onChange={setTimeZone}
                options={zones}
                emptyLabel={t('general.dateTime.viewerTimeZone')}
                disabled={!canEdit}
              />
              <FieldError>{errors.timeZone}</FieldError>
            </div>
          </Field>
        </FieldGroup>
      </SettingsCard>
    </form>
  )
}
