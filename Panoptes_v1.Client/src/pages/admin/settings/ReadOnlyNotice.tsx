import { useTranslation } from 'react-i18next'
import { Eye } from 'lucide-react'
import { Alert, AlertDescription } from '@/components/ui/alert'

// Για όσους έχουν μόνο Settings · View. message: όταν την ενότητα την αλλάζει άλλο permission (π.χ. Finance · Settings)
export function ReadOnlyNotice({ message }: { message?: string }) {
  const { t } = useTranslation('settings')
  return (
    <Alert variant="info">
      <Eye />
      <AlertDescription>{message ?? t('readOnly')}</AlertDescription>
    </Alert>
  )
}
