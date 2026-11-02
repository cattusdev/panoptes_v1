import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, useNavigate, useSearchParams } from 'react-router'
import { CircleAlert, Smartphone } from 'lucide-react'
import { CodeInput } from '@/components/form/CodeInput'
import { AuthHeading, AuthLayout, authButtonClass, authInputClass } from '@/components/layout/AuthLayout'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Spinner } from '@/components/ui/spinner'
import { useTwoFactorLogin, useTwoFactorPending } from '@/lib/account-api'
import { homePath, safeReturnUrl } from '@/lib/auth'
import { StatusIcon } from './AuthStatus'

// /login/2fa: το δεύτερο βήμα του login (όπως στο GitHub): κωδικός από το app ή recovery code
export function TwoFactorPage() {
  const { t } = useTranslation('auth')
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const pending = useTwoFactorPending()
  const login = useTwoFactorLogin()

  const [mode, setMode] = useState<'app' | 'recovery'>('app')
  const [code, setCode] = useState('')
  const [recoveryCode, setRecoveryCode] = useState('')
  const [rememberMachine, setRememberMachine] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const returnUrl = safeReturnUrl(params.get('returnUrl'))
  const rememberMe = params.get('rememberMe') === 'true'

  // Δεν υπάρχει login σε εξέλιξη (π.χ. άνοιξε τη σελίδα απευθείας ή έληξε): πίσω στο /login
  if (pending.isError) return <Navigate to="/login" replace />

  async function submit(value?: string) {
    setError(null)
    const input = mode === 'app' ? { code: value ?? code, rememberMe, rememberMachine } : { recoveryCode }
    try {
      const { result, user } = await login.mutateAsync(input)
      if (result === 'success') navigate(returnUrl ?? homePath(user), { replace: true })
      else if (result === 'lockedOut') navigate('/lockout', { replace: true })
      else if (result === 'expired') navigate('/login', { replace: true })
      else {
        setError(mode === 'app' ? t('twoFactor.invalidCode') : t('twoFactor.invalidRecovery'))
        setCode('')
      }
    } catch {
      setError(t('errors.generic', { ns: 'common' }))
    }
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    void submit()
  }

  function switchMode() {
    setMode((m) => (m === 'app' ? 'recovery' : 'app'))
    setError(null)
  }

  return (
    <AuthLayout
      below={
        <a href="/login" className="font-semibold text-foreground">
          {t('backToSignIn')}
        </a>
      }
    >
      <StatusIcon tone="neutral">
        <Smartphone />
      </StatusIcon>
      <AuthHeading
        title={t('twoFactor.title')}
        description={mode === 'app' ? t('twoFactor.appDescription') : t('twoFactor.recoveryDescription')}
      />

      {error && (
        <Alert variant="error" className="mb-5 py-2.5">
          <CircleAlert />
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}

      <form onSubmit={onSubmit} className="grid gap-5">
        {mode === 'app' ? (
          <>
            <div className="flex justify-center">
              <CodeInput
                value={code}
                onChange={setCode}
                onComplete={(value) => void submit(value)}
                disabled={login.isPending}
                invalid={!!error}
                autoFocus
              />
            </div>
            <div className="flex items-center gap-2">
              <Checkbox id="remember-machine" checked={rememberMachine} onCheckedChange={(v) => setRememberMachine(v === true)} />
              <Label htmlFor="remember-machine" className="font-normal">
                {t('twoFactor.rememberDevice')}
              </Label>
            </div>
          </>
        ) : (
          <Field>
            <FieldLabel htmlFor="recovery-code">{t('twoFactor.recoveryCode')}</FieldLabel>
            <Input
              id="recovery-code"
              autoComplete="off"
              autoFocus
              required
              value={recoveryCode}
              onChange={(e) => setRecoveryCode(e.target.value)}
              className={`${authInputClass} font-mono`}
            />
            <FieldDescription>{t('twoFactor.recoveryHint')}</FieldDescription>
          </Field>
        )}

        <Button
          type="submit"
          size="lg"
          className={authButtonClass}
          disabled={login.isPending || (mode === 'app' ? code.length < 6 : !recoveryCode.trim())}
        >
          {login.isPending && <Spinner />}
          {t('twoFactor.verify')}
        </Button>
      </form>

      <button type="button" onClick={switchMode} className="mt-4 w-full text-center text-sm text-muted-foreground hover:text-foreground">
        {mode === 'app' ? t('twoFactor.useRecovery') : t('twoFactor.useApp')}
      </button>
    </AuthLayout>
  )
}
