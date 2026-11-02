import { useState, type FormEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { CircleAlert, Eye, LockOpen, ShieldCheck } from 'lucide-react'
import { DangerZone, SettingsCard, UserAvatar, UserStatusBadge } from '@/components/admin/Common'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { PageHeader } from '@/components/admin/PageHeader'
import { PasswordInput } from '@/components/form/PasswordInput'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { useResetPassword, useRoleOptions, useUpdateUser, useUser, useUserAction, type UserDetail } from '@/lib/admin-api'
import { fieldErrors, problemMessage } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import { useDateFormat } from '@/lib/dates'
import { Permissions } from '@/lib/permissions'

// Το link "πίσω" στη λίστα χρηστών
function useBack() {
  const { t } = useTranslation()
  return { to: '/admin/users', label: t('nav.users') }
}

export function UserDetailPage() {
  const { t } = useTranslation('admin')
  const back = useBack()
  const { id = '' } = useParams()
  const user = useUser(id)

  if (user.isPending) {
    return (
      <>
        <PageHeader title={<Skeleton className="h-8 w-64" />} back={back} />
        <div className="grid gap-4 lg:grid-cols-2">
          <Skeleton className="h-72 rounded-[14px]" />
          <Skeleton className="h-72 rounded-[14px]" />
        </div>
      </>
    )
  }

  if (user.isError) {
    return (
      <>
        <PageHeader title={t('users.detail.title')} back={back} />
        <Alert variant="error">
          <CircleAlert />
          <AlertTitle>{t('users.detail.loadError')}</AlertTitle>
          <AlertDescription>{problemMessage(user.error)}</AlertDescription>
        </Alert>
      </>
    )
  }

  return <UserDetail user={user.data} />
}

function UserDetail({ user }: { user: UserDetail }) {
  const { t } = useTranslation('admin')
  const back = useBack()
  const { user: me } = useSession()
  const canEdit = user.canManage && hasPermission(me, Permissions.Users.Edit)
  const canDelete = user.canManage && !user.isSelf && hasPermission(me, Permissions.Users.Delete)

  return (
    <>
      <PageHeader
        back={back}
        title={
          <span className="flex flex-wrap items-center gap-3">
            <UserAvatar email={user.email} avatarUrl={user.avatarUrl} active={!user.isLockedOut} size="lg" />
            <span className="break-all">{user.email}</span>
            {user.isAdmin && <Badge variant="purple">{t('shared.administrator')}</Badge>}
            {user.isSelf && <Badge variant="info">{t('shared.you')}</Badge>}
            <UserStatusBadge isLockedOut={user.isLockedOut} emailConfirmed={user.emailConfirmed} />
          </span>
        }
      />

      {!user.canManage && (
        <Alert className="mb-5">
          <Eye />
          <AlertTitle>{t('users.detail.viewOnly')}</AlertTitle>
          <AlertDescription>{t('users.detail.viewOnlyDescription')}</AlertDescription>
        </Alert>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <AccountCard key={`${user.email}|${user.role}`} user={user} canEdit={canEdit} />
        <SecurityCard user={user} canEdit={canEdit} />
      </div>

      {canDelete && <DeleteUser user={user} />}
    </>
  )
}

function AccountCard({ user, canEdit }: { user: UserDetail; canEdit: boolean }) {
  const { t } = useTranslation('admin')
  const updateUser = useUpdateUser(user.id)
  const roleOptions = useRoleOptions()
  const [email, setEmail] = useState(user.email)
  const [role, setRole] = useState(user.role ?? '')
  const [errors, setErrors] = useState<Record<string, string>>({})

  const dirty = email !== user.email || role !== (user.role ?? '')

  // Ο τρέχων ρόλος εμφανίζεται πάντα, ακόμα κι αν δεν μπορείς να τον δώσεις
  const assignable = roleOptions.data?.assignable ?? []
  const options = [...new Set([...assignable, ...(user.role ? [user.role] : [])])].map((r) => ({
    value: r,
    label: r,
    disabled: !assignable.includes(r),
  }))

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setErrors({})
    try {
      await updateUser.mutateAsync({ email, role: role || null })
      toast.success(t('users.detail.account.updated'))
    } catch (error) {
      const byField = fieldErrors(error)
      if (Object.keys(byField).length > 0) setErrors(byField)
      else toast.error(problemMessage(error))
    }
  }

  return (
    <SettingsCard title={t('users.detail.account.title')} description={t('users.detail.account.description')}>
      <form onSubmit={onSubmit}>
        <FieldGroup>
          <Field data-invalid={!!errors.email || undefined}>
            <FieldLabel htmlFor="user-email">{t('users.detail.account.email')}</FieldLabel>
            <Input
              id="user-email"
              type="email"
              required
              value={email}
              disabled={!canEdit}
              onChange={(e) => setEmail(e.target.value)}
              aria-invalid={!!errors.email || undefined}
            />
            <FieldError>{errors.email}</FieldError>
          </Field>
          <Field>
            <FieldLabel htmlFor="user-role">{t('users.detail.account.role')}</FieldLabel>
            <OptionSelect id="user-role" value={role} onChange={setRole} emptyLabel={t('users.detail.account.noRole')} options={options} disabled={!canEdit} />
            <FieldDescription>{t('users.detail.account.roleHint')}</FieldDescription>
          </Field>
        </FieldGroup>
        {canEdit && (
          <div className="mt-6 flex gap-2">
            <Button type="submit" disabled={!dirty || updateUser.isPending}>
              {updateUser.isPending && <Spinner />}
              {t('actions.saveChanges', { ns: 'common' })}
            </Button>
            {dirty && (
              <Button
                type="button"
                variant="ghost"
                onClick={() => {
                  setEmail(user.email)
                  setRole(user.role ?? '')
                  setErrors({})
                }}
              >
                {t('users.detail.account.discard')}
              </Button>
            )}
          </div>
        )}
      </form>
    </SettingsCard>
  )
}

function SecurityCard({ user, canEdit }: { user: UserDetail; canEdit: boolean }) {
  const { t } = useTranslation('admin')
  const resetPassword = useResetPassword(user.id)
  const userAction = useUserAction()
  const format = useDateFormat()
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function onReset(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      await resetPassword.mutateAsync(password)
      setPassword('')
      toast.success(t('users.detail.security.passwordUpdated'), { description: t('users.detail.security.passwordUpdatedDescription') })
    } catch (err) {
      setError(fieldErrors(err).newPassword ?? problemMessage(err))
    }
  }

  async function unlock() {
    try {
      await userAction.mutateAsync({ id: user.id, action: 'unlock' })
      toast.success(t('users.detail.security.unlocked'))
    } catch (err) {
      toast.error(problemMessage(err))
    }
  }

  return (
    <SettingsCard title={t('users.detail.security.title')} description={t('users.detail.security.description')}>
      <div className="mb-6 grid gap-3">
        <div className="flex items-center justify-between gap-3 rounded-lg border px-3.5 py-3">
          <div>
            <div className="text-sm font-medium">{t('users.detail.security.signInStatus')}</div>
            <div className="text-xs text-muted-foreground">
              {user.isLockedOut
                ? user.lockoutEnd
                  ? t('users.detail.security.lockedUntil', { date: format.dateTime(user.lockoutEnd) })
                  : t('users.detail.security.locked')
                : user.emailConfirmed
                  ? t('users.detail.security.canSignIn')
                  : t('users.detail.security.notConfirmed')}
            </div>
          </div>
          {user.isLockedOut && canEdit ? (
            <Button size="sm" variant="outline" onClick={unlock} disabled={userAction.isPending}>
              <LockOpen /> {t('users.detail.security.unlock')}
            </Button>
          ) : (
            <UserStatusBadge isLockedOut={user.isLockedOut} emailConfirmed={user.emailConfirmed} />
          )}
        </div>
        <div className="flex items-center justify-between gap-3 rounded-lg border px-3.5 py-3">
          <div>
            <div className="text-sm font-medium">{t('users.detail.security.twoFactor')}</div>
            <div className="text-xs text-muted-foreground">{t('users.detail.security.twoFactorHint')}</div>
          </div>
          {user.twoFactorEnabled ? (
            <Badge variant="success">
              <ShieldCheck /> {t('users.detail.security.enabled')}
            </Badge>
          ) : (
            <Badge variant="muted">{t('users.detail.security.off')}</Badge>
          )}
        </div>
      </div>

      {canEdit && (
        <form onSubmit={onReset}>
          <Field data-invalid={!!error || undefined}>
            <FieldLabel htmlFor="new-password">{t('users.detail.security.newPassword')}</FieldLabel>
            <div className="flex gap-2">
              <div className="flex-1">
                <PasswordInput
                  id="new-password"
                  autoComplete="new-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  aria-invalid={!!error || undefined}
                />
              </div>
              <Button type="submit" variant="outline" disabled={!password || resetPassword.isPending}>
                {resetPassword.isPending && <Spinner />}
                {t('users.detail.security.setPassword')}
              </Button>
            </div>
            {error ? <FieldError>{error}</FieldError> : <FieldDescription>{t('users.detail.security.newPasswordHint')}</FieldDescription>}
          </Field>
        </form>
      )}
    </SettingsCard>
  )
}

function DeleteUser({ user }: { user: UserDetail }) {
  const { t } = useTranslation('admin')
  const navigate = useNavigate()
  const userAction = useUserAction()
  const [open, setOpen] = useState(false)

  return (
    <div className="mt-4">
      <DangerZone
        title={t('users.detail.delete.title')}
        description={t('users.detail.delete.description')}
        action={
          <Button variant="destructive" onClick={() => setOpen(true)}>
            {t('users.detail.delete.button')}
          </Button>
        }
      />
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title={t('users.detail.delete.confirmTitle')}
        description={
          <Trans t={t} i18nKey="users.detail.delete.confirmDescription" values={{ email: user.email }} components={{ strong: <strong /> }} />
        }
        confirmLabel={t('users.detail.delete.button')}
        destructive
        onConfirm={async () => {
          try {
            await userAction.mutateAsync({ id: user.id, action: 'delete' })
            toast.success(t('users.detail.delete.deleted', { email: user.email }))
            navigate('/admin/users', { replace: true })
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />
    </div>
  )
}
