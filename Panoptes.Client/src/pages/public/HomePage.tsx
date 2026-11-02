import type { ComponentType } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { NotebookText, ShieldCheck, Zap } from 'lucide-react'
import { PublicLayout } from '@/components/layout/PublicLayout'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { currentLanguage } from '@/i18n'
import { canAccessAdmin, useSession } from '@/lib/auth'
import { useBranding } from '@/lib/branding'

// Ενδεικτικά νούμερα για το "preview" του console (marketing, όχι πραγματικά δεδομένα).
// Κρατάμε keys, όχι κείμενα: μεταφράζονται στο render (η γλώσσα αλλάζει)
const previewTiles = [
  { label: 'users', value: 1284, status: 'healthy', watch: false },
  { label: 'roles', value: 12, status: 'synced', watch: false },
  { label: 'auditEvents', value: 3906, status: 'tracked', watch: false },
  { label: 'failedLogins', value: 4, status: 'watch', watch: true },
] as const

const features: { icon: ComponentType<{ className?: string }>; key: 'security' | 'fast' | 'audit' }[] = [
  { icon: ShieldCheck, key: 'security' },
  { icon: Zap, key: 'fast' },
  { icon: NotebookText, key: 'audit' },
]

export function HomePage() {
  const { t } = useTranslation('auth')
  const { appName } = useBranding()
  const { user } = useSession()
  const numberFormat = new Intl.NumberFormat(currentLanguage())

  return (
    <PublicLayout>
      <section className="mx-auto max-w-6xl px-4 pt-24 pb-16 text-center">
        <span className="mb-6 inline-block rounded-full bg-accent px-3 py-1.5 text-[0.7rem] font-semibold tracking-[0.06em] text-accent-foreground uppercase">
          {t('home.version')}
        </span>
        <h1 className="text-[clamp(2.4rem,5vw,3.6rem)] leading-[1.08] font-semibold tracking-[-0.035em]">
          {t('home.titleLine1')}
          <br />
          <span className="text-brand">{t('home.titleLine2')}</span>
        </h1>
        <p className="mx-auto mt-5 mb-9 max-w-xl text-lg text-muted-foreground">{t('home.lead', { appName })}</p>
        <div className="flex justify-center gap-2">
          {user ? (
            <Button asChild size="lg" className="h-11 px-6">
              <Link to={canAccessAdmin(user) ? '/admin' : '/account'}>{canAccessAdmin(user) ? t('home.goToDashboard') : t('home.goToAccount')}</Link>
            </Button>
          ) : (
            <>
              <Button asChild size="lg" className="h-11 px-6">
                <Link to="/register">{t('home.getStartedFree')}</Link>
              </Button>
              <Button asChild size="lg" variant="outline" className="h-11 bg-card px-6">
                <Link to="/login">{t('home.signIn')}</Link>
              </Button>
            </>
          )}
        </div>

        <div className="bg-brand-panel mx-auto mt-14 max-w-5xl rounded-2xl p-7 text-left shadow-[0_30px_60px_-30px_rgb(10_10_11/0.45)]">
          <span className="text-[0.7rem] font-semibold tracking-[0.1em] text-[#9d9da6] uppercase">{t('home.preview.eyebrow')}</span>
          <h2 className="mt-1.5 mb-5 text-2xl font-semibold text-white">{t('home.preview.title')}</h2>
          <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
            {previewTiles.map((tile) => (
              <div key={tile.label} className="rounded-xl border border-white/8 bg-white/4 px-4.5 py-4">
                <div className="text-[0.68rem] tracking-[0.1em] text-[#9d9da6] uppercase">{t(`home.preview.${tile.label}`)}</div>
                <div className="mt-1 text-2xl font-semibold text-white">{numberFormat.format(tile.value)}</div>
                <Badge dot className={tile.watch ? 'mt-1.5 bg-yellow-500/12 text-yellow-400' : 'mt-1.5 bg-green-500/12 text-green-400'}>
                  {t(`home.preview.${tile.status}`)}
                </Badge>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section id="features" className="mx-auto max-w-6xl scroll-mt-24 px-4 py-12">
        <div className="grid gap-4 md:grid-cols-3">
          {features.map(({ icon: Icon, key }) => (
            <Card key={key} className="gap-0 rounded-xl p-7 ring-border transition-shadow hover:shadow-[0_12px_28px_-16px_rgb(16_24_40/0.18)]">
              <span className="mb-4 inline-flex size-11 items-center justify-center rounded-xl bg-accent text-accent-foreground">
                <Icon className="size-5" />
              </span>
              <h3 className="mb-2 text-lg font-semibold">{t(`home.features.${key}.title`)}</h3>
              <p className="text-muted-foreground">{t(`home.features.${key}.text`)}</p>
            </Card>
          ))}
        </div>
      </section>
    </PublicLayout>
  )
}
