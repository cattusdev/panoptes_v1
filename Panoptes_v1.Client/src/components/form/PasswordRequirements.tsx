import { useTranslation } from 'react-i18next'
import { Check, Circle } from 'lucide-react'
import { usePasswordPolicy } from '@/lib/account-api'
import { passwordRules } from '@/lib/password'
import { cn } from '@/lib/utils'

// Live checklist με τους κανόνες του server (όπως στο Clerk / Vercel sign-up)
export function PasswordRequirements({ password }: { password: string }) {
  const { t } = useTranslation()
  const { data: policy } = usePasswordPolicy()
  if (!policy) return null

  return (
    <ul className="grid gap-1 text-xs sm:grid-cols-2" aria-label={t('password.requirements')}>
      {passwordRules(policy).map((rule) => {
        const met = rule.test(password)
        return (
          <li key={rule.label} className={cn('flex items-center gap-1.5', met ? 'text-success' : 'text-muted-foreground')}>
            {met ? <Check className="size-3.5" /> : <Circle className="size-3" />}
            {rule.label}
          </li>
        )
      })}
    </ul>
  )
}
