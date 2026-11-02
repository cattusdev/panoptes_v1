import { Outlet } from 'react-router'
import { useTranslation } from 'react-i18next'
import { PageHeader, RouteTabs } from '@/components/admin/PageHeader'
import { accountSections } from './sections'

// Ρυθμίσεις λογαριασμού (όπως Vercel / GitHub settings): ένα header, tabs ανά ενότητα
export function AccountLayout() {
  const { t } = useTranslation('account')
  const tabs = accountSections.map((section) => ({ to: section.to, label: t(`layout.tabs.${section.id}`), end: section.end }))

  return (
    <div className="max-w-4xl">
      <PageHeader title={t('layout.title')} description={t('layout.description')}>
        <RouteTabs label={t('layout.tabsLabel')} tabs={tabs} />
      </PageHeader>
      <Outlet />
    </div>
  )
}
