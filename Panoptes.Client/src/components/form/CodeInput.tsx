import { REGEXP_ONLY_DIGITS } from 'input-otp'
import { useTranslation } from 'react-i18next'
import { InputOTP, InputOTPGroup, InputOTPSeparator, InputOTPSlot } from '@/components/ui/input-otp'

// 6ψήφιος κωδικός (authenticator app): auto-advance, paste, μόνο αριθμοί
export function CodeInput({
  value,
  onChange,
  onComplete,
  disabled,
  autoFocus,
  invalid,
}: {
  value: string
  onChange: (value: string) => void
  onComplete?: (value: string) => void
  disabled?: boolean
  autoFocus?: boolean
  invalid?: boolean
}) {
  const { t } = useTranslation()
  return (
    <InputOTP
      maxLength={6}
      pattern={REGEXP_ONLY_DIGITS}
      value={value}
      onChange={onChange}
      onComplete={onComplete}
      disabled={disabled}
      autoFocus={autoFocus}
      autoComplete="one-time-code"
      inputMode="numeric"
      aria-label={t('code.label')}
      aria-invalid={invalid || undefined}
      data-testid="code-input"
    >
      <InputOTPGroup>
        <InputOTPSlot index={0} className="size-11 text-base" />
        <InputOTPSlot index={1} className="size-11 text-base" />
        <InputOTPSlot index={2} className="size-11 text-base" />
      </InputOTPGroup>
      <InputOTPSeparator />
      <InputOTPGroup>
        <InputOTPSlot index={3} className="size-11 text-base" />
        <InputOTPSlot index={4} className="size-11 text-base" />
        <InputOTPSlot index={5} className="size-11 text-base" />
      </InputOTPGroup>
    </InputOTP>
  )
}
