import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ShieldCheck } from 'lucide-react'
import { SettingsCard } from '@/components/admin/Common'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { PasswordInput } from '@/components/form/PasswordInput'
import { PasswordRequirements } from '@/components/form/PasswordRequirements'
import { usePasswordValid } from '@/lib/password'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import {
  useAccount,
  useChangePassword,
  useDisableTwoFactor,
  useForgetBrowser,
  useRegenerateRecoveryCodes,
  useResetAuthenticator,
  type Account,
} from '@/lib/account-api'
import { fieldErrors, problemMessage } from '@/lib/api'
import { RecoveryCodes } from './RecoveryCodes'
import { TwoFactorSetupDialog } from './TwoFactorSetupDialog'

export function SecurityPage() {
  const account = useAccount()
  if (account.isPending) return <Skeleton className="h-96 rounded-[14px]" />
  if (account.isError) return <p className="text-sm text-destructive">{problemMessage(account.error)}</p>

  return (
    <div className="grid gap-4">
      <ChangePasswordCard />
      <TwoFactorCard account={account.data} />
    </div>
  )
}

function ChangePasswordCard() {
  const { t } = useTranslation('account')
  const changePassword = useChangePassword()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const nextValid = usePasswordValid(next)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setErrors({})
    if (!nextValid) {
      setErrors({ newPassword: t('security.password.notValid') })
      return
    }
    try {
      await changePassword.mutateAsync({ currentPassword: current, newPassword: next })
      setCurrent('')
      setNext('')
      toast.success(t('security.password.updated'), { description: t('security.password.updatedDescription') })
    } catch (error) {
      const byField = fieldErrors(error)
      if (Object.keys(byField).length > 0) setErrors(byField)
      else toast.error(problemMessage(error))
    }
  }

  return (
    <SettingsCard title={t('security.password.title')} description={t('security.password.description')}>
      <form onSubmit={submit} className="max-w-sm">
        <FieldGroup>
          <Field data-invalid={!!errors.currentPassword || undefined}>
            <FieldLabel htmlFor="current-password">{t('security.password.current')}</FieldLabel>
            <PasswordInput
              id="current-password"
              autoComplete="current-password"
              required
              value={current}
              onChange={(e) => setCurrent(e.target.value)}
              aria-invalid={!!errors.currentPassword || undefined}
            />
            <FieldError>{errors.currentPassword}</FieldError>
          </Field>
          <Field data-invalid={!!errors.newPassword || undefined}>
            <FieldLabel htmlFor="new-password">{t('security.password.new')}</FieldLabel>
            <PasswordInput
              id="new-password"
              autoComplete="new-password"
              required
              value={next}
              onChange={(e) => setNext(e.target.value)}
              aria-invalid={!!errors.newPassword || undefined}
            />
            <PasswordRequirements password={next} />
            <FieldError>{errors.newPassword}</FieldError>
          </Field>
        </FieldGroup>
        <Button type="submit" className="mt-5" disabled={changePassword.isPending || !current || !next}>
          {changePassword.isPending && <Spinner />}
          {t('security.password.submit')}
        </Button>
      </form>
    </SettingsCard>
  )
}

type PendingAction = 'regenerate' | 'reset' | 'disable' | null

function TwoFactorCard({ account }: { account: Account }) {
  const { t } = useTranslation('account')
  const [setupOpen, setSetupOpen] = useState(false)
  const [pending, setPending] = useState<PendingAction>(null)
  const [newCodes, setNewCodes] = useState<string[] | null>(null)
  const regenerate = useRegenerateRecoveryCodes()
  const reset = useResetAuthenticator()
  const disable = useDisableTwoFactor()
  const forgetBrowser = useForgetBrowser()

  const confirmations = {
    regenerate: {
      title: t('security.confirm.regenerate.title'),
      description: t('security.confirm.regenerate.description'),
      confirmLabel: t('security.confirm.regenerate.confirm'),
      destructive: false,
      run: async () => setNewCodes((await regenerate.mutateAsync()).recoveryCodes),
    },
    reset: {
      title: t('security.confirm.reset.title'),
      description: t('security.confirm.reset.description'),
      confirmLabel: t('security.confirm.reset.confirm'),
      destructive: true,
      run: async () => {
        await reset.mutateAsync()
        toast.success(t('security.confirm.reset.success'), { description: t('security.confirm.reset.successDescription') })
      },
    },
    disable: {
      title: t('security.confirm.disable.title'),
      description: t('security.confirm.disable.description'),
      confirmLabel: t('security.confirm.disable.confirm'),
      destructive: true,
      run: async () => {
        await disable.mutateAsync()
        toast.success(t('security.confirm.disable.success'))
      },
    },
  } as const

  const active = pending ? confirmations[pending] : null

  return (
    <SettingsCard title={t('security.twoFactor.title')} description={t('security.twoFactor.description')}>
      {!account.twoFactorEnabled ? (
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-2 text-sm">
            <Badge variant="muted">{t('security.twoFactor.off')}</Badge>
            <span className="text-muted-foreground">{t('security.twoFactor.offHint')}</span>
          </div>
          <Button onClick={() => setSetupOpen(true)}>
            <ShieldCheck /> {t('security.twoFactor.setUp')}
          </Button>
        </div>
      ) : (
        <div className="grid gap-3">
          <div className="flex items-center gap-2 text-sm">
            <Badge variant="success" dot>
              {t('security.twoFactor.on')}
            </Badge>
            <span className="text-muted-foreground">{t('security.twoFactor.onHint')}</span>
          </div>
          <SettingRow title={t('security.twoFactor.app')} description={t('security.twoFactor.appConfigured')}>
            <Button variant="outline" size="sm" onClick={() => setPending('reset')}>
              {t('security.twoFactor.reset')}
            </Button>
          </SettingRow>
          <SettingRow
            title={t('security.twoFactor.recoveryCodes')}
            description={
              account.recoveryCodesLeft <= 3 ? (
                <span className="text-warning">{t('security.twoFactor.codesLow', { count: account.recoveryCodesLeft })}</span>
              ) : (
                t('security.twoFactor.codesLeft', { left: account.recoveryCodesLeft })
              )
            }
          >
            <Button variant="outline" size="sm" onClick={() => setPending('regenerate')}>
              {t('security.twoFactor.generateNew')}
            </Button>
          </SettingRow>
          {account.isMachineRemembered && (
            <SettingRow title={t('security.twoFactor.thisDevice')} description={t('security.twoFactor.thisDeviceHint')}>
              <Button
                variant="outline"
                size="sm"
                disabled={forgetBrowser.isPending}
                onClick={async () => {
                  await forgetBrowser.mutateAsync()
                  toast.success(t('security.twoFactor.deviceForgotten'))
                }}
              >
                {t('security.twoFactor.forgetDevice')}
              </Button>
            </SettingRow>
          )}
          <SettingRow title={t('security.twoFactor.turnOff')} description={t('security.twoFactor.turnOffHint')}>
            <Button variant="destructive" size="sm" onClick={() => setPending('disable')}>
              {t('security.twoFactor.turnOff')}
            </Button>
          </SettingRow>
        </div>
      )}

      <TwoFactorSetupDialog open={setupOpen} onOpenChange={setSetupOpen} />

      <ConfirmDialog
        open={!!active}
        onOpenChange={(open) => !open && setPending(null)}
        title={active?.title ?? ''}
        description={active?.description ?? ''}
        confirmLabel={active?.confirmLabel ?? ''}
        destructive={active?.destructive}
        onConfirm={async () => {
          try {
            await active?.run()
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />

      <Dialog open={!!newCodes} onOpenChange={(open) => !open && setNewCodes(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader className="mb-4">
            <DialogTitle>{t('security.newCodes.title')}</DialogTitle>
            <DialogDescription>{t('security.newCodes.description')}</DialogDescription>
          </DialogHeader>
          {newCodes && <RecoveryCodes codes={newCodes} />}
          <DialogFooter className="mt-6">
            <Button onClick={() => setNewCodes(null)}>{t('recoveryCodes.saved')}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </SettingsCard>
  )
}

function SettingRow({ title, description, children }: { title: string; description: ReactNode; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 rounded-lg border px-3.5 py-3">
      <div>
        <div className="text-sm font-medium">{title}</div>
        <div className="text-xs text-muted-foreground">{description}</div>
      </div>
      {children}
    </div>
  )
}
