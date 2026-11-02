import { Landmark, Mail, Palette, SlidersHorizontal } from 'lucide-react'
import type { SettingsNavItem } from '@/components/admin/SettingsNav'
import { Permissions } from '@/lib/permissions'

// Οι ενότητες του Settings. Νέα ενότητα = μια γραμμή εδώ + ένα route στο router.tsx
// + label/description στο settings:sections.<id> (en και el).
export type SettingsSection = Omit<SettingsNavItem, 'label'> & { id: 'general' | 'branding' | 'email' | 'finance' }

export const settingsSections: SettingsSection[] = [
  { id: 'general', to: '/admin/settings', end: true, icon: SlidersHorizontal, permission: Permissions.Settings.View },
  { id: 'branding', to: '/admin/settings/branding', icon: Palette, permission: Permissions.Settings.View },
  { id: 'email', to: '/admin/settings/email', icon: Mail, permission: Permissions.Settings.View },
  { id: 'finance', to: '/admin/settings/finance', icon: Landmark, permission: Permissions.Settings.View },
]

export function activeSection(pathname: string) {
  const path = pathname.replace(/\/+$/, '')
  return settingsSections.find((section) => (section.end ? path === section.to : path === section.to || path.startsWith(`${section.to}/`)))
}
