import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Copy, Download } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useBranding } from '@/lib/branding'

// Οι recovery codes εμφανίζονται ΜΙΑ φορά: Copy / Download για να τους φυλάξει ο χρήστης
export function RecoveryCodes({ codes }: { codes: string[] }) {
  const { t } = useTranslation('account')
  const { appName } = useBranding()
  const text = codes.join('\n')

  async function copy() {
    try {
      await navigator.clipboard.writeText(text)
      toast.success(t('recoveryCodes.copied'))
    } catch {
      toast.error(t('recoveryCodes.copyFailed'))
    }
  }

  function download() {
    const url = URL.createObjectURL(new Blob([`${t('recoveryCodes.fileHeader', { appName })}\n\n${text}\n`], { type: 'text/plain' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'panoptes-recovery-codes.txt'
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div>
      <ul className="grid grid-cols-2 gap-x-6 gap-y-1.5 rounded-lg border bg-muted/40 p-4 font-mono text-sm" data-testid="recovery-codes">
        {codes.map((code) => (
          <li key={code}>{code}</li>
        ))}
      </ul>
      <div className="mt-3 flex gap-2">
        <Button type="button" variant="outline" size="sm" onClick={copy}>
          <Copy /> {t('actions.copy', { ns: 'common' })}
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={download}>
          <Download /> {t('actions.download', { ns: 'common' })}
        </Button>
      </div>
    </div>
  )
}
