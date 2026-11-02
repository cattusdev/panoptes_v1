import { Outlet } from 'react-router'
import { useTranslation } from 'react-i18next'
import { PageHeader, RouteTabs } from '@/components/admin/PageHeader'

// Ρυθμίσεις λογαριασμού (όπως Vercel / GitHub settings): ένα header, tabs ανά ενότητα
export function AccountLayout() {
  const { t } = useTranslation('account')
  const tabs = [
    { to: '/account', label: t('layout.tabs.profile'), end: true },
    { to: '/account/security', label: t('layout.tabs.security') },
    { to: '/account/data', label: t('layout.tabs.data') },
  ]

  return (
    <div className="max-w-4xl">
      <PageHeader title={t('layout.title')} description={t('layout.description')}>
        <RouteTabs label={t('layout.tabsLabel')} tabs={tabs} />
      </PageHeader>
      <Outlet />
    </div>
  )
}
