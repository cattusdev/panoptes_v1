import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'
import { CircleCheck, CircleX } from 'lucide-react'
import { AuthEyebrow, AuthHeading, AuthLayout, authButtonClass, authInputClass } from '@/components/layout/AuthLayout'
import { Button } from '@/components/ui/button'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { useConfirmEmail, useConfirmEmailChange, useResendConfirmation } from '@/lib/account-api'
import { problemMessage } from '@/lib/api'
import { useSession } from '@/lib/auth'
import { CheckYourEmail, PendingStatus, StatusIcon } from './AuthStatus'

// Τρέχει ένα mutation μία φορά όταν ανοίγει η σελίδα (το StrictMode θα το έτρεχε δύο φορές)
function useRunOnce(run: () => Promise<unknown>) {
  const started = useRef(false)
  const [state, setState] = useState<{ status: 'pending' | 'success' | 'error'; error?: unknown }>({ status: 'pending' })

  useEffect(() => {
    if (started.current) return
    started.current = true
    run().then(
      () => setState({ status: 'success' }),
      (error) => setState({ status: 'error', error }),
    )
  }, [run])

  return state
}

// /confirm-email?userId=...&code=...  (το link του email επιβεβαίωσης)
export function ConfirmEmailPage() {
  const { t } = useTranslation('auth')
  const [params] = useSearchParams()
  const confirm = useConfirmEmail()
  const userId = params.get('userId') ?? ''
  const code = params.get('code') ?? ''
  const [run] = useState(() => () => confirm.mutateAsync({ userId, code }))
  const state = useRunOnce(run)

  return (
    <AuthLayout>
      {state.status === 'pending' && <PendingStatus title={t('confirmEmail.pending')} />}
      {state.status === 'success' && (
        <>
          <StatusIcon tone="success">
            <CircleCheck />
          </StatusIcon>
          <AuthHeading title={t('confirmEmail.successTitle')} description={t('confirmEmail.successText')} />
          <Button asChild size="lg" className={authButtonClass}>
            <Link to="/login">{t('continueToSignIn')}</Link>
          </Button>
        </>
      )}
      {state.status === 'error' && (
        <>
          <StatusIcon tone="danger">
            <CircleX />
          </StatusIcon>
          <AuthHeading title={t('confirmEmail.errorTitle')} description={problemMessage(state.error)} />
          <Button asChild size="lg" className={authButtonClass}>
            <Link to="/resend-confirmation">{t('confirmEmail.newLink')}</Link>
          </Button>
        </>
      )}
    </AuthLayout>
  )
}

// /confirm-email-change?userId=...&email=...&code=...
export function ConfirmEmailChangePage() {
  const { t } = useTranslation('auth')
  const [params] = useSearchParams()
  const { user } = useSession()
  const confirm = useConfirmEmailChange()
  const email = params.get('email') ?? ''
  const [run] = useState(() => () => confirm.mutateAsync({ userId: params.get('userId') ?? '', email, code: params.get('code') ?? '' }))
  const state = useRunOnce(run)

  return (
    <AuthLayout>
      {state.status === 'pending' && <PendingStatus title={t('confirmEmailChange.pending')} />}
      {state.status === 'success' && (
        <>
          <StatusIcon tone="success">
            <CircleCheck />
          </StatusIcon>
          <AuthHeading
            title={t('confirmEmailChange.successTitle')}
            description={
              <Trans
                t={t}
                i18nKey="confirmEmailChange.successText"
                values={{ email }}
                components={{ email: <span className="font-medium text-foreground" /> }}
              />
            }
          />
          <Button asChild size="lg" className={authButtonClass}>
            <Link to={user ? '/account' : '/login'}>{user ? t('backToAccount') : t('continueToSignIn')}</Link>
          </Button>
        </>
      )}
      {state.status === 'error' && (
        <>
          <StatusIcon tone="danger">
            <CircleX />
          </StatusIcon>
          <AuthHeading title={t('confirmEmailChange.errorTitle')} description={problemMessage(state.error)} />
          <Button asChild size="lg" className={authButtonClass}>
            <Link to="/account">{t('backToAccount')}</Link>
          </Button>
        </>
      )}
    </AuthLayout>
  )
}

// /resend-confirmation
export function ResendConfirmationPage() {
  const { t } = useTranslation('auth')
  const resend = useResendConfirmation()
  const [email, setEmail] = useState('')
  const [sent, setSent] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    await resend.mutateAsync(email)
    setSent(true)
  }

  return (
    <AuthLayout below={<Link to="/login" className="font-semibold text-foreground">{t('backToSignIn')}</Link>}>
      {sent ? (
        <CheckYourEmail
          email={email}
          message={t('resendConfirmation.sentMessage')}
          onResend={() => resend.mutateAsync(email)}
        />
      ) : (
        <>
          <AuthEyebrow>{t('resendConfirmation.eyebrow')}</AuthEyebrow>
          <AuthHeading title={t('resendConfirmation.title')} description={t('resendConfirmation.description')} />
          <form onSubmit={onSubmit} className="grid gap-5">
            <Field>
              <FieldLabel htmlFor="email">{t('emailLabel')}</FieldLabel>
              <Input
                id="email"
                type="email"
                autoComplete="username"
                autoFocus
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className={authInputClass}
              />
            </Field>
            <Button type="submit" size="lg" className={authButtonClass} disabled={resend.isPending}>
              {resend.isPending && <Spinner />}
              {t('resendConfirmation.submit')}
            </Button>
          </form>
        </>
      )}
    </AuthLayout>
  )
}
