import { useTranslation } from 'react-i18next'
import { currencySymbol, formatAmount } from '@/lib/money'
import { DecimalInput, type DecimalInputProps } from './DecimalInput'

// Πεδίο ποσού: γράφεις όπως συνηθίζεις (1.234,50 ή 1,234.50), βλέπεις το νόμισμα δεξιά.
// value: αριθμός, null = κενό, NaN = κάτι που δεν είναι ποσό (το απορρίπτει το schema με μήνυμα).
// Μετά το blur το ποσό γράφεται ξανά στη μορφή της γλώσσας.
export function MoneyInput({ currency, ...props }: DecimalInputProps & { currency: string }) {
  const { i18n } = useTranslation()
  return <DecimalInput {...props} unit={currencySymbol(currency, i18n.language)} format={formatAmount} />
}
