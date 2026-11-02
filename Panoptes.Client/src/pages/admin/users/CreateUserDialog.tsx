import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { PasswordInput } from '@/components/form/PasswordInput'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { useCreateUser, useRoleOptions } from '@/lib/admin-api'
import { fieldErrors, problemMessage } from '@/lib/api'

export function CreateUserDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { t } = useTranslation('admin')
  const createUser = useCreateUser()
  const roleOptions = useRoleOptions()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})

  function reset() {
    setEmail('')
    setPassword('')
    setRole('')
    setErrors({})
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setErrors({})
    try {
      const user = await createUser.mutateAsync({ email, password, role: role || null })
      toast.success(t('users.create.created', { email: user.email }), { description: t('users.create.createdDescription') })
      reset()
      onOpenChange(false)
    } catch (error) {
      const byField = fieldErrors(error)
      if (Object.keys(byField).length > 0) setErrors(byField)
      else toast.error(problemMessage(error))
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(value) => {
        if (!value) reset()
        onOpenChange(value)
      }}
    >
      <DialogContent className="sm:max-w-md">
        <form onSubmit={onSubmit}>
          <DialogHeader className="mb-5">
            <DialogTitle>{t('users.create.title')}</DialogTitle>
            <DialogDescription>{t('users.create.description')}</DialogDescription>
          </DialogHeader>

          <FieldGroup>
            <Field data-invalid={!!errors.email || undefined}>
              <FieldLabel htmlFor="new-user-email">{t('users.create.email')}</FieldLabel>
              <Input
                id="new-user-email"
                type="email"
                required
                autoFocus
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                aria-invalid={!!errors.email || undefined}
              />
              <FieldError>{errors.email}</FieldError>
            </Field>

            <Field data-invalid={!!errors.password || undefined}>
              <FieldLabel htmlFor="new-user-password">{t('users.create.password')}</FieldLabel>
              <PasswordInput
                id="new-user-password"
                required
                autoComplete="new-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                aria-invalid={!!errors.password || undefined}
              />
              {errors.password ? (
                <FieldError>{errors.password}</FieldError>
              ) : (
                <FieldDescription>{t('users.create.passwordHint')}</FieldDescription>
              )}
            </Field>

            <Field>
              <FieldLabel htmlFor="new-user-role">{t('users.create.role')}</FieldLabel>
              <OptionSelect
                id="new-user-role"
                value={role}
                onChange={setRole}
                emptyLabel={t('users.create.noRole')}
                options={(roleOptions.data?.assignable ?? []).map((r) => ({ value: r, label: r }))}
              />
              <FieldDescription>{t('users.create.roleHint')}</FieldDescription>
            </Field>
          </FieldGroup>

          <DialogFooter className="mt-6">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t('actions.cancel', { ns: 'common' })}
            </Button>
            <Button type="submit" disabled={createUser.isPending}>
              {createUser.isPending && <Spinner />}
              {t('users.create.submit')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
