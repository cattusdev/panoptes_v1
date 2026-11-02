import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import logoUrl from '@/assets/logo.svg'
import { SettingsCard } from '@/components/admin/Common'
import { MediaField } from '@/components/media/MediaField'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { fieldErrors, problemMessage } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import { useBrandingSettings, useUpdateBranding, type BrandingSettings } from '@/lib/branding'
import type { MediaItem } from '@/lib/media-api'
import { Permissions } from '@/lib/permissions'
import { ReadOnlyNotice } from './ReadOnlyNotice'

// Όνομα, λογότυπο και favicon (όπως τα project settings του Vercel/Supabase): κάρτες + ζωντανή προεπισκόπηση.
// Ενότητα του Settings (ο τίτλος έρχεται από το SettingsLayout).
export function BrandingPage() {
  const settings = useBrandingSettings()
  if (settings.isPending) return <Skeleton className="h-96 rounded-[14px]" />
  if (settings.isError) return <p className="text-sm text-destructive">{problemMessage(settings.error)}</p>
  return <BrandingForm key={JSON.stringify([settings.data.appName, settings.data.logo?.id, settings.data.favicon?.id])} settings={settings.data} />
}

function BrandingForm({ settings }: { settings: BrandingSettings }) {
  const { t } = useTranslation('settings')
  const { user } = useSession()
  const update = useUpdateBranding()
  const [appName, setAppName] = useState(settings.appName)
  const [logo, setLogo] = useState<MediaItem | null>(settings.logo)
  const [favicon, setFavicon] = useState<MediaItem | null>(settings.favicon)
  const [errors, setErrors] = useState<Record<string, string>>({})

  const canEdit = hasPermission(user, Permissions.Settings.Edit)
  const dirty = appName !== settings.appName || logo?.id !== settings.logo?.id || favicon?.id !== settings.favicon?.id
  const makesPublic = [logo, favicon].some((item) => item && item.visibility === 'Private')

  async function save(e: FormEvent) {
    e.preventDefault()
    setErrors({})
    try {
      await update.mutateAsync({ appName, logoMediaId: logo?.id ?? null, faviconMediaId: favicon?.id ?? null })
      toast.success(t('branding.saved'), { description: t('branding.savedDescription') })
    } catch (error) {
      const byField = fieldErrors(error)
      if (Object.keys(byField).length > 0) setErrors(byField)
      else toast.error(problemMessage(error))
    }
  }

  const logoSrc = logo?.version.urls.medium ?? logo?.version.urls.original
  const faviconSrc = favicon?.version.urls.thumb ?? favicon?.version.urls.original

  return (
    <form onSubmit={save} className="grid gap-4 2xl:grid-cols-[minmax(0,1fr)_320px] 2xl:items-start">
      <div className="grid gap-4">
        {!canEdit && <ReadOnlyNotice />}

        <SettingsCard title={t('branding.appName')} description={t('branding.appNameHint')}>
          <Field data-invalid={!!errors.appName || undefined}>
            <FieldLabel htmlFor="app-name" className="sr-only">
              {t('branding.appName')}
            </FieldLabel>
            <Input
              id="app-name"
              required
              maxLength={50}
              value={appName}
              onChange={(e) => setAppName(e.target.value)}
              disabled={!canEdit}
              aria-invalid={!!errors.appName || undefined}
              className="max-w-sm"
            />
            <FieldError>{errors.appName}</FieldError>
          </Field>
        </SettingsCard>

        <SettingsCard title={t('branding.logo')} description={t('branding.logoHint')}>
          <MediaField id="branding-logo" value={logo} onChange={setLogo} placeholder={t('branding.chooseLogo')} pickerTitle={t('branding.chooseLogo')} disabled={!canEdit} />
        </SettingsCard>

        <SettingsCard title={t('branding.favicon')} description={t('branding.faviconHint')}>
          <MediaField id="branding-favicon" value={favicon} onChange={setFavicon} placeholder={t('branding.chooseFavicon')} pickerTitle={t('branding.chooseFavicon')} disabled={!canEdit} />
        </SettingsCard>

        {canEdit && (
          <div className="flex flex-wrap items-center justify-end gap-3">
            {makesPublic && (
              <FieldDescription className="mr-auto">{t('branding.makesPublic')}</FieldDescription>
            )}
            <Button type="submit" disabled={!dirty || update.isPending || !appName.trim()}>
              {update.isPending && <Spinner />}
              {t('actions.saveChanges', { ns: 'common' })}
            </Button>
          </div>
        )}
      </div>

      {/* Ζωντανή προεπισκόπηση */}
      <SettingsCard title={t('branding.preview')} className="2xl:sticky 2xl:top-24">
        <div className="space-y-3" aria-label={t('branding.previewLabel')}>
          <PreviewLabel>{t('branding.browserTab')}</PreviewLabel>
          <div className="flex items-center gap-2 rounded-t-lg border border-b-0 bg-muted/60 px-3 py-2 text-xs">
            <img src={faviconSrc ?? logoUrl} alt="" className="size-4 rounded-sm object-contain" />
            <span className="truncate">{appName || ' '}</span>
          </div>

          <PreviewLabel>{t('branding.sidebar')}</PreviewLabel>
          <div className="flex h-14 items-center gap-2.5 rounded-lg border bg-sidebar px-3.5">
            <img src={logoSrc ?? logoUrl} alt="" className="h-7 w-auto max-w-[120px] object-contain" />
            <span className="truncate font-semibold">{appName}</span>
          </div>

          <PreviewLabel>{t('branding.publicPages')}</PreviewLabel>
          <div className="flex h-14 items-center gap-2.5 rounded-lg border border-black/10 bg-[#fafaf9] px-3.5 font-semibold text-[#18181b]">
            {logoSrc ? (
              <img src={logoSrc} alt="" className="h-7 w-auto max-w-[120px] object-contain" />
            ) : (
              <span className="inline-flex size-7 items-center justify-center rounded-lg bg-[#18181b]">
                <img src={logoUrl} alt="" className="h-5" />
              </span>
            )}
            <span className="truncate">{appName}</span>
          </div>
        </div>
      </SettingsCard>
    </form>
  )
}

function PreviewLabel({ children }: { children: string }) {
  return <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{children}</p>
}
