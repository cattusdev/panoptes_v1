import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { cn } from '@/lib/utils'

const EMPTY = '__empty' // Το Radix Select δεν δέχεται "" ως τιμή

export type Option = { value: string; label: string; disabled?: boolean }

// shadcn Select με απλό API: value "" = η "κενή" επιλογή (π.χ. "All roles" ή "No role")
export function OptionSelect({
  id,
  value,
  onChange,
  options,
  emptyLabel,
  ariaLabel,
  disabled,
  className,
}: {
  id?: string
  value: string
  onChange: (value: string) => void
  options: Option[]
  emptyLabel?: string
  ariaLabel?: string
  disabled?: boolean
  className?: string
}) {
  return (
    <Select value={value || EMPTY} onValueChange={(v) => onChange(v === EMPTY ? '' : v)} disabled={disabled}>
      <SelectTrigger id={id} aria-label={ariaLabel} className={cn('h-9 w-full', className)}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent position="popper">
        {emptyLabel && <SelectItem value={EMPTY}>{emptyLabel}</SelectItem>}
        {options.map((option) => (
          <SelectItem key={option.value} value={option.value} disabled={option.disabled}>
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
