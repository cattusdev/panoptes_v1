import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSession } from '@/lib/auth'
import { useGeneralSettings } from '@/lib/settings-api'
import { resolveLanguage, setLanguage } from '@/i18n'

// Κρατάει τη γλώσσα συγχρονισμένη με τον χρήστη και τις ρυθμίσεις.
// Περιμένει τα πραγματικά δεδομένα (όχι placeholders): μέχρι τότε μένει η τελευταία γλώσσα που δείξαμε.
export function useLanguageSync() {
  const { i18n } = useTranslation()
  const session = useSession()
  const settings = useGeneralSettings()
  const ready = session.isSuccess && settings.isSuccess && !settings.isPlaceholderData
  const target = ready ? resolveLanguage(session.user?.language, settings.data.defaultLanguage) : null

  useEffect(() => {
    if (target && target !== i18n.language) void setLanguage(target)
  }, [target, i18n.language])
}
