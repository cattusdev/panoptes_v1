import { useState, type FormEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router'
import { CircleAlert, Loader2 } from 'lucide-react'
import { AuthEyebrow, AuthHeading, AuthLayout } from '@/components/layout/AuthLayout'
import { PasswordInput } from '@/components/form/PasswordInput'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { homePath, safeReturnUrl, useLogin, useSession, type SessionUser } from '@/lib/auth'

const fieldClass = 'h-11 rounded-lg bg-card px-3.5'

export function LoginPage() {
  const { t } = useTranslation('auth')
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const { user, isPending: sessionPending } = useSession()
  const login = useLogin()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [rememberMe, setRememberMe] = useState(false)
  const [error, setError] = useState<'invalid' | 'notAllowed' | 'unexpected' | null>(null)

  // Το cookie middleware στέλνει "ReturnUrl", εμείς "returnUrl"
  const returnUrl = safeReturnUrl(params.get('returnUrl') ?? params.get('ReturnUrl'))

  // Χωρίς returnUrl: όποιος έχει πρόσβαση πάει στο dashboard, οι υπόλοιποι στον λογαριασμό τους
  const destinationFor = (u: SessionUser | null) => returnUrl ?? homePath(u)

  if (!sessionPending && user) return <Navigate to={destinationFor(user)} replace />

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      const { result, user: signedIn } = await login.mutateAsync({ email, password, rememberMe })
      switch (result) {
        case 'success':
          navigate(destinationFor(signedIn), { replace: true })
          break
        case 'lockedOut':
          navigate('/lockout', { replace: true })
          break
        case 'requiresTwoFactor': {
          const query = new URLSearchParams({ rememberMe: String(rememberMe) })
          if (returnUrl) query.set('returnUrl', returnUrl)
          navigate(`/login/2fa?${query}`, { replace: true })
          break
        }
        default:
          setError(result === 'notAllowed' ? 'notAllowed' : 'invalid')
      }
    } catch {
      setError('unexpected')
    }
  }

  return (
    // Λογαριασμούς και νέους κωδικούς δίνει μόνο ο διαχειριστής (χωρίς εγγραφή ή "Ξέχασα τον κωδικό")
    <AuthLayout below={t('login.askAdmin')}>
      <AuthEyebrow>{t('login.eyebrow')}</AuthEyebrow>
      <AuthHeading title={t('login.title')} description={t('login.description')} />

      {error && (
        <Alert variant="error" className="mb-5 py-2.5">
          <CircleAlert />
          <AlertDescription>
            {error === 'invalid' && t('login.invalid')}
            {error === 'unexpected' && t('errors.generic', { ns: 'common' })}
            {error === 'notAllowed' && (
              <Trans
                t={t}
                i18nKey="login.notAllowed"
                components={{ link: <Link to="/resend-confirmation" className="font-medium underline underline-offset-2" /> }}
              />
            )}
          </AlertDescription>
        </Alert>
      )}

      <form onSubmit={onSubmit} className="grid gap-4">
        <div className="grid gap-2">
          <Label htmlFor="email">{t('emailLabel')}</Label>
          <Input
            id="email"
            type="email"
            autoComplete="username"
            autoFocus
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-invalid={error === 'invalid' || undefined}
            className={fieldClass}
          />
        </div>

        <div className="grid gap-2">
          <Label htmlFor="password">{t('passwordLabel')}</Label>
          <PasswordInput
            id="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={error === 'invalid' || undefined}
            className={fieldClass}
          />
        </div>

        <div className="mt-1 mb-2 flex items-center gap-2">
          <Checkbox id="remember" checked={rememberMe} onCheckedChange={(v) => setRememberMe(v === true)} />
          <Label htmlFor="remember" className="font-normal">
            {t('login.rememberMe')}
          </Label>
        </div>

        <Button
          id="login-submit"
          type="submit"
          size="lg"
          className="h-11 w-full font-semibold"
          disabled={login.isPending}
        >
          {login.isPending ? (
            <>
              <Loader2 className="animate-spin" /> {t('login.submitting')}
            </>
          ) : (
            t('login.submit')
          )}
        </Button>
      </form>
    </AuthLayout>
  )
}
