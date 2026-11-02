import { zodResolver } from '@hookform/resolvers/zod'
import { useForm, type DefaultValues, type FieldValues, type Path, type UseFormSetError } from 'react-hook-form'
import { z } from 'zod'
import i18n from '@/i18n'
import { fieldErrors } from '@/lib/api'
import { formatAmount } from '@/lib/money'

// Τα μηνύματα της zod στη γλώσσα του χρήστη (common:validation.*). Υπολογίζονται τη στιγμή του ελέγχου,
// οπότε τα schemas μπορούν να ζουν στο module scope. Ένα schema με δικό του message το κρατάει.
z.config({
  customError: (issue) => {
    const t = i18n.t
    switch (issue.code) {
      case 'invalid_type':
        if (issue.input === undefined || issue.input === null || issue.input === '') return t('validation.required')
        if (issue.expected === 'number') {
          return Number.isNaN(issue.input) ? t('validation.amount', { example: formatAmount(1234.5) }) : t('validation.number')
        }
        return t('validation.invalid')
      case 'too_small':
        if (issue.origin === 'string') {
          return Number(issue.minimum) <= 1 ? t('validation.required') : t('validation.tooShort', { count: Number(issue.minimum) })
        }
        if (issue.origin === 'array' || issue.origin === 'set') return t('validation.chooseAtLeast', { count: Number(issue.minimum) })
        return t('validation.min', { min: issue.minimum })
      case 'too_big':
        if (issue.origin === 'string') return t('validation.tooLong', { count: Number(issue.maximum) })
        return t('validation.max', { max: issue.maximum })
      case 'invalid_format':
        if (issue.format === 'email') return t('validation.email')
        if (issue.format === 'date') return t('validation.date')
        return t('validation.invalid')
      default:
        return t('validation.invalid')
    }
  },
})

// Κοινά κομμάτια schemas
export const schemas = {
  // Υποχρεωτικό κείμενο (κενά δεν μετράνε)
  text: () => z.string().trim().min(1),
  email: () => z.string().trim().min(1).pipe(z.email()),
  // Ημερομηνία χωρίς ώρα (DateOnly του server): "yyyy-MM-dd"
  date: () => z.iso.date(),
  // Ποσό από το MoneyInput: αριθμός (το NaN = μη έγκυρο κείμενο). Για προαιρετικό: .nullable()
  amount: () => z.number(),
  // Πεδία που ξεκινούν κενά (null: DatePicker, MoneyInput, Combobox) αλλά είναι υποχρεωτικά: null -> "This field is required."
  required: <T extends z.ZodType>(schema: T) =>
    schema.nullable().transform((value, ctx) => {
      if (value !== null) return value
      ctx.addIssue({ code: 'custom', message: i18n.t('validation.required') })
      return z.NEVER
    }),
}

// useForm με zod: έλεγχος όταν φύγεις από ένα πεδίο και μετά σε κάθε αλλαγή, focus στο πρώτο λάθος
export function useZodForm<TInput extends FieldValues, TOutput extends FieldValues>(
  schema: z.ZodType<TOutput, TInput>,
  defaultValues: DefaultValues<TInput>,
) {
  return useForm<TInput, unknown, TOutput>({
    resolver: zodResolver(schema),
    defaultValues,
    mode: 'onTouched',
  })
}

// Τα λάθη ανά πεδίο του server (ProblemDetails.errors) στα πεδία της φόρμας. true αν κάποιο ταίριαξε
// (τα υπόλοιπα λάθη πάνε σε toast από αυτόν που καλεί).
export function applyServerErrors<T extends FieldValues>(form: { getValues: () => T; setError: UseFormSetError<T> }, error: unknown) {
  const values = form.getValues() as Record<string, unknown>
  const matching = Object.entries(fieldErrors(error)).filter(([name]) => name in values)
  matching.forEach(([name, message], index) =>
    form.setError(name as Path<T>, { type: 'server', message }, { shouldFocus: index === 0 }),
  )
  return matching.length > 0
}
