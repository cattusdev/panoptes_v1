import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { MailCheck } from 'lucide-react'
import { AuthHeading, authButtonClass } from '@/components/layout/AuthLayout'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { useCooldown } from '@/hooks/use-cooldown'
import { problemMessage } from '@/lib/api'
import { cn } from '@/lib/utils'

// Εικονίδιο κατάστασης για τις auth σελίδες (επιτυχία, λάθος, ουδέτερο)
export function StatusIcon({ children, tone }: { children: ReactNode; tone: 'success' | 'danger' | 'neutral' }) {
  return (
    <span
      className={cn(
        'mb-4 inline-flex size-11 items-center justify-center rounded-xl [&_svg]:size-5',
        tone === 'success' && 'bg-success/10 text-success',
        tone === 'danger' && 'bg-destructive/8 text-destructive',
        tone === 'neutral' && 'bg-accent text-accent-foreground',
      )}
    >
      {children}
    </span>
  )
}

// "Check your email" με κουμπί επαναποστολής (με cooldown για να μη γίνεται spam)
export function CheckYourEmail({
  email,
  message,
  onResend,
  footer,
}: {
  email: string
  message: string
  onResend: () => Promise<unknown>
  footer?: ReactNode
}) {
  const { t } = useTranslation('auth')
  const cooldown = useCooldown(30)

  async function resend() {
    try {
      await onResend()
      cooldown.start()
      toast.success(t('checkEmail.sent'))
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  return (
    <>
      <StatusIcon tone="success">
        <MailCheck />
      </StatusIcon>
      <AuthHeading
        title={t('checkEmail.title')}
        description={
          <>
            {message} <span className="font-medium text-foreground">{email}</span>
          </>
        }
      />
      <p className="mb-5 text-sm text-muted-foreground">{t('checkEmail.hint')}</p>
      <Button variant="outline" size="lg" className={authButtonClass} onClick={resend} disabled={cooldown.active}>
        {cooldown.active ? t('checkEmail.resendIn', { seconds: cooldown.remaining }) : t('checkEmail.resend')}
      </Button>
      {footer && <div className="mt-3 text-center text-sm">{footer}</div>}
    </>
  )
}

export function PendingStatus({ title }: { title: string }) {
  return (
    <div className="flex items-center gap-3 py-6 text-muted-foreground">
      <Spinner /> {title}
    </div>
  )
}
