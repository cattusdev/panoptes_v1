import { useState, type FormEvent } from 'react'
import { QRCodeSVG } from 'qrcode.react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Copy } from 'lucide-react'
import { CodeInput } from '@/components/form/CodeInput'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { FieldError } from '@/components/ui/field'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { useAuthenticatorSetup, useEnableTwoFactor } from '@/lib/account-api'
import { fieldErrors, problemMessage } from '@/lib/api'
import { RecoveryCodes } from './RecoveryCodes'

// Ενεργοποίηση 2FA σε 2 βήματα (όπως GitHub / Clerk): σάρωση QR + κωδικός, μετά recovery codes
export function TwoFactorSetupDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">{open && <SetupSteps onDone={() => onOpenChange(false)} />}</DialogContent>
    </Dialog>
  )
}

function SetupSteps({ onDone }: { onDone: () => void }) {
  const { t } = useTranslation('account')
  const setup = useAuthenticatorSetup(true)
  const enable = useEnableTwoFactor()
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null)

  async function verify(e?: FormEvent, value = code) {
    e?.preventDefault()
    setError(null)
    try {
      const result = await enable.mutateAsync(value)
      setRecoveryCodes(result.recoveryCodes)
      toast.success(t('setup.enabled'))
    } catch (err) {
      setError(fieldErrors(err).code ?? problemMessage(err))
      setCode('')
    }
  }

  async function copyKey() {
    if (!setup.data) return
    await navigator.clipboard.writeText(setup.data.sharedKey.replace(/\s/g, ''))
    toast.success(t('setup.keyCopied'))
  }

  if (recoveryCodes) {
    return (
      <>
        <DialogHeader className="mb-4">
          <DialogTitle>{t('setup.saveTitle')}</DialogTitle>
          <DialogDescription>{t('setup.saveDescription')}</DialogDescription>
        </DialogHeader>
        <RecoveryCodes codes={recoveryCodes} />
        <DialogFooter className="mt-6">
          <Button onClick={onDone}>{t('recoveryCodes.saved')}</Button>
        </DialogFooter>
      </>
    )
  }

  return (
    <form onSubmit={verify}>
      <DialogHeader className="mb-4">
        <DialogTitle>{t('setup.title')}</DialogTitle>
        <DialogDescription>{t('setup.description')}</DialogDescription>
      </DialogHeader>

      <div className="grid gap-4">
        <div className="flex justify-center">
          {setup.data ? (
            <div className="rounded-xl bg-white p-3">
              <QRCodeSVG value={setup.data.authenticatorUri} size={168} />
            </div>
          ) : (
            <Skeleton className="size-[192px] rounded-xl" />
          )}
        </div>

        <div className="rounded-lg border px-3 py-2.5">
          <div className="text-xs text-muted-foreground">{t('setup.cantScan')}</div>
          <div className="mt-1 flex items-center justify-between gap-2">
            <code className="font-mono text-sm break-all" data-testid="authenticator-key">
              {setup.data?.sharedKey ?? '…'}
            </code>
            <Button type="button" variant="ghost" size="icon-sm" onClick={copyKey} aria-label={t('setup.copyKey')} disabled={!setup.data}>
              <Copy />
            </Button>
          </div>
        </div>

        <div>
          <div className="mb-2 text-sm font-medium">{t('setup.enterCode')}</div>
          <CodeInput value={code} onChange={setCode} onComplete={(value) => void verify(undefined, value)} disabled={enable.isPending} invalid={!!error} />
          <FieldError className="mt-2">{error}</FieldError>
        </div>
      </div>

      <DialogFooter className="mt-6">
        <Button type="button" variant="outline" onClick={onDone}>
          {t('actions.cancel', { ns: 'common' })}
        </Button>
        <Button type="submit" disabled={code.length < 6 || enable.isPending || !setup.data}>
          {enable.isPending && <Spinner />}
          {t('setup.submit')}
        </Button>
      </DialogFooter>
    </form>
  )
}
