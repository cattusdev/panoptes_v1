import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { FieldGroup } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { problemMessage } from '@/lib/api'
import { applyServerErrors, useZodForm } from '@/lib/forms'
import { useCreatePerson, useUpdatePerson, type Person } from '../api'
import { emptyPerson, personSchema, personValues, toPersonInput } from '../forms'
import { PersonDuplicates, PersonFields } from './PersonFields'

// Νέα επαφή (πρόσωπο) ή αλλαγή των στοιχείων της. Το ίδιο πρόσωπο μπορεί μετά να μπει σε πολλούς πελάτες.
export function PersonFormDialog({
  open,
  onOpenChange,
  person,
  onSaved,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  person?: Person
  onSaved?: (person: Person) => void
}) {
  const { t } = useTranslation('crm')
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{person ? t('people.form.editTitle') : t('people.form.createTitle')}</DialogTitle>
          <DialogDescription>{person ? t('people.form.editDescription') : t('people.form.createDescription')}</DialogDescription>
        </DialogHeader>
        {open && <PersonForm person={person} onDone={() => onOpenChange(false)} onSaved={onSaved} />}
      </DialogContent>
    </Dialog>
  )
}

function PersonForm({ person, onDone, onSaved }: { person?: Person; onDone: () => void; onSaved?: (person: Person) => void }) {
  const { t } = useTranslation('crm')
  const form = useZodForm(personSchema, person ? personValues(person) : emptyPerson)
  const create = useCreatePerson()
  const update = useUpdatePerson(person?.id ?? '')
  const values = form.watch()

  const onSubmit = form.handleSubmit(async (submitted) => {
    try {
      const input = toPersonInput(submitted, person?.version)
      const saved = person ? await update.mutateAsync(input) : await create.mutateAsync(input)
      toast.success(person ? t('people.form.saved', { name: saved.name }) : t('people.form.created', { name: saved.name }))
      onSaved?.(saved)
      onDone()
    } catch (error) {
      if (!applyServerErrors(form, error)) toast.error(problemMessage(error))
    }
  })

  return (
    <form onSubmit={onSubmit} noValidate>
      <FieldGroup>
        <PersonFields control={form.control} idPrefix="person" />
        {!person && <PersonDuplicates values={values} />}
      </FieldGroup>
      <DialogFooter className="mt-6">
        <Button type="button" variant="outline" onClick={onDone}>
          {t('actions.cancel', { ns: 'common' })}
        </Button>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting && <Spinner />}
          {person ? t('form.save') : t('people.form.create')}
        </Button>
      </DialogFooter>
    </form>
  )
}
