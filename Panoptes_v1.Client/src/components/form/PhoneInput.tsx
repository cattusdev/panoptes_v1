import { useMemo, useState, type Ref } from 'react'
import { useTranslation } from 'react-i18next'
import { Check, ChevronDown } from 'lucide-react'
import { Command, CommandEmpty, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { InputGroup, InputGroupAddon, InputGroupButton, InputGroupInput } from '@/components/ui/input-group'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { callingCode, defaultPhoneCountry, parsePhone, phoneCountries, toE164, type CountryCode } from '@/lib/phone'
import { cn } from '@/lib/utils'

export type PhoneInputProps = {
  value: string
  onChange: (value: string) => void
  onBlur?: () => void
  id?: string
  name?: string
  ref?: Ref<HTMLInputElement>
  disabled?: boolean
  placeholder?: string
  className?: string
  'aria-invalid'?: boolean
  'aria-label'?: string
}

// Τηλέφωνο με κωδικό χώρας (Ελλάδα προεπιλογή). Ο χρήστης γράφει τον αριθμό όπως τον ξέρει ("210 1234567" ή
// "+44 20 …", που αλλάζει και τη χώρα)· value = E.164 ("+302101234567"), ή το κείμενο όσο δεν είναι αριθμός
// (το απορρίπτει το schema με isValidPhone). Μετά το blur ένας έγκυρος αριθμός γράφεται στη μορφή της χώρας του.
export function PhoneInput({
  value,
  onChange,
  onBlur,
  id,
  name,
  ref,
  disabled,
  placeholder,
  className,
  'aria-invalid': ariaInvalid,
  'aria-label': ariaLabel,
}: PhoneInputProps) {
  const [country, setCountry] = useState<CountryCode>(() => parsePhone(value)?.country ?? defaultPhoneCountry)
  const [text, setText] = useState(() => national(value))
  const [shown, setShown] = useState(value)

  // Νέα τιμή από έξω (reset της φόρμας): ξαναγράφουμε το κείμενο
  if (shown !== value) {
    setShown(value)
    if (toE164(text, country) !== value) {
      setText(national(value))
      setCountry(parsePhone(value)?.country ?? country)
    }
  }

  const emit = (nextText: string, nextCountry: CountryCode) => {
    const next = toE164(nextText, nextCountry)
    setShown(next)
    onChange(next)
  }

  return (
    <InputGroup className={className} data-disabled={disabled || undefined}>
      <InputGroupAddon align="inline-start" className="pl-1">
        <CountryPicker
          country={country}
          disabled={disabled}
          onPick={(next) => {
            setCountry(next)
            emit(text, next)
          }}
        />
      </InputGroupAddon>
      <InputGroupInput
        id={id}
        name={name}
        ref={ref}
        type="tel"
        inputMode="tel"
        autoComplete="tel"
        disabled={disabled}
        placeholder={placeholder}
        aria-invalid={ariaInvalid}
        aria-label={ariaLabel}
        value={text}
        onChange={(e) => {
          const nextText = e.target.value
          // "+44 …": η χώρα βγαίνει από τον αριθμό
          const detected = nextText.trim().startsWith('+') ? parsePhone(nextText)?.country : undefined
          const nextCountry = detected ?? country
          setText(nextText)
          if (detected) setCountry(detected)
          emit(nextText, nextCountry)
        }}
        onBlur={() => {
          const phone = parsePhone(toE164(text, country))
          if (phone?.isValid()) {
            setText(phone.formatNational())
            if (phone.country) setCountry(phone.country)
          }
          onBlur?.()
        }}
        className="tabular-nums"
      />
    </InputGroup>
  )
}

// Ο αριθμός χωρίς τον κωδικό χώρας (αυτόν τον δείχνει το κουμπί της χώρας)
function national(value: string) {
  const phone = parsePhone(value)
  return phone ? phone.formatNational() : value
}

function CountryPicker({ country, disabled, onPick }: { country: CountryCode; disabled?: boolean; onPick: (country: CountryCode) => void }) {
  const { t, i18n } = useTranslation()
  const [open, setOpen] = useState(false)
  const countries = useMemo(() => phoneCountries(i18n.language), [i18n.language])
  const current = countries.find((c) => c.code === country)

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <InputGroupButton
          size="xs"
          disabled={disabled}
          aria-label={t('phone.country', { country: current?.name ?? country, code: callingCode(country) })}
          className="font-normal tabular-nums"
        >
          <span className="text-xs text-muted-foreground">{country}</span>
          {callingCode(country)}
          <ChevronDown className="size-3 text-muted-foreground" />
        </InputGroupButton>
      </PopoverTrigger>
      <PopoverContent className="w-72 p-0" align="start">
        <Command>
          <CommandInput placeholder={t('phone.searchCountry')} />
          <CommandList>
            <CommandEmpty>{t('picker.empty')}</CommandEmpty>
            {countries.map((c) => (
              <CommandItem
                key={c.code}
                value={`${c.name} ${c.code} ${c.callingCode}`}
                onSelect={() => {
                  onPick(c.code)
                  setOpen(false)
                }}
              >
                <span className="w-7 shrink-0 text-xs text-muted-foreground">{c.code}</span>
                <span className="truncate">{c.name}</span>
                <span className="ml-auto text-muted-foreground tabular-nums">{c.callingCode}</span>
                <Check className={cn('size-4', c.code === country ? 'opacity-100' : 'opacity-0')} />
              </CommandItem>
            ))}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
