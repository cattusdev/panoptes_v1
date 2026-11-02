import { useTranslation } from 'react-i18next'
import { formatMoney } from '@/lib/money'
import { cn } from '@/lib/utils'

// Ένα ποσό για ανάγνωση (docs/ux/README.md → Money): πάντα με νόμισμα, tabular αριθμοί.
// null = άγνωστο ("Unknown", όχι €0.00). credit: η λέξη "Credit" δίπλα (ποτέ μόνο χρώμα· το ποσό έρχεται με το πρόσημό του).
export function MoneyText({
  amount,
  currency,
  credit,
  className,
}: {
  amount: number | null
  currency: string
  credit?: boolean
  className?: string
}) {
  const { t, i18n } = useTranslation()

  if (amount === null) return <span className={cn('text-muted-foreground italic', className)}>{t('money.unknown')}</span>

  return (
    <span className={cn('whitespace-nowrap tabular-nums', className)}>
      {formatMoney(amount, currency, i18n.language)}
      {credit && <span className="ml-1.5 text-muted-foreground">{t('money.credit')}</span>}
    </span>
  )
}
