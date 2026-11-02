import type { ComponentType, ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Trans, useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Activity, ArrowRight, CircleAlert, CircleCheck, KeyRound, Layers, ListOrdered, ShieldAlert, ShieldCheck, Users } from 'lucide-react'
import { DateTime } from '@/components/admin/DateTime'
import { Alert, AlertAction, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useAuditTypeLabel } from '@/lib/admin-api'
import { api } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import { timeAgo } from '@/lib/format'
import { Permissions } from '@/lib/permissions'
import { cn } from '@/lib/utils'

type Dashboard = {
  metrics: { totalUsers: number; totalRoles: number; administrators: number; auditEvents24h: number }
  security: { usersWithFailedLogins: number; lockedAccounts: number; unconfirmedEmails: number; twoFactorEnabled: number }
  health: { databaseHealthy: boolean; databaseResponseMs: number }
  recentActivity: { id: number; type: string; tableName: string | null; dateTime: string; userEmail: string | null }[] | null
}

const activityBadge: Record<string, 'success' | 'warning' | 'destructive' | 'muted'> = {
  Create: 'success',
  Update: 'warning',
  Delete: 'destructive',
}

const activityDot: Record<string, string> = {
  Create: 'bg-success',
  Update: 'bg-warning',
  Delete: 'bg-destructive',
}

const panelCard = 'gap-0 rounded-[14px] py-0 ring-border'
const panelHeader = 'border-b px-5.5 py-4.5 [.border-b]:pb-4.5'

export function DashboardPage() {
  const { t } = useTranslation('admin')
  const auditTypeLabel = useAuditTypeLabel()
  const { user } = useSession()
  const { data, isPending, isError, refetch, dataUpdatedAt, isFetching } = useQuery({
    queryKey: ['dashboard'],
    queryFn: () => api<Dashboard>('/api/dashboard'),
    refetchInterval: 60_000,
  })

  if (isPending) return <DashboardSkeleton />

  if (isError) {
    return (
      <Alert variant="error">
        <CircleAlert />
        <AlertTitle>{t('dashboard.loadError')}</AlertTitle>
        <AlertDescription>{t('dashboard.loadErrorHint')}</AlertDescription>
        <AlertAction>
          <Button size="sm" variant="outline" onClick={() => refetch()} disabled={isFetching}>
            {t('actions.retry', { ns: 'common' })}
          </Button>
        </AlertAction>
      </Alert>
    )
  }

  const { metrics, security, health, recentActivity } = data
  const canSeeUsers = hasPermission(user, Permissions.Users.View)
  const issues = security.usersWithFailedLogins + security.lockedAccounts + security.unconfirmedEmails

  return (
    <div className="grid gap-6">
      {/* Hero: τίτλος, βασικά νούμερα σε μία γραμμή και η κατάσταση του συστήματος δεξιά */}
      <section className="bg-hero grid gap-6 rounded-[18px] border border-[#262a38] px-6 py-6 sm:px-8 sm:py-7 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-center lg:gap-10">
        <div className="min-w-0">
          <h1 className="text-[1.9rem] leading-tight font-semibold tracking-tight">{t('nav.dashboard', { ns: 'common' })}</h1>
          <p className="mt-1.5 max-w-xl text-[0.95rem] text-foreground/80">
            {t('dashboard.description')}
          </p>
          <ul aria-label={t('dashboard.keyMetrics')} className="mt-6 grid grid-cols-2 gap-3 sm:flex sm:flex-wrap sm:items-center sm:gap-y-3">
            <Metric
              icon={Users}
              tint="text-success"
              value={metrics.totalUsers}
              label={t('dashboard.metrics.users', { count: metrics.totalUsers })}
              testId="stat-users"
              to={canSeeUsers ? '/admin/users' : undefined}
            />
            <Metric
              icon={Layers}
              tint="text-purple"
              value={metrics.totalRoles}
              label={t('dashboard.metrics.roles', { count: metrics.totalRoles })}
              testId="stat-roles"
              to={hasPermission(user, Permissions.Roles.View) ? '/admin/roles' : undefined}
            />
            <Metric
              icon={ShieldCheck}
              tint="text-info"
              value={metrics.administrators}
              label={t('dashboard.metrics.admins', { count: metrics.administrators })}
              testId="stat-admins"
              to={canSeeUsers ? '/admin/users?role=Admin' : undefined}
            />
            <Metric
              icon={Activity}
              tint="text-warning"
              value={metrics.auditEvents24h}
              label={t('dashboard.metrics.audits', { count: metrics.auditEvents24h })}
              testId="stat-audits"
              to={hasPermission(user, Permissions.AuditLogs.View) ? '/admin/audit-logs' : undefined}
            />
          </ul>
        </div>

        <dl
          aria-label={t('dashboard.systemStatus')}
          className="grid grid-cols-[auto_auto_minmax(0,1fr)] items-center gap-x-3 gap-y-3.5 border-white/10 text-sm max-lg:border-t max-lg:pt-5 lg:border-l lg:pl-10"
        >
          <StatusRow
            icon={health.databaseHealthy ? CircleCheck : CircleAlert}
            tint={health.databaseHealthy ? 'text-success' : 'text-destructive'}
            label={t('dashboard.status')}
          >
            <span className="flex items-center gap-2.5" title={t('dashboard.responseTime', { time: timeAgo(new Date(dataUpdatedAt).toISOString()).toLowerCase() })}>
              {health.databaseHealthy ? (
                <Badge variant="success" dot className="bg-black/40">
                  {t('dashboard.healthy')}
                </Badge>
              ) : (
                <Badge variant="destructive" dot>
                  {t('dashboard.unavailable')}
                </Badge>
              )}
              <span className="font-semibold tabular-nums">{health.databaseResponseMs}ms</span>
            </span>
          </StatusRow>

          <StatusRow icon={issues === 0 ? ShieldCheck : ShieldAlert} tint={issues === 0 ? 'text-info' : 'text-warning'} label={t('dashboard.security')}>
            <Tooltip>
              <TooltipTrigger asChild>
                {canSeeUsers ? (
                  <Link to="/admin/users" className={cn('font-semibold hover:underline', issues === 0 ? 'text-success' : 'text-warning')}>
                    {issues === 0 ? t('dashboard.noIssues') : t('dashboard.toReview', { count: issues })}
                  </Link>
                ) : (
                  <span tabIndex={0} className={cn('font-semibold', issues === 0 ? 'text-success' : 'text-warning')}>
                    {issues === 0 ? t('dashboard.noIssues') : t('dashboard.toReview', { count: issues })}
                  </span>
                )}
              </TooltipTrigger>
              <TooltipContent side="bottom" className="grid gap-1">
                <SecurityLine label={t('dashboard.failedLogins')} value={security.usersWithFailedLogins} />
                <SecurityLine label={t('dashboard.lockedAccounts')} value={security.lockedAccounts} />
                <SecurityLine label={t('dashboard.unconfirmedEmails')} value={security.unconfirmedEmails} />
              </TooltipContent>
            </Tooltip>
          </StatusRow>

          <StatusRow icon={KeyRound} tint="text-purple" label={t('dashboard.twoFactor')}>
            <span>
              <span className="font-semibold tabular-nums">{security.twoFactorEnabled}</span>
              <span className="text-muted-foreground"> {t('dashboard.twoFactorOf', { count: metrics.totalUsers })}</span>
            </span>
          </StatusRow>
        </dl>
      </section>

      {recentActivity && (
        <section className="grid gap-4 xl:grid-cols-2">
          <Card className={panelCard}>
            <CardHeader className={panelHeader}>
              <CardTitle className="flex items-center gap-2.5">
                <ListOrdered className="size-[18px] text-muted-foreground" />
                {t('dashboard.recentActivity')}
              </CardTitle>
              <CardDescription>{t('dashboard.recentActivityDescription')}</CardDescription>
              <CardAction>
                <Link to="/admin/audit-logs" className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground">
                  {t('actions.viewAll', { ns: 'common' })} <ArrowRight className="size-3.5" />
                </Link>
              </CardAction>
            </CardHeader>
            <CardContent className="px-5.5 py-1.5">
              {recentActivity.length === 0 ? (
                <p className="py-4 text-sm text-muted-foreground">{t('dashboard.noActivity')}</p>
              ) : (
                <ol aria-label={t('dashboard.recentActivity')}>
                  {recentActivity.map((item) => (
                    // Γραμμή χρόνου: τελεία ανά εγγραφή, ενωμένες με μια κάθετη γραμμή
                    <li
                      key={item.id}
                      className="group relative flex items-center pl-6 text-sm before:absolute before:inset-y-0 before:left-[3.5px] before:w-px before:bg-border first:before:top-1/2 last:before:bottom-1/2 only:before:hidden"
                    >
                      <span
                        aria-hidden
                        className={cn('absolute top-1/2 left-0 size-2 -translate-y-1/2 rounded-full ring-4 ring-card', activityDot[item.type] ?? 'bg-muted-foreground')}
                      />
                      <div className="flex min-w-0 flex-1 items-center gap-3 border-b py-3 group-last:border-b-0">
                        <Badge variant={activityBadge[item.type] ?? 'muted'} className="min-w-16 justify-center">
                          {auditTypeLabel(item.type)}
                        </Badge>
                        <div className="min-w-0 flex-1 truncate">
                          <Trans
                            t={t}
                            i18nKey="dashboard.changed"
                            values={{ user: item.userEmail ?? t('shared.system'), table: item.tableName ?? '' }}
                            components={{
                              user: <span className="font-medium" />,
                              muted: <span className="text-muted-foreground" />,
                              table: <span className="font-mono text-xs" />,
                            }}
                          />
                        </div>
                        <DateTime value={item.dateTime} mode="relative" className="text-xs whitespace-nowrap text-muted-foreground" />
                      </div>
                    </li>
                  ))}
                </ol>
              )}
            </CardContent>
          </Card>
        </section>
      )}
    </div>
  )
}

// Ένα νούμερο της γραμμής του hero (link προς τη σχετική σελίδα όταν υπάρχει πρόσβαση)
function Metric({
  icon: Icon,
  tint,
  value,
  label,
  testId,
  to,
}: {
  icon: ComponentType<{ className?: string }>
  tint: string
  value: number
  label: string
  testId: string
  to?: string
}) {
  const content = (
    <>
      <Icon className={cn('size-[18px] shrink-0', tint)} />
      <span className="font-semibold tabular-nums" data-testid={testId}>
        {value}
      </span>
      <span className="text-foreground/75">{label}</span>
    </>
  )
  return (
    <li className="flex items-center sm:pr-5 sm:not-first:border-l sm:not-first:border-white/10 sm:not-first:pl-5 sm:last:pr-0">
      {to ? (
        <Link to={to} className="-mx-1.5 -my-1 flex items-center gap-2 rounded-md px-1.5 py-1 transition-colors hover:bg-white/5">
          {content}
        </Link>
      ) : (
        <span className="flex items-center gap-2">{content}</span>
      )}
    </li>
  )
}

function StatusRow({
  icon: Icon,
  tint,
  label,
  children,
}: {
  icon: ComponentType<{ className?: string }>
  tint: string
  label: string
  children: ReactNode
}) {
  return (
    <div className="contents">
      <Icon className={cn('size-[18px]', tint)} />
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="min-w-0">{children}</dd>
    </div>
  )
}

function SecurityLine({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex justify-between gap-6">
      <span>{label}</span>
      <span className="font-semibold tabular-nums">{value}</span>
    </div>
  )
}

function DashboardSkeleton() {
  const { t } = useTranslation('admin')
  return (
    <div className="grid gap-6" aria-busy="true" aria-label={t('dashboard.loading')}>
      <Skeleton className="h-[176px] rounded-[18px]" />
      <div className="grid gap-4 xl:grid-cols-2">
        <Skeleton className="h-[260px] rounded-[14px]" />
      </div>
    </div>
  )
}
