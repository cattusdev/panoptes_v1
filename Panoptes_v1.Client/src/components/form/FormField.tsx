import { useId, type ReactNode } from 'react'
import { Controller, type Control, type ControllerRenderProps, type FieldPath, type FieldValues } from 'react-hook-form'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'

export type FormControlProps<TValues extends FieldValues, TName extends FieldPath<TValues>> = ControllerRenderProps<TValues, TName> & {
  id: string
  'aria-invalid'?: true
}

// Ένα πεδίο φόρμας (react-hook-form): label, το control, και από κάτω είτε το λάθος είτε η περιγραφή.
// Το control παίρνει value/onChange/onBlur/ref, id και aria-invalid: <FormField ...>{(field) => <Input {...field} />}</FormField>
export function FormField<TValues extends FieldValues, TName extends FieldPath<TValues>>({
  control,
  name,
  label,
  description,
  id,
  className,
  children,
}: {
  control: Control<TValues>
  name: TName
  label: ReactNode
  description?: ReactNode
  id?: string
  className?: string
  children: (field: FormControlProps<TValues, TName>) => ReactNode
}) {
  const generatedId = useId()
  const controlId = id ?? generatedId

  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <Field data-invalid={fieldState.invalid || undefined} className={className}>
          <FieldLabel htmlFor={controlId}>{label}</FieldLabel>
          {children({ ...field, id: controlId, 'aria-invalid': fieldState.invalid || undefined })}
          {fieldState.error?.message ? (
            <FieldError>{fieldState.error.message}</FieldError>
          ) : (
            description && <FieldDescription>{description}</FieldDescription>
          )}
        </Field>
      )}
    />
  )
}
