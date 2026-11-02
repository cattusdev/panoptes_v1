import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Download } from 'lucide-react'
import { DangerZone, SettingsCard } from '@/components/admin/Common'
import { PasswordInput } from '@/components/form/PasswordInput'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { useDeleteAccount } from '@/lib/account-api'
import { fieldErrors, problemMessage } from '@/lib/api'

export function DataPage() {
  const { t } = useTranslation('account')
  const [deleteOpen, setDeleteOpen] = useState(false)

  return (
    <div className="grid gap-4">
      <SettingsCard title={t('data.download.title')} description={t('data.download.description')}>
        <Button asChild variant="outline">
          <a href="/api/account/personal-data" download>
            <Download /> {t('data.download.button')}
          </a>
        </Button>
      </SettingsCard>

      <DangerZone
        title={t('data.delete.title')}
        description={t('data.delete.description')}
        action={
          <Button variant="destructive" onClick={() => setDeleteOpen(true)}>
            {t('data.delete.button')}
          </Button>
        }
      />

      <Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <DialogContent className="sm:max-w-md">{deleteOpen && <DeleteAccountForm onCancel={() => setDeleteOpen(false)} />}</DialogContent>
      </Dialog>
    </div>
  )
}

// Ζητάμε τον κωδικό: η διαγραφή είναι μη αναστρέψιμη (όπως GitHub)
function DeleteAccountForm({ onCancel }: { onCancel: () => void }) {
  const { t } = useTranslation('account')
  const navigate = useNavigate()
  const deleteAccount = useDeleteAccount()
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      await deleteAccount.mutateAsync(password)
      toast.success(t('data.delete.deleted'))
      navigate('/login', { replace: true })
    } catch (err) {
      setError(fieldErrors(err).password ?? problemMessage(err))
    }
  }

  return (
    <form onSubmit={submit}>
      <DialogHeader className="mb-5">
        <DialogTitle>{t('data.delete.dialogTitle')}</DialogTitle>
        <DialogDescription>{t('data.delete.dialogDescription')}</DialogDescription>
      </DialogHeader>
      <Field data-invalid={!!error || undefined}>
        <FieldLabel htmlFor="delete-password">{t('data.delete.password')}</FieldLabel>
        <PasswordInput
          id="delete-password"
          autoComplete="current-password"
          autoFocus
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          aria-invalid={!!error || undefined}
        />
        <FieldError>{error}</FieldError>
      </Field>
      <DialogFooter className="mt-6">
        <Button type="button" variant="outline" onClick={onCancel} disabled={deleteAccount.isPending}>
          {t('actions.cancel', { ns: 'common' })}
        </Button>
        <Button type="submit" variant="destructive" disabled={deleteAccount.isPending || !password}>
          {deleteAccount.isPending && <Spinner />}
          {t('data.delete.submit')}
        </Button>
      </DialogFooter>
    </form>
  )
}
