import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { z } from 'zod'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { FormField } from '@/components/form/FormField'
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
import { FieldGroup } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { useCreateUser, useRoleOptions } from '@/lib/admin-api'
import { problemMessage } from '@/lib/api'
import { applyServerErrors, schemas, useZodForm } from '@/lib/forms'

// Οι κανόνες του κωδικού τους ελέγχει ο server (ίδιοι με το Identity): εδώ μόνο ότι δεν είναι κενός
const schema = z.object({
  email: schemas.email(),
  password: z.string().min(1),
  role: z.string(),
})

const empty = { email: '', password: '', role: '' }

export function CreateUserDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { t } = useTranslation('admin')
  const createUser = useCreateUser()
  const roleOptions = useRoleOptions()
  const form = useZodForm(schema, empty)

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const user = await createUser.mutateAsync({ email: values.email, password: values.password, role: values.role || null })
      toast.success(t('users.create.created', { email: user.email }), { description: t('users.create.createdDescription') })
      form.reset(empty)
      onOpenChange(false)
    } catch (error) {
      if (!applyServerErrors(form, error)) toast.error(problemMessage(error))
    }
  })

  return (
    <Dialog
      open={open}
      onOpenChange={(value) => {
        if (!value) form.reset(empty)
        onOpenChange(value)
      }}
    >
      <DialogContent className="sm:max-w-md">
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader className="mb-5">
            <DialogTitle>{t('users.create.title')}</DialogTitle>
            <DialogDescription>{t('users.create.description')}</DialogDescription>
          </DialogHeader>

          <FieldGroup>
            <FormField control={form.control} name="email" id="new-user-email" label={t('users.create.email')}>
              {(field) => <Input type="email" autoFocus {...field} />}
            </FormField>

            <FormField
              control={form.control}
              name="password"
              id="new-user-password"
              label={t('users.create.password')}
              description={t('users.create.passwordHint')}
            >
              {(field) => <PasswordInput autoComplete="new-password" {...field} />}
            </FormField>

            <FormField
              control={form.control}
              name="role"
              id="new-user-role"
              label={t('users.create.role')}
              description={t('users.create.roleHint')}
            >
              {(field) => (
                <OptionSelect
                  id={field.id}
                  value={field.value}
                  onChange={field.onChange}
                  emptyLabel={t('users.create.noRole')}
                  options={(roleOptions.data?.assignable ?? []).map((r) => ({ value: r, label: r }))}
                />
              )}
            </FormField>
          </FieldGroup>

          <DialogFooter className="mt-6">
            <Button
              type="button"
              variant="outline"
              onClick={() => {
                form.reset(empty)
                onOpenChange(false)
              }}
            >
              {t('actions.cancel', { ns: 'common' })}
            </Button>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting && <Spinner />}
              {t('users.create.submit')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
