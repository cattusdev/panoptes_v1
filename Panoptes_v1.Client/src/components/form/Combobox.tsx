import { useState, type ReactNode, type Ref } from 'react'
import { useTranslation } from 'react-i18next'
import { Check, ChevronsUpDown, X } from 'lucide-react'
import type { Option } from '@/components/admin/OptionSelect'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { cn } from '@/lib/utils'

type PickerProps = {
  options: Option[]
  id?: string
  ref?: Ref<HTMLButtonElement>
  placeholder?: string
  searchPlaceholder?: string
  emptyText?: string
  onBlur?: () => void
  disabled?: boolean
  className?: string
  'aria-invalid'?: boolean
}

// Λίστα επιλογών με αναζήτηση (για λίστες που μεγαλώνουν: πελάτες, έργα, χρήστες). Για λίγες σταθερές τιμές: OptionSelect.
export function Combobox({ value, onChange, ...props }: PickerProps & { value: string | null; onChange: (value: string | null) => void }) {
  const selected = props.options.find((option) => option.value === value)
  return (
    <Picker
      {...props}
      isSelected={(option) => option.value === value}
      onPick={(option, close) => {
        onChange(option.value === value ? null : option.value)
        close()
      }}
      label={selected?.label}
    />
  )
}

// Πολλαπλή επιλογή με αναζήτηση: οι επιλογές φαίνονται ως badges στο κουμπί, το μενού μένει ανοιχτό
export function MultiSelect({ value, onChange, ...props }: PickerProps & { value: string[]; onChange: (value: string[]) => void }) {
  const { t } = useTranslation()
  const selected = props.options.filter((option) => value.includes(option.value))
  const shown = selected.slice(0, 3)

  return (
    <Picker
      {...props}
      isSelected={(option) => value.includes(option.value)}
      onPick={(option) =>
        onChange(value.includes(option.value) ? value.filter((v) => v !== option.value) : [...value, option.value])
      }
      label={
        selected.length > 0 && (
          <span className="flex min-w-0 flex-wrap gap-1">
            {shown.map((option) => (
              <Badge key={option.value} variant="muted">
                {option.label}
              </Badge>
            ))}
            {selected.length > shown.length && <Badge variant="muted">{t('picker.more', { count: selected.length - shown.length })}</Badge>}
          </span>
        )
      }
      onClear={selected.length > 0 ? () => onChange([]) : undefined}
    />
  )
}

function Picker({
  options,
  id,
  ref,
  placeholder,
  searchPlaceholder,
  emptyText,
  onBlur,
  disabled,
  className,
  'aria-invalid': ariaInvalid,
  isSelected,
  onPick,
  label,
  onClear,
}: PickerProps & {
  isSelected: (option: Option) => boolean
  onPick: (option: Option, close: () => void) => void
  label: ReactNode
  onClear?: () => void
}) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)

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
          role="combobox"
          aria-expanded={open}
          aria-invalid={ariaInvalid}
          disabled={disabled}
          className={cn('h-auto min-h-9 w-full justify-between py-1.5 font-normal', className)}
        >
          {label || <span className="text-muted-foreground">{placeholder ?? t('picker.placeholder')}</span>}
          <ChevronsUpDown className="ml-auto text-muted-foreground" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-(--radix-popover-trigger-width) min-w-56 p-0" align="start">
        <Command>
          <CommandInput placeholder={searchPlaceholder ?? t('picker.search')} />
          <CommandList>
            <CommandEmpty>{emptyText ?? t('picker.empty')}</CommandEmpty>
            <CommandGroup>
              {options.map((option) => (
                <CommandItem
                  key={option.value}
                  value={`${option.label} ${option.value}`}
                  disabled={option.disabled}
                  onSelect={() => onPick(option, () => setOpen(false))}
                  data-checked={isSelected(option) || undefined}
                >
                  <Check className={cn(isSelected(option) ? 'opacity-100' : 'opacity-0')} />
                  {option.label}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
          {onClear && (
            <div className="border-t p-1">
              <Button type="button" variant="ghost" size="sm" className="w-full justify-start" onClick={onClear}>
                <X /> {t('picker.clear')}
              </Button>
            </div>
          )}
        </Command>
      </PopoverContent>
    </Popover>
  )
}
