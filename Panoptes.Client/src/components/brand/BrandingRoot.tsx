import { Outlet } from 'react-router'
import { useLanguageSync } from '@/i18n/useLanguageSync'
import { useBrandingHead } from '@/lib/branding'

// Ρίζα όλων των σελίδων: τίτλος καρτέλας και favicon από το branding, γλώσσα από χρήστη / ρυθμίσεις
export function BrandingRoot() {
  useBrandingHead()
  useLanguageSync()
  return <Outlet />
}
