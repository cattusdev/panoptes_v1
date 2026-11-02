import { useTranslation } from 'react-i18next'
import { Outlet, useLocation } from 'react-router'
import { PageHeader } from '@/components/admin/PageHeader'
import { SettingsNav } from '@/components/admin/SettingsNav'
import { activeSection, settingsSections } from './sections'

// Ρυθμίσεις της εφαρμογής (όπως GitHub / Linear / Stripe settings): ενότητες αριστερά, η ενότητα δεξιά
export function SettingsLayout() {
  const { t } = useTranslation('settings')
  const section = activeSection(useLocation().pathname)
  const items = settingsSections.map((item) => ({ ...item, label: t(`sections.${item.id}.label`) }))

  return (
    <div className="max-w-6xl">
      <PageHeader title={t('title')} description={t('description')} />
      <div className="grid gap-6 md:grid-cols-[200px_minmax(0,1fr)] md:items-start md:gap-8">
        <SettingsNav label={t('sectionsNav')} items={items} />
        <section className="min-w-0" aria-labelledby="settings-section-title">
          {section && (
            <div className="mb-5">
              <h2 id="settings-section-title" className="text-lg font-semibold tracking-tight">
                {t(`sections.${section.id}.label`)}
              </h2>
              <p className="mt-0.5 text-sm text-muted-foreground">{t(`sections.${section.id}.description`)}</p>
            </div>
          )}
          <Outlet />
        </section>
      </div>
    </div>
  )
}
