import { useState, type ReactNode, type Ref } from 'react'
import { useTranslation } from 'react-i18next'
import { InputGroup, InputGroupAddon, InputGroupInput, InputGroupText } from '@/components/ui/input-group'
import { parseAmount } from '@/lib/money'
import { cn } from '@/lib/utils'

export type DecimalInputProps = {
  value: number | null
  onChange: (value: number | null) => void
  onBlur?: () => void
  id?: string
  name?: string
  ref?: Ref<HTMLInputElement>
  disabled?: boolean
  className?: string
  'aria-invalid'?: boolean
}

// Πεδίο δεκαδικού αριθμού (βάση του MoneyInput / PercentInput): γράφεις όπως συνηθίζεις (1.234,50 ή 1,234.50),
// βλέπεις τη μονάδα δεξιά. value: αριθμός, null = κενό, NaN = κάτι που δεν είναι αριθμός (το απορρίπτει το schema).
// Μετά το blur ο αριθμός γράφεται ξανά στη μορφή της γλώσσας (format).
export function DecimalInput({
  value,
  onChange,
  onBlur,
  unit,
  format,
  id,
  name,
  ref,
  disabled,
  className,
  'aria-invalid': ariaInvalid,
}: DecimalInputProps & { unit: ReactNode; format: (value: number, language: string) => string }) {
  const { i18n } = useTranslation()
  const language = i18n.language
  const display = (next: number | null, lang: string) => (next === null || Number.isNaN(next) ? '' : format(next, lang))
  const [text, setText] = useState(() => display(value, language))
  const [shown, setShown] = useState({ value, language })

  // Νέα τιμή από έξω (reset της φόρμας) ή αλλαγή γλώσσας: ξαναγράφουμε το κείμενο
  if (!sameNumber(shown.value, value) || shown.language !== language) {
    setShown({ value, language })
    if (!sameNumber(parseAmount(text, language), value) || shown.language !== language) setText(display(value, language))
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
          if (parsed !== null && !Number.isNaN(parsed)) setText(format(parsed, language))
          onBlur?.()
        }}
        className="text-right tabular-nums"
      />
      <InputGroupAddon align="inline-end">
        <InputGroupText>{unit}</InputGroupText>
      </InputGroupAddon>
    </InputGroup>
  )
}

function sameNumber(a: number | null, b: number | null) {
  return a === b || (Number.isNaN(a) && Number.isNaN(b))
}
