import { Fragment, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { ChevronDown } from 'lucide-react'
import { Logo } from '@/components/brand/Logo'
import { isActive, visibleNav, type NavGroupId, type NavItem } from '@/components/layout/nav'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useSession } from '@/lib/auth'
import { useBranding } from '@/lib/branding'
import { cn } from '@/lib/utils'

const collapsedKey = 'panoptes-v1.sidebar.collapsed'

function readCollapsed(): NavGroupId[] {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(collapsedKey) ?? '[]')
    return Array.isArray(value) ? value : []
  } catch {
    return []
  }
}

function writeCollapsed(groups: NavGroupId[]) {
  try {
    localStorage.setItem(collapsedKey, JSON.stringify(groups))
  } catch {
    // private mode κ.λπ.: απλά δεν το θυμόμαστε
  }
}

const itemClass =
  'relative flex items-center gap-2.5 rounded-[10px] px-2.5 py-2 text-sm font-medium whitespace-nowrap text-sidebar-foreground transition-colors hover:bg-white/4 hover:text-sidebar-accent-foreground'

// Το δικό μας sidebar (όχι το shadcn): floating panel, "mini" (μόνο icons) ή πλήρες.
// Οι ομάδες κλείνουν/ανοίγουν (όπως τα sections του Linear) και το θυμόμαστε.
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
  const { pathname } = useLocation()
  // Στο κινητό το sidebar ανοίγει πάντα πλήρες
  const mini = !expanded && !mobileOpen
  const groups = visibleNav(user)

  const [collapsed, setCollapsed] = useState(readCollapsed)
  // Όταν πας σε σελίδα μιας κλειστής ομάδας (π.χ. από το Ctrl+K), η ομάδα ανοίγει: δεν χάνεις πού βρίσκεσαι
  const [seenPath, setSeenPath] = useState(pathname)
  if (seenPath !== pathname) {
    setSeenPath(pathname)
    const active = groups.find(({ items }) => items.some(({ item }) => isActive(item, pathname)))?.group
    if (active && collapsed.includes(active)) {
      const next = collapsed.filter((group) => group !== active)
      setCollapsed(next)
      writeCollapsed(next)
    }
  }

  function toggle(group: NavGroupId) {
    const next = collapsed.includes(group) ? collapsed.filter((g) => g !== group) : [...collapsed, group]
    setCollapsed(next)
    writeCollapsed(next)
  }

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

      <div className="flex-1 overflow-y-auto px-2.5 py-1.5 pb-3">
        {groups.map(({ group, items }) => {
          // Στο mini δεν υπάρχουν τίτλοι ομάδων: όλα τα icons φαίνονται, με διαχωριστικό ανάμεσα
          const open = mini || !collapsed.includes(group)
          const listId = `nav-group-${group}`
          return (
            <Fragment key={group}>
              {mini ? (
                <div className="mx-2 my-2.5 h-px bg-sidebar-border first:mt-1" />
              ) : (
                <button
                  type="button"
                  aria-expanded={open}
                  aria-controls={listId}
                  data-group={group}
                  onClick={() => toggle(group)}
                  className="group/heading flex w-full items-center gap-1 rounded-md px-2.5 pt-4 pb-2 text-left text-[0.68rem] font-semibold tracking-[0.08em] text-muted-foreground/70 uppercase outline-none hover:text-muted-foreground focus-visible:ring-2 focus-visible:ring-ring"
                >
                  {t(`nav.groups.${group}`)}
                  <ChevronDown
                    className={cn(
                      'size-3 transition-[transform,opacity]',
                      open ? 'opacity-0 group-hover/heading:opacity-100 group-focus-visible/heading:opacity-100' : '-rotate-90',
                    )}
                  />
                </button>
              )}
              <ul id={listId} hidden={!open} className="grid gap-0.5">
                {items.map(({ item, to }) => (
                  <li key={item.id}>
                    <SidebarLink item={item} to={to} active={isActive(item, pathname)} mini={mini} onNavigate={onNavigate} />
                  </li>
                ))}
              </ul>
            </Fragment>
          )
        })}
      </div>
    </nav>
  )
}

function SidebarLink({ item, to, active, mini, onNavigate }: { item: NavItem; to: string; active: boolean; mini: boolean; onNavigate: () => void }) {
  const { t } = useTranslation()
  const label = t(`nav.${item.label}`)
  const Icon = item.icon

  // Το active υπολογίζεται απ' έξω (string className): το Tooltip asChild δεν συνδυάζεται με
  // το className-as-function του NavLink
  const link = (
    <Link
      to={to}
      data-title={item.id}
      aria-label={mini ? label : undefined}
      aria-current={active ? 'page' : undefined}
      onClick={onNavigate}
      className={cn(itemClass, mini && 'justify-center px-0 py-2.5', active && 'bg-sidebar-accent text-sidebar-accent-foreground')}
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
