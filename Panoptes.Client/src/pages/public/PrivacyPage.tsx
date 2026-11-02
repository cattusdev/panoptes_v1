import { useTranslation } from 'react-i18next'
import { PublicLayout } from '@/components/layout/PublicLayout'

// TODO: το πραγματικό κείμενο της πολιτικής απορρήτου (ήταν placeholder και στην Razor εκδοχή)
export function PrivacyPage() {
  const { t } = useTranslation('auth')

  return (
    <PublicLayout>
      <article className="mx-auto max-w-2xl px-4 pt-20">
        <h1 className="mb-4 text-3xl font-semibold tracking-tight">{t('privacy.title')}</h1>
        <p className="text-muted-foreground">{t('privacy.text')}</p>
      </article>
    </PublicLayout>
  )
}
