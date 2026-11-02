import { formatPercentNumber } from '@/lib/money'
import { DecimalInput, type DecimalInputProps } from './DecimalInput'

// Πεδίο ποσοστού (π.χ. ΦΠΑ): 24 ή 13,5 / 13.5, με το % δεξιά. Έως δύο δεκαδικά, όπως ο server.
export function PercentInput(props: DecimalInputProps) {
  return <DecimalInput {...props} unit="%" format={formatPercentNumber} />
}
