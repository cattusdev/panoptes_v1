import { useTranslation } from 'react-i18next'
import { Alert, AlertDescription } from '@/components/ui/alert'

// Για όσους έχουν μόνο Settings · View
export function ReadOnlyNotice() {
  const { t } = useTranslation('settings')
  return (
    <Alert>
      <AlertDescription>{t('readOnly')}</AlertDescription>
    </Alert>
  )
}
