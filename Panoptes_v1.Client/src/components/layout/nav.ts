import type { ComponentType } from 'react'
import { House, Images, NotebookText, ServerCog, Settings, Users } from 'lucide-react'
import type { Resources } from '@/i18n/locales/en'
import { canAccessAdmin, hasPermission, type SessionUser } from '@/lib/auth'
import { Permissions } from '@/lib/permissions'

type NavKeys = Resources['common']['nav']
// Κείμενα του common:nav.* (μόνο τα απλά strings, όχι τα groups)
export type NavLabel = { [K in keyof NavKeys]: NavKeys[K] extends string ? K : never }[keyof NavKeys]
export type NavGroupId = keyof NavKeys['groups']

// Η σειρά των ομάδων στο sidebar (SCREEN-PLANNING). Μια ομάδα χωρίς ορατά items δεν εμφανίζεται.
export const navGroups: NavGroupId[] = ['overview', 'relationships', 'delivery', 'business', 'workspace', 'administration']

export type NavRoute = {
  to: string
  permission?: string
  // Μόνο για τον Admin (σελίδες χωρίς permission, π.χ. Jobs)
  adminOnly?: boolean
  // Όταν η σελίδα είναι tab του item (π.χ. Roles μέσα στο Users): το όνομά της στο command palette
  label?: NavLabel
}

export type NavItem = {
  // Σταθερό αναγνωριστικό (data-title, tests), το κείμενο έρχεται από το common:nav.*
  id: string
  label: NavLabel
  icon: ComponentType<{ className?: string }>
  group: NavGroupId
  // Η σελίδα του item και όσες είναι tabs της (π.χ. Roles μέσα στο Users): το item είναι ενεργό
  // σε όλες και οδηγεί στην πρώτη που επιτρέπεται στον χρήστη
  routes: NavRoute[]
  // Μόνο για όσους έχουν πρόσβαση στο admin area (Admin ή έστω ένα permission)
  adminArea?: boolean
}

// Το registry της πλοήγησης: sidebar και command palette διαβάζουν από εδώ.
// Νέο module = μια γραμμή εδώ + το label στο common:nav.* (en και el) + το route (με RequirePermission) στο router.tsx.
export const navItems: NavItem[] = [
  { id: 'Dashboard', label: 'dashboard', icon: House, group: 'overview', routes: [{ to: '/admin' }], adminArea: true },
  { id: 'Media', label: 'media', icon: Images, group: 'workspace', routes: [{ to: '/admin/media', permission: Permissions.Media.View }] },
  {
    id: 'Users',
    label: 'users',
    icon: Users,
    group: 'administration',
    routes: [
      { to: '/admin/users', permission: Permissions.Users.View },
      { to: '/admin/roles', permission: Permissions.Roles.View, label: 'roles' },
    ],
  },
  { id: 'Settings', label: 'settings', icon: Settings, group: 'administration', routes: [{ to: '/admin/settings', permission: Permissions.Settings.View }] },
  {
    id: 'Audit Logs',
    label: 'auditLogs',
    icon: NotebookText,
    group: 'administration',
    routes: [{ to: '/admin/audit-logs', permission: Permissions.AuditLogs.View }],
  },
  { id: 'Jobs', label: 'jobs', icon: ServerCog, group: 'administration', routes: [{ to: '/admin/jobs', adminOnly: true }] },
]

export function allowedRoutes(item: NavItem, user: SessionUser | null) {
  if (item.adminArea && !canAccessAdmin(user)) return []
  return item.routes.filter(
    (route) => (!route.adminOnly || !!user?.isAdmin) && (!route.permission || hasPermission(user, route.permission)),
  )
}

// Οι ομάδες με τα items που βλέπει ο χρήστης και πού οδηγεί το καθένα (η πρώτη επιτρεπτή σελίδα του)
export function visibleNav(user: SessionUser | null) {
  return navGroups.flatMap((group) => {
    const items = navItems
      .filter((item) => item.group === group)
      .flatMap((item) => {
        const to = allowedRoutes(item, user)[0]?.to
        return to ? [{ item, to }] : []
      })
    return items.length > 0 ? [{ group, items }] : []
  })
}

// Όλες οι σελίδες που επιτρέπονται στον χρήστη, μαζί με τα tabs (για το command palette), με τη σειρά του sidebar
export function navPages(user: SessionUser | null) {
  return navGroups.flatMap((group) =>
    navItems
      .filter((item) => item.group === group)
      .flatMap((item) => allowedRoutes(item, user).map((route) => ({ to: route.to, label: route.label ?? item.label, icon: item.icon }))),
  )
}

// Το Dashboard (/admin) είναι ενεργό μόνο στο ακριβές path, τα άλλα και στις υποσελίδες τους
export function matches(to: string, pathname: string) {
  return to === '/admin' ? pathname === '/admin' : pathname === to || pathname.startsWith(`${to}/`)
}

export function isActive(item: NavItem, pathname: string) {
  return item.routes.some((route) => matches(route.to, pathname))
}
