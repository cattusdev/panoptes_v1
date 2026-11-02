import { Fragment, type ComponentType } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { House, Images, NotebookText, Settings, Users } from 'lucide-react'
import { Logo } from '@/components/brand/Logo'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { canAccessAdmin, hasPermission, useSession, type SessionUser } from '@/lib/auth'
import { useBranding } from '@/lib/branding'
import { Permissions } from '@/lib/permissions'
import { cn } from '@/lib/utils'

type NavRoute = { to: string; permission?: string }

type NavItem = {
  // Σταθερό αναγνωριστικό (data-title, tests), το κείμενο έρχεται από το common:nav.*
  id: string
  label: 'dashboard' | 'users' | 'auditLogs' | 'media' | 'settings'
  icon: ComponentType<{ className?: string }>
  // Η σελίδα του item και όσες είναι tabs της (π.χ. Roles μέσα στο Users): το item είναι ενεργό
  // σε όλες και οδηγεί στην πρώτη που επιτρέπεται στον χρήστη
  routes: NavRoute[]
  // Μόνο για όσους έχουν πρόσβαση στο admin area (Admin ή έστω ένα permission)
  adminArea?: boolean
}

type NavGroup = { label: 'main' | 'management' | 'content' | 'system'; items: NavItem[] }

const groups: NavGroup[] = [
  { label: 'main', items: [{ id: 'Dashboard', label: 'dashboard', icon: House, routes: [{ to: '/admin' }], adminArea: true }] },
  {
    label: 'management',
    items: [
      {
        id: 'Users',
        label: 'users',
        icon: Users,
        routes: [
          { to: '/admin/users', permission: Permissions.Users.View },
          { to: '/admin/roles', permission: Permissions.Roles.View },
        ],
      },
      { id: 'Audit Logs', label: 'auditLogs', icon: NotebookText, routes: [{ to: '/admin/audit-logs', permission: Permissions.AuditLogs.View }] },
    ],
  },
  {
    label: 'content',
    items: [{ id: 'Media', label: 'media', icon: Images, routes: [{ to: '/admin/media', permission: Permissions.Media.View }] }],
  },
  {
    label: 'system',
    items: [{ id: 'Settings', label: 'settings', icon: Settings, routes: [{ to: '/admin/settings', permission: Permissions.Settings.View }] }],
  },
]

function targetOf(item: NavItem, user: SessionUser | null) {
  if (item.adminArea && !canAccessAdmin(user)) return undefined
  return item.routes.find((route) => !route.permission || hasPermission(user, route.permission))?.to
}

// Το Dashboard (/admin) είναι ενεργό μόνο στο ακριβές path, τα άλλα και στις υποσελίδες τους
function matches(to: string, pathname: string) {
  return to === '/admin' ? pathname === '/admin' : pathname === to || pathname.startsWith(`${to}/`)
}

const itemClass =
  'relative flex items-center gap-2.5 rounded-[10px] px-2.5 py-2 text-sm font-medium whitespace-nowrap text-sidebar-foreground transition-colors hover:bg-white/4 hover:text-sidebar-accent-foreground'

// Το δικό μας sidebar (όχι το shadcn): floating panel, "mini" (μόνο icons) ή πλήρες
export function AdminSidebar({
  expanded,
  mobileOpen,
  onNavigate,
}: {
  expanded: boolean
  mobileOpen: boolean
  onNavigate: () => void
}) {
  const { t } = useTranslation()
  const { user } = useSession()
  const { appName } = useBranding()
  // Στο κινητό το sidebar ανοίγει πάντα πλήρες
  const mini = !expanded && !mobileOpen

  return (
    <nav
      aria-label={t('nav.main')}
      className={cn(
        'fixed inset-y-2.5 left-2.5 z-40 flex flex-col rounded-[14px] border border-sidebar-border bg-sidebar transition-[width,transform] duration-250 ease-out',
        mini ? 'md:w-16' : 'md:w-62',
        'w-[80%] max-md:-translate-x-[calc(100%+20px)]',
        mobileOpen && 'max-md:translate-x-0',
      )}
    >
      <div className={cn('flex h-15 shrink-0 items-center px-3.5', mini && 'justify-center px-0')}>
        <Logo className="h-[30px]" />
        {!mini && <span className="ml-2.5 truncate font-semibold text-foreground">{appName}</span>}
      </div>

      <div className="flex-1 overflow-y-auto px-2.5 py-1.5">
        {groups.map((group) => {
          const items = group.items.flatMap((item) => {
            const to = targetOf(item, user)
            return to ? [{ item, to }] : []
          })
          if (items.length === 0) return null
          return (
            <Fragment key={group.label}>
              {mini ? (
                <div className="mx-2 my-2.5 h-px bg-sidebar-border first:mt-1" />
              ) : (
                <div className="px-2.5 pt-4 pb-2 text-[0.68rem] font-semibold tracking-[0.08em] text-muted-foreground/70 uppercase">
                  {t(`nav.groups.${group.label}`)}
                </div>
              )}
              <ul className="grid gap-0.5">
                {items.map(({ item, to }) => (
                  <li key={item.id}>
                    <SidebarLink item={item} to={to} mini={mini} onNavigate={onNavigate} />
                  </li>
                ))}
              </ul>
            </Fragment>
          )
        })}
      </div>

      <div className="border-t border-sidebar-border p-3.5 text-center text-xs text-muted-foreground/70">
        {!mini && `${appName} v2.3`}
      </div>
    </nav>
  )
}

function SidebarLink({ item, to, mini, onNavigate }: { item: NavItem; to: string; mini: boolean; onNavigate: () => void }) {
  const { t } = useTranslation()
  const label = t(`nav.${item.label}`)
  const Icon = item.icon
  const { pathname } = useLocation()

  // Το active υπολογίζεται εδώ (string className): το Tooltip asChild δεν συνδυάζεται με
  // το className-as-function του NavLink
  const isActive = item.routes.some((route) => matches(route.to, pathname))

  const link = (
    <Link
      to={to}
      data-title={item.id}
      aria-label={mini ? label : undefined}
      aria-current={isActive ? 'page' : undefined}
      onClick={onNavigate}
      className={cn(itemClass, mini && 'justify-center px-0 py-2.5', isActive && 'bg-sidebar-accent text-sidebar-accent-foreground')}
    >
      <Icon className="size-[18px] shrink-0" />
      {!mini && <span>{label}</span>}
    </Link>
  )

  if (!mini) return link

  return (
    <Tooltip>
      <TooltipTrigger asChild>{link}</TooltipTrigger>
      <TooltipContent side="right" sideOffset={12}>
        {label}
      </TooltipContent>
    </Tooltip>
  )
}
