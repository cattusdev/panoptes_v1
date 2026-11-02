import { useTranslation } from 'react-i18next'
import { Globe } from 'lucide-react'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { currentLanguage, languages, setLanguage, setVisitorChoice, type Language } from '@/i18n'
import { useSession, useUpdateLanguage } from '@/lib/auth'
import { cn } from '@/lib/utils'

// Μικρή επιλογή γλώσσας για τα footers (όπως Vercel / Stripe). Συνδεδεμένος χρήστης: αποθηκεύεται στο προφίλ του,
// επισκέπτης: στον browser.
export function LanguageSwitcher({ className }: { className?: string }) {
  const { t } = useTranslation()
  const { user } = useSession()
  const update = useUpdateLanguage()

  async function change(value: string) {
    const language = value as Language
    if (user) await update.mutateAsync(language)
    else setVisitorChoice(language)
    await setLanguage(language)
  }

  return (
    <Select value={currentLanguage()} onValueChange={(value) => void change(value)}>
      <SelectTrigger
        aria-label={t('language.choose')}
        data-testid="language-switcher"
        className={cn('h-8 w-auto gap-1.5 border-none bg-transparent px-2 text-xs text-muted-foreground shadow-none hover:text-foreground', className)}
      >
        <Globe className="size-3.5" />
        <SelectValue />
      </SelectTrigger>
      <SelectContent position="popper" align="center">
        {languages.map((language) => (
          <SelectItem key={language.code} value={language.code}>
            {language.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
