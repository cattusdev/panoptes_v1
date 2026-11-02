import logoUrl from '@/assets/logo.svg'
import { useBranding } from '@/lib/branding'
import { cn } from '@/lib/utils'

// Το λογότυπο σε σκούρο "app icon" πλακίδιο (για φωτεινά backgrounds).
// Με δικό σας λογότυπο (Settings → Branding) εμφανίζεται όπως είναι.
export function LogoMark({ className }: { className?: string }) {
  const branding = useBranding()
  if (branding.logoUrl) {
    return <img src={branding.logoUrl} alt="" className={cn('h-8 w-auto max-w-[140px] object-contain', className)} />
  }
  return (
    <span className={cn('inline-flex size-8 items-center justify-center rounded-lg bg-primary', className)}>
      <img src={logoUrl} alt="" className="h-[23px]" />
    </span>
  )
}

// Σκέτο λογότυπο (για σκούρα backgrounds)
export function Logo({ className }: { className?: string }) {
  const branding = useBranding()
  return (
    <img
      src={branding.logoUrl ?? logoUrl}
      alt={branding.appName}
      className={cn('h-8', branding.logoUrl && 'w-auto max-w-[140px] object-contain', className)}
    />
  )
}

// Λογότυπο + όνομα εφαρμογής (header, footer, σελίδες λογαριασμού)
export function BrandLockup() {
  const { appName } = useBranding()
  return (
    <>
      <LogoMark />
      <span data-testid="app-name">{appName}</span>
    </>
  )
}
