import { useTranslation } from 'react-i18next'
import { isRouteErrorResponse, useNavigate, useRouteError } from 'react-router'
import { Lock, SearchX, ShieldX, TriangleAlert } from 'lucide-react'
import { AuthHeading, AuthLayout, authButtonClass } from '@/components/layout/AuthLayout'
import { Button } from '@/components/ui/button'
import { useLogout, useSession } from '@/lib/auth'
import { StatusIcon } from './AuthStatus'


export function LockoutPage() {
  const { t } = useTranslation('auth')

  return (
    <AuthLayout>
      <StatusIcon tone="danger">
        <Lock />
      </StatusIcon>
      <AuthHeading title={t('lockout.title')} description={t('lockout.text')} />
      <div className="grid gap-2">
        <Button asChild size="lg" className={authButtonClass}>
          <a href="/login">{t('backToSignIn')}</a>
        </Button>
      </div>
    </AuthLayout>
  )
}

export function AccessDeniedPage() {
  const { t } = useTranslation('auth')
  const navigate = useNavigate()
  const { user } = useSession()
  const logout = useLogout()

  return (
    <AuthLayout>
      <StatusIcon tone="danger">
        <ShieldX />
      </StatusIcon>
      <AuthHeading title={t('accessDenied.title')} description={t('accessDenied.text')} />
      <div className="grid gap-2">
        <Button asChild size="lg" className={authButtonClass}>
          <a href="/">{t('returnHome')}</a>
        </Button>
        {user && (
          <Button
            size="lg"
            variant="outline"
            className={authButtonClass}
            disabled={logout.isPending}
            onClick={async () => {
              await logout.mutateAsync()
              navigate('/login', { replace: true })
            }}
          >
            {t('accessDenied.switchAccount')}
          </Button>
        )}
      </div>
    </AuthLayout>
  )
}

// Error boundary των routes: κάτι έσκασε στο React app (όχι στον server)
export function RouteErrorPage() {
  const { t } = useTranslation('auth')
  const error = useRouteError()
  if (isRouteErrorResponse(error) && error.status === 404) return <NotFoundPage />

  return (
    <AuthLayout>
      <StatusIcon tone="danger">
        <TriangleAlert />
      </StatusIcon>
      <AuthHeading title={t('routeError.title')} description={t('routeError.text')} />
      <div className="grid gap-2">
        <Button size="lg" className={authButtonClass} onClick={() => window.location.reload()}>
          {t('routeError.reload')}
        </Button>
        <Button asChild size="lg" variant="outline" className={authButtonClass}>
          <a href="/">{t('returnHome')}</a>
        </Button>
      </div>
    </AuthLayout>
  )
}

export function NotFoundPage() {
  const { t } = useTranslation('auth')

  return (
    <AuthLayout>
      <StatusIcon tone="neutral">
        <SearchX />
      </StatusIcon>
      <AuthHeading title={t('notFound.title')} description={t('notFound.text')} />
      <Button asChild size="lg" className={authButtonClass}>
        <a href="/">{t('returnHome')}</a>
      </Button>
    </AuthLayout>
  )
}
