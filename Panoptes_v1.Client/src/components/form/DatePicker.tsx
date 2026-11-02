import { useState, type Ref } from 'react'
import { useTranslation } from 'react-i18next'
import { el, enUS } from 'react-day-picker/locale'
import { CalendarDays } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Calendar } from '@/components/ui/calendar'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { useDateFormat } from '@/lib/dates'
import { cn } from '@/lib/utils'

const calendarLocales = { en: enUS, el } as const

// "yyyy-MM-dd" <-> Date στην τοπική ώρα (το ημερολόγιο δουλεύει με μέρες, όχι στιγμές)
function toDate(value: string) {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(year, month - 1, day)
}

function toValue(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

// Επιλογή ημερομηνίας χωρίς ώρα (DateOnly). value: "yyyy-MM-dd" ή null. Εμφανίζεται με τη μορφή του Settings → General,
// το ημερολόγιο στη γλώσσα του χρήστη (ονόματα μηνών, πρώτη μέρα της εβδομάδας).
export function DatePicker({
  value,
  onChange,
  onBlur,
  id,
  ref,
  placeholder,
  clearable,
  disabled,
  className,
  'aria-invalid': ariaInvalid,
}: {
  value: string | null
  onChange: (value: string | null) => void
  onBlur?: () => void
  id?: string
  ref?: Ref<HTMLButtonElement>
  placeholder?: string
  clearable?: boolean
  disabled?: boolean
  className?: string
  'aria-invalid'?: boolean
}) {
  const { t, i18n } = useTranslation()
  const format = useDateFormat()
  const [open, setOpen] = useState(false)
  const selected = value ? toDate(value) : undefined

  function pick(next: string | null) {
    onChange(next)
    setOpen(false)
    onBlur?.()
  }

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (!next) onBlur?.()
      }}
    >
      <PopoverTrigger asChild>
        <Button
          id={id}
          ref={ref}
          type="button"
          variant="outline"
          disabled={disabled}
          aria-invalid={ariaInvalid}
          data-empty={!value || undefined}
          className={cn('h-9 w-full max-w-56 justify-start font-normal data-[empty]:text-muted-foreground', className)}
        >
          <CalendarDays />
          {value ? format.day(value) : (placeholder ?? t('datePicker.placeholder'))}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar
          mode="single"
          selected={selected}
          defaultMonth={selected}
          captionLayout="dropdown"
          locale={calendarLocales[i18n.language as keyof typeof calendarLocales] ?? enUS}
          onSelect={(date) => date && pick(toValue(date))}
          autoFocus
        />
        <div className="flex justify-between gap-2 border-t p-2">
          <Button type="button" variant="ghost" size="sm" onClick={() => pick(toValue(new Date()))}>
            {t('datePicker.today')}
          </Button>
          {clearable && value && (
            <Button type="button" variant="ghost" size="sm" onClick={() => pick(null)}>
              {t('datePicker.clear')}
            </Button>
          )}
        </div>
      </PopoverContent>
    </Popover>
  )
}
