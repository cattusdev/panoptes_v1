import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { fieldErrors, problemMessage } from '@/lib/api'

type Props = {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  submitLabel: string
  initialName?: string
  onSubmit: (name: string) => Promise<unknown>
}

// Ένα dialog για δημιουργία και μετονομασία ρόλου
export function RoleNameDialog({ open, onOpenChange, ...form }: Props) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        {/* Το περιεχόμενο ξαναστήνεται σε κάθε άνοιγμα, άρα ξεκινάει πάντα καθαρό */}
        <RoleNameForm {...form} onDone={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}

function RoleNameForm({
  title,
  description,
  submitLabel,
  initialName = '',
  onSubmit,
  onDone,
}: Omit<Props, 'open' | 'onOpenChange'> & { onDone: () => void }) {
  const { t } = useTranslation('admin')
  const [name, setName] = useState(initialName)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setPending(true)
    setError(null)
    try {
      await onSubmit(name.trim())
      onDone()
    } catch (err) {
      setError(fieldErrors(err).name ?? problemMessage(err))
    } finally {
      setPending(false)
    }
  }

  return (
    <form onSubmit={submit}>
      <DialogHeader className="mb-5">
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>{description}</DialogDescription>
      </DialogHeader>
      <Field data-invalid={!!error || undefined}>
        <FieldLabel htmlFor="role-name">{t('roles.nameDialog.label')}</FieldLabel>
        <Input
          id="role-name"
          required
          minLength={2}
          maxLength={64}
          autoFocus
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder={t('roles.nameDialog.placeholder')}
          aria-invalid={!!error || undefined}
        />
        {error ? <FieldError>{error}</FieldError> : <FieldDescription>{t('roles.nameDialog.hint')}</FieldDescription>}
      </Field>
      <DialogFooter className="mt-6">
        <Button type="button" variant="outline" onClick={onDone} disabled={pending}>
          {t('actions.cancel', { ns: 'common' })}
        </Button>
        <Button type="submit" disabled={pending || name.trim().length < 2 || name.trim() === initialName}>
          {pending && <Spinner />}
          {submitLabel}
        </Button>
      </DialogFooter>
    </form>
  )
}
