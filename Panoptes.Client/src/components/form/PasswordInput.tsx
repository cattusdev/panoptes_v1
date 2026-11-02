import { useState, type ComponentProps } from 'react'
import { useTranslation } from 'react-i18next'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'

// Password πεδίο με κουμπί Show / Hide
export function PasswordInput({ className, ...props }: Omit<ComponentProps<typeof Input>, 'type'>) {
  const { t } = useTranslation()
  const [visible, setVisible] = useState(false)

  return (
    <div className="relative">
      <Input type={visible ? 'text' : 'password'} className={cn('pr-16', className)} {...props} />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        aria-label={visible ? t('password.hidePassword') : t('password.showPassword')}
        className="absolute top-1/2 right-2 -translate-y-1/2 rounded-md px-2 py-1 text-xs font-medium text-muted-foreground hover:bg-muted hover:text-foreground"
      >
        {visible ? t('actions.hide') : t('actions.show')}
      </button>
    </div>
  )
}
