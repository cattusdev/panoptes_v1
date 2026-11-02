import { useState, type Ref } from 'react'
import { useTranslation } from 'react-i18next'
import { InputGroup, InputGroupAddon, InputGroupInput, InputGroupText } from '@/components/ui/input-group'
import { currencySymbol, formatAmount, parseAmount } from '@/lib/money'
import { cn } from '@/lib/utils'

// Πεδίο ποσού: γράφεις όπως συνηθίζεις (1.234,50 ή 1,234.50), βλέπεις το νόμισμα δεξιά.
// value: αριθμός, null = κενό, NaN = κάτι που δεν είναι ποσό (το απορρίπτει το schema με μήνυμα).
// Μετά το blur το ποσό γράφεται ξανά στη μορφή της γλώσσας.
export function MoneyInput({
  value,
  onChange,
  onBlur,
  currency,
  id,
  name,
  ref,
  disabled,
  className,
  'aria-invalid': ariaInvalid,
}: {
  value: number | null
  onChange: (value: number | null) => void
  onBlur?: () => void
  currency: string
  id?: string
  name?: string
  ref?: Ref<HTMLInputElement>
  disabled?: boolean
  className?: string
  'aria-invalid'?: boolean
}) {
  const { i18n } = useTranslation()
  const language = i18n.language
  const [text, setText] = useState(() => display(value, language))
  const [shown, setShown] = useState({ value, language })

  // Νέα τιμή από έξω (reset της φόρμας) ή αλλαγή γλώσσας: ξαναγράφουμε το κείμενο
  if (!sameAmount(shown.value, value) || shown.language !== language) {
    setShown({ value, language })
    if (!sameAmount(parseAmount(text, language), value) || shown.language !== language) setText(display(value, language))
  }

  return (
    <InputGroup className={cn('max-w-56', className)} data-disabled={disabled || undefined}>
      <InputGroupInput
        id={id}
        name={name}
        ref={ref}
        inputMode="decimal"
        autoComplete="off"
        disabled={disabled}
        aria-invalid={ariaInvalid}
        value={text}
        onChange={(e) => {
          setText(e.target.value)
          const parsed = parseAmount(e.target.value, language)
          setShown({ value: parsed, language })
          onChange(parsed)
        }}
        onBlur={() => {
          const parsed = parseAmount(text, language)
          if (parsed !== null && !Number.isNaN(parsed)) setText(formatAmount(parsed, language))
          onBlur?.()
        }}
        className="text-right tabular-nums"
      />
      <InputGroupAddon align="inline-end">
        <InputGroupText>{currencySymbol(currency, language)}</InputGroupText>
      </InputGroupAddon>
    </InputGroup>
  )
}

function display(value: number | null, language: string) {
  return value === null || Number.isNaN(value) ? '' : formatAmount(value, language)
}

function sameAmount(a: number | null, b: number | null) {
  return a === b || (Number.isNaN(a) && Number.isNaN(b))
}
