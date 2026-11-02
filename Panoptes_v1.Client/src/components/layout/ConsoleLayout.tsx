import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, Outlet, useLocation } from 'react-router'
import { Loader2, PanelLeft } from 'lucide-react'
import { AdminSidebar } from '@/components/layout/AdminSidebar'
import { CommandPalette } from '@/components/layout/CommandPalette'
import { UserMenu } from '@/components/layout/UserMenu'
import { Button } from '@/components/ui/button'
import { Toaster } from '@/components/ui/sonner'
import { useThemeMode } from '@/hooks/use-theme-mode'
import { canAccessAdmin, useSession } from '@/lib/auth'
import { cn } from '@/lib/utils'

const storageKey = 'panoptes-v1.sidebar.expanded'

function readExpanded() {
  try {
    return localStorage.getItem(storageKey) === 'true'
  } catch {
    return false
  }
}

// Το console: σκούρο θέμα, δικό μας sidebar, topbar. Χρειάζεται μόνο login
// (το /account είναι για όλους, το /admin προστατεύεται επιπλέον από το RequireAdminArea).
export function ConsoleLayout() {
  useThemeMode('dark')
  const { t } = useTranslation()
  const location = useLocation()
  const { user, isPending } = useSession()
  const [expanded, setExpanded] = useState(readExpanded)
  const [mobileOpen, setMobileOpen] = useState(false)

  if (isPending) {
    return (
      <div className="flex min-h-svh items-center justify-center text-muted-foreground">
        <Loader2 className="size-5 animate-spin" />
      </div>
    )
  }

  if (!user) {
    const returnUrl = encodeURIComponent(location.pathname + location.search)
    return <Navigate to={`/login?returnUrl=${returnUrl}`} replace />
  }

  function toggleSidebar() {
    if (window.matchMedia('(max-width: 767px)').matches) {
      setMobileOpen((open) => !open)
      return
    }
    setExpanded((value) => {
      try {
        localStorage.setItem(storageKey, String(!value))
      } catch {
        // private mode κ.λπ.: απλά δεν το θυμόμαστε
      }
      return !value
    })
  }

  return (
    <div className="min-h-svh">
      {mobileOpen && (
        <div className="fixed inset-0 z-30 bg-black/55 backdrop-blur-[2px] md:hidden" onClick={() => setMobileOpen(false)} />
      )}

      <AdminSidebar expanded={expanded} mobileOpen={mobileOpen} onNavigate={() => setMobileOpen(false)} />

      <div className={cn('pr-2.5 pl-2.5 transition-[padding] duration-250', expanded ? 'md:pl-[268px]' : 'md:pl-[84px]')}>
        <header className="sticky top-2.5 z-20 mt-2.5 flex h-15 items-center justify-between rounded-[14px] border border-border bg-[#1c1c1c] pr-3.5 pl-2.5">
          <div className="flex items-center gap-2">
            <Button variant="ghost" size="icon" onClick={toggleSidebar} aria-label={t('nav.toggleSidebar')}>
              <PanelLeft className="size-[18px] text-muted-foreground" />
            </Button>
            <CommandPalette />
          </div>
          <UserMenu />
        </header>

        <main className="px-0.5 py-6 md:px-3">
          <Outlet />
        </main>
      </div>

      <Toaster theme="dark" />
    </div>
  )
}

// Τα /admin routes: Admin ή έστω ένα permission (ίδιος κανόνας με το AdminArea policy του server)
export function RequireAdminArea() {
  const { user } = useSession()
  return canAccessAdmin(user) ? <Outlet /> : <Navigate to="/access-denied" replace />
}
