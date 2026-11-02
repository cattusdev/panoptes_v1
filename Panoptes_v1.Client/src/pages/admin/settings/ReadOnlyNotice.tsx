import { useTranslation } from 'react-i18next'
import { Eye } from 'lucide-react'
import { Alert, AlertDescription } from '@/components/ui/alert'

// Για όσους έχουν μόνο Settings · View
export function ReadOnlyNotice() {
  const { t } = useTranslation('settings')
  return (
    <Alert variant="info">
      <Eye />
      <AlertDescription>{t('readOnly')}</AlertDescription>
    </Alert>
  )
}
