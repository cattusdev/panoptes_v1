import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, NavLink } from 'react-router'
import { ArrowLeft } from 'lucide-react'
import { hasPermission, useSession } from '@/lib/auth'
import { Permissions } from '@/lib/permissions'
import { cn } from '@/lib/utils'

export function PageHeader({
  title,
  description,
  actions,
  back,
  children,
}: {
  title: ReactNode
  description?: ReactNode
  actions?: ReactNode
  back?: { to: string; label: string }
  children?: ReactNode
}) {
  return (
    <div className="mb-6">
      {back && (
        <Link to={back.to} className="mb-3 inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" /> {back.label}
        </Link>
      )}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          {description && <p className="mt-1 text-sm text-muted-foreground">{description}</p>}
        </div>
        {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
      </div>
      {children}
    </div>
  )
}

// Tabs που είναι routes (υπογραμμισμένο το ενεργό), κάτω από ένα PageHeader
export function RouteTabs({ label, tabs }: { label: string; tabs: { to: string; label: string; end?: boolean }[] }) {
  return (
    <nav className="mt-5 flex gap-1 overflow-x-auto border-b" aria-label={label}>
      {tabs.map((tab) => (
        <NavLink
          key={tab.to}
          to={tab.to}
          end={tab.end}
          className={({ isActive }) =>
            cn(
              '-mb-px border-b-2 border-transparent px-3.5 py-2 text-sm font-medium whitespace-nowrap text-muted-foreground hover:text-foreground',
              isActive && 'border-muted-foreground text-foreground',
            )
          }
        >
          {tab.label}
        </NavLink>
      ))}
    </nav>
  )
}

// Κοινή κεφαλίδα για Users / Roles (όπως στο reference design: τίτλος + tabs)
export function AccessTabs() {
  const { t } = useTranslation('admin')
  const { user } = useSession()
  const tabs = [
    { to: '/admin/users', label: t('nav.users', { ns: 'common' }), visible: hasPermission(user, Permissions.Users.View) },
    { to: '/admin/roles', label: t('nav.roles', { ns: 'common' }), visible: hasPermission(user, Permissions.Roles.View) },
  ].filter((tab) => tab.visible)

  return (
    <PageHeader title={t('access.title')} description={t('access.description')}>
      <RouteTabs label={t('access.title')} tabs={tabs} />
    </PageHeader>
  )
}
