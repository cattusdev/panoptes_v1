import { useState, type FormEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'
import { CircleAlert, CircleCheck } from 'lucide-react'
import { PasswordInput } from '@/components/form/PasswordInput'
import { PasswordRequirements } from '@/components/form/PasswordRequirements'
import { usePasswordValid } from '@/lib/password'
import { AuthEyebrow, AuthHeading, AuthLayout, authButtonClass, authInputClass } from '@/components/layout/AuthLayout'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { useForgotPassword, useResetPassword } from '@/lib/account-api'
import { fieldErrors, problemMessage } from '@/lib/api'
import { CheckYourEmail, StatusIcon } from './AuthStatus'

function BackToSignIn() {
  const { t } = useTranslation('auth')
  return (
    <Link to="/login" className="font-semibold text-foreground">
      {t('backToSignIn')}
    </Link>
  )
}

// /forgot-password: ίδιο μήνυμα είτε υπάρχει ο λογαριασμός είτε όχι
export function ForgotPasswordPage() {
  const { t } = useTranslation('auth')
  const forgot = useForgotPassword()
  const [email, setEmail] = useState('')
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      await forgot.mutateAsync(email)
      setSent(true)
    } catch (err) {
      setError(problemMessage(err))
    }
  }

  return (
    <AuthLayout below={<BackToSignIn />}>
      {sent ? (
        <CheckYourEmail
          email={email}
          message={t('forgotPassword.sentMessage')}
          onResend={() => forgot.mutateAsync(email)}
        />
      ) : (
        <>
          <AuthEyebrow>{t('forgotPassword.eyebrow')}</AuthEyebrow>
          <AuthHeading title={t('forgotPassword.title')} description={t('forgotPassword.description')} />
          {error && (
            <Alert variant="error" className="mb-5 py-2.5">
              <CircleAlert />
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
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
            <Button type="submit" size="lg" className={authButtonClass} disabled={forgot.isPending}>
              {forgot.isPending && <Spinner />}
              {t('forgotPassword.submit')}
            </Button>
          </form>
        </>
      )}
    </AuthLayout>
  )
}

// /reset-password?email=...&code=...  (το link του email)
export function ResetPasswordPage() {
  const { t } = useTranslation('auth')
  const [params] = useSearchParams()
  const reset = useResetPassword()
  const email = params.get('email') ?? ''
  const code = params.get('code') ?? ''
  const [password, setPassword] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [linkError, setLinkError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const passwordValid = usePasswordValid(password)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setFieldError(null)
    if (!passwordValid) {
      setFieldError(t('passwordNotValid'))
      return
    }
    try {
      await reset.mutateAsync({ email, code, password })
      setDone(true)
    } catch (err) {
      const byField = fieldErrors(err)
      if (byField.password) setFieldError(byField.password)
      else setLinkError(problemMessage(err))
    }
  }

  if (done) {
    return (
      <AuthLayout>
        <StatusIcon tone="success">
          <CircleCheck />
        </StatusIcon>
        <AuthHeading title={t('resetPassword.doneTitle')} description={t('resetPassword.doneText')} />
        <Button asChild size="lg" className={authButtonClass}>
          <Link to="/login">{t('continueToSignIn')}</Link>
        </Button>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout below={<BackToSignIn />}>
      <AuthEyebrow>{t('resetPassword.eyebrow')}</AuthEyebrow>
      <AuthHeading
        title={t('resetPassword.title')}
        description={
          email ? (
            <Trans t={t} i18nKey="resetPassword.for" values={{ email }} components={{ email: <span className="font-medium text-foreground" /> }} />
          ) : undefined
        }
      />

      {(linkError || !email || !code) && (
        <Alert variant="error" className="mb-5 py-2.5">
          <CircleAlert />
          <AlertDescription>
            {linkError ?? t('resetPassword.incomplete')}{' '}
            <Link to="/forgot-password" className="font-medium underline underline-offset-2">
              {t('resetPassword.requestNew')}
            </Link>
          </AlertDescription>
        </Alert>
      )}

      <form onSubmit={onSubmit} className="grid gap-5">
        <Field data-invalid={!!fieldError || undefined}>
          <FieldLabel htmlFor="password">{t('resetPassword.newPassword')}</FieldLabel>
          <PasswordInput
            id="password"
            autoComplete="new-password"
            autoFocus
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={!!fieldError || undefined}
            className={authInputClass}
          />
          <PasswordRequirements password={password} />
          <FieldError>{fieldError}</FieldError>
        </Field>
        <Button type="submit" size="lg" className={authButtonClass} disabled={reset.isPending || !email || !code}>
          {reset.isPending && <Spinner />}
          {t('resetPassword.submit')}
        </Button>
      </form>
    </AuthLayout>
  )
}
