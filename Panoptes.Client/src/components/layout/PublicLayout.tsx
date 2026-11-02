import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { LayoutGrid } from 'lucide-react'
import { BrandLockup } from '@/components/brand/Logo'
import { LanguageSwitcher } from '@/components/common/LanguageSwitcher'
import { useBranding } from '@/lib/branding'
import { Button } from '@/components/ui/button'
import { Toaster } from '@/components/ui/sonner'
import { useThemeMode } from '@/hooks/use-theme-mode'
import { canAccessAdmin, useLogout, useSession } from '@/lib/auth'

// Για το © στο footer
const year = new Date().getFullYear()

// Δημόσιες σελίδες (αρχική, privacy): φωτεινό θέμα, sticky navbar, footer
export function PublicLayout({ children }: { children: ReactNode }) {
  useThemeMode('light')

  return (
    <div className="flex min-h-svh flex-col">
      <PublicNavbar />
      <main className="flex-1">{children}</main>
      <PublicFooter />
      <Toaster theme="light" />
    </div>
  )
}

function PublicNavbar() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { user, isPending } = useSession()
  const logout = useLogout()

  return (
    <header className="sticky top-0 z-40 border-b bg-background/85 backdrop-blur-md">
      <div className="mx-auto flex h-[72px] max-w-6xl items-center justify-between gap-4 px-4">
        <Link to="/" className="flex items-center gap-2.5 font-semibold">
          <BrandLockup />
        </Link>

        <nav className="hidden items-center gap-1 text-sm sm:flex" aria-label={t('public.nav')}>
          <a href="/#features" className="rounded-md px-3 py-2 text-muted-foreground hover:text-foreground">
            {t('public.features')}
          </a>
          <Link to="/privacy" className="rounded-md px-3 py-2 text-muted-foreground hover:text-foreground">
            {t('public.privacy')}
          </Link>
        </nav>

        {/* Μέχρι να ξέρουμε αν είναι συνδεδεμένος, κρατάμε τον χώρο (χωρίς "αναβόσβημα") */}
        <div className="flex min-w-[180px] items-center justify-end gap-2">
          {isPending ? null : user ? (
            <>
              <Button asChild variant="secondary" size="lg">
                <Link to={canAccessAdmin(user) ? '/admin' : '/account'}>
                  <LayoutGrid /> {canAccessAdmin(user) ? t('nav.dashboard') : t('nav.myAccount')}
                </Link>
              </Button>
              <Button
                variant="ghost"
                size="lg"
                disabled={logout.isPending}
                onClick={async () => {
                  await logout.mutateAsync()
                  navigate('/', { replace: true })
                }}
              >
                {t('userMenu.signOut')}
              </Button>
            </>
          ) : (
            <>
              <Button asChild variant="ghost" size="lg">
                <Link to="/login">{t('public.signIn')}</Link>
              </Button>
              <Button asChild size="lg">
                <Link to="/register">{t('public.getStarted')}</Link>
              </Button>
            </>
          )}
        </div>
      </div>
    </header>
  )
}

function PublicFooter() {
  const { t } = useTranslation()
  const { appName } = useBranding()
  return (
    <footer className="mt-24 border-t bg-card">
      <div className="mx-auto grid max-w-6xl gap-10 px-4 py-14 sm:grid-cols-[2fr_1fr_1fr]">
        <div>
          <div className="mb-3 flex items-center gap-2.5 font-semibold">
            <BrandLockup />
          </div>
          <p className="max-w-sm text-sm text-muted-foreground">{t('public.tagline')}</p>
        </div>
        <FooterColumn title={t('public.product')}>
          <a href="/#features">{t('public.features')}</a>
          <Link to="/login">{t('public.signIn')}</Link>
          <Link to="/register">{t('public.createAccount')}</Link>
        </FooterColumn>
        <FooterColumn title={t('public.legal')}>
          <Link to="/privacy">{t('public.privacy')}</Link>
        </FooterColumn>
      </div>
      <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-3 border-t px-4 py-5 text-xs text-muted-foreground">
        <span>{t('public.rights', { year, appName })}</span>
        <LanguageSwitcher />
      </div>
    </footer>
  )
}

function FooterColumn({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div>
      <div className="mb-3 text-sm font-semibold">{title}</div>
      <div className="grid gap-2 text-sm text-muted-foreground [&_a:hover]:text-foreground">{children}</div>
    </div>
  )
}
