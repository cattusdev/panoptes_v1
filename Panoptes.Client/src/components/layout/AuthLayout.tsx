import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Check } from 'lucide-react'
import { Card, CardContent } from '@/components/ui/card'
import { Toaster } from '@/components/ui/sonner'
import { BrandLockup, Logo } from '@/components/brand/Logo'
import { LanguageSwitcher } from '@/components/common/LanguageSwitcher'
import { useBranding } from '@/lib/branding'
import { useThemeMode } from '@/hooks/use-theme-mode'

const highlights = ['highlight1', 'highlight2', 'highlight3'] as const

// Για το © στο footer
const year = new Date().getFullYear()

// Split layout για login & account σελίδες: φόρμα αριστερά, σκούρο brand panel δεξιά
export function AuthLayout({ children, below }: { children: ReactNode; below?: ReactNode }) {
  const { t } = useTranslation()
  const { appName } = useBranding()
  useThemeMode('light')

  return (
    <>
      <div className="grid min-h-svh lg:grid-cols-[1.15fr_1fr]">
        <main className="flex items-center justify-center px-4 py-12">
          <div className="w-full max-w-[408px]">
            <a href="/" className="mb-7 inline-flex items-center gap-2.5 font-semibold">
              <BrandLockup />
            </a>

            <Card className="gap-0 rounded-xl py-0 shadow-[0_1px_2px_rgb(16_24_40/0.04),0_8px_24px_-12px_rgb(16_24_40/0.08)] ring-border">
              <CardContent className="p-7">{children}</CardContent>
            </Card>

            {below && <div className="mt-6 text-center text-sm text-muted-foreground">{below}</div>}

            <div className="mt-5 flex items-center justify-center gap-1 text-xs text-muted-foreground/80">
              <span>
                © {year} {appName} ·{' '}
                <a href="/" className="font-medium text-foreground">
                  {t('authLayout.home')}
                </a>
              </span>
              <span aria-hidden>·</span>
              <LanguageSwitcher className="-ml-1" />
            </div>
          </div>
        </main>

        <aside className="bg-brand-panel hidden items-center px-[5vw] py-16 text-[#c9c9ce] lg:flex">
          <div className="max-w-[470px]">
            <Logo className="h-16" />
            <p className="mt-6 mb-3.5 text-xs font-semibold tracking-[0.1em] text-[#9d9da6] uppercase">{t('authLayout.eyebrow')}</p>
            <h2 className="mb-4 text-4xl font-semibold tracking-tight text-[#7f5a6c] xl:text-[2.6rem]">{t('authLayout.title')}</h2>
            <p className="mb-6 leading-relaxed">{t('authLayout.text')}</p>
            <div className="mb-6 h-px bg-[#232326]" />
            <ul className="grid gap-3 text-[0.92rem]">
              {highlights.map((key) => (
                <li key={key} className="flex items-center gap-2.5">
                  <Check className="size-4 text-zinc-300" />
                  {t(`authLayout.${key}`)}
                </li>
              ))}
            </ul>
          </div>
        </aside>
      </div>
      {/* Εκτός grid: αλλιώς γίνεται τρίτο στοιχείο και σπάει τις στήλες */}
      <Toaster theme="light" />
    </>
  )
}

// Κοινό στυλ για τα inputs των auth σελίδων (ψηλότερα, όπως στο design)
export const authInputClass = 'h-11 rounded-lg bg-card px-3.5'
export const authButtonClass = 'h-11 w-full font-semibold'

// Το μικρό "pill" πάνω από τους τίτλους (π.χ. ADMIN CONSOLE)
export function AuthEyebrow({ children }: { children: ReactNode }) {
  return (
    <span className="mb-4 inline-block rounded-full bg-accent px-3 py-1 text-[0.68rem] font-semibold tracking-[0.06em] text-accent-foreground uppercase">
      {children}
    </span>
  )
}

export function AuthHeading({ title, description }: { title: string; description?: ReactNode }) {
  return (
    <div className="mb-6">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      {description && <p className="mt-1.5 text-sm text-muted-foreground">{description}</p>}
    </div>
  )
}
