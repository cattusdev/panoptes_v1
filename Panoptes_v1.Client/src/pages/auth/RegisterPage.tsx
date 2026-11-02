import { useState, type FormEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link, Navigate } from 'react-router'
import { CircleAlert } from 'lucide-react'
import { PasswordInput } from '@/components/form/PasswordInput'
import { PasswordRequirements } from '@/components/form/PasswordRequirements'
import { usePasswordValid } from '@/lib/password'
import { AuthEyebrow, AuthHeading, AuthLayout, authButtonClass, authInputClass } from '@/components/layout/AuthLayout'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { useRegister, useResendConfirmation } from '@/lib/account-api'
import { fieldErrors, problemMessage } from '@/lib/api'
import { useSession } from '@/lib/auth'
import { CheckYourEmail } from './AuthStatus'

export function RegisterPage() {
  const { t } = useTranslation('auth')
  const { user } = useSession()
  const register = useRegister()
  const resend = useResendConfirmation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [sent, setSent] = useState(false)
  const passwordValid = usePasswordValid(password)

  if (user) return <Navigate to="/account" replace />

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setErrors({})
    setError(null)
    if (!passwordValid) {
      setErrors({ password: t('passwordNotValid') })
      return
    }
    try {
      await register.mutateAsync({ email, password })
      setSent(true)
    } catch (err) {
      const byField = fieldErrors(err)
      if (Object.keys(byField).length > 0) setErrors(byField)
      else setError(problemMessage(err))
    }
  }

  if (sent) {
    return (
      <AuthLayout below={<Link to="/login" className="font-semibold text-foreground">{t('backToSignIn')}</Link>}>
        <CheckYourEmail
          email={email}
          message={t('register.sentMessage')}
          onResend={() => resend.mutateAsync(email)}
          footer={
            <button type="button" className="text-muted-foreground hover:text-foreground" onClick={() => setSent(false)}>
              {t('register.differentEmail')}
            </button>
          }
        />
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      below={
        <Trans t={t} i18nKey="register.haveAccount" components={{ link: <Link to="/login" className="font-semibold text-foreground" /> }} />
      }
    >
      <AuthEyebrow>{t('register.eyebrow')}</AuthEyebrow>
      <AuthHeading title={t('register.title')} description={t('register.description')} />

      {error && (
        <Alert variant="error" className="mb-5 py-2.5">
          <CircleAlert />
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}

      <form onSubmit={onSubmit}>
        <FieldGroup className="gap-4">
          <Field data-invalid={!!errors.email || undefined}>
            <FieldLabel htmlFor="email">{t('emailLabel')}</FieldLabel>
            <Input
              id="email"
              type="email"
              autoComplete="username"
              autoFocus
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              aria-invalid={!!errors.email || undefined}
              className={authInputClass}
            />
            <FieldError>{errors.email}</FieldError>
          </Field>

          <Field data-invalid={!!errors.password || undefined}>
            <FieldLabel htmlFor="password">{t('passwordLabel')}</FieldLabel>
            <PasswordInput
              id="password"
              autoComplete="new-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-invalid={!!errors.password || undefined}
              className={authInputClass}
            />
            <PasswordRequirements password={password} />
            <FieldError>{errors.password}</FieldError>
          </Field>

          <Button id="register-submit" type="submit" size="lg" className={`${authButtonClass} mt-2`} disabled={register.isPending}>
            {register.isPending && <Spinner />}
            {t('register.submit')}
          </Button>
        </FieldGroup>
      </form>
    </AuthLayout>
  )
}
