import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'

// Επιβεβαίωση για επικίνδυνες ενέργειες. Μένει ανοιχτό αν το onConfirm αποτύχει.
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  destructive,
  onConfirm,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: ReactNode
  confirmLabel: string
  destructive?: boolean
  onConfirm: () => Promise<unknown>
}) {
  const { t } = useTranslation()
  const [pending, setPending] = useState(false)

  async function confirm() {
    setPending(true)
    try {
      await onConfirm()
      onOpenChange(false)
    } catch {
      // Το μήνυμα λάθους το δείχνει ο caller (toast)
    } finally {
      setPending(false)
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={(value) => !pending && onOpenChange(value)}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>{t('actions.cancel')}</AlertDialogCancel>
          <Button variant={destructive ? 'destructive' : 'default'} onClick={confirm} disabled={pending}>
            {pending && <Spinner />}
            {confirmLabel}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
