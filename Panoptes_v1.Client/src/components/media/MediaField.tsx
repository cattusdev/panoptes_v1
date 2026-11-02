import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ImagePlus } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { hasPermission, useSession } from '@/lib/auth'
import type { MediaItem, MediaKind } from '@/lib/media-api'
import { Permissions } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { MediaImage } from './MediaImage'
import { MediaPicker } from './MediaPicker'
import { fileStyle } from './media-utils'

// Πεδίο φόρμας για ένα αρχείο της βιβλιοθήκης (όπως Sanity/Payload): προεπισκόπηση + Change / Remove.
// Η τιμή είναι το ίδιο το αρχείο· η φόρμα στέλνει μόνο το id του.
export function MediaField({
  id,
  value,
  onChange,
  accept = ['Image'],
  placeholder,
  pickerTitle,
  previewClassName,
  disabled,
}: {
  id?: string
  value: MediaItem | null
  onChange: (item: MediaItem | null) => void
  accept?: MediaKind[]
  placeholder?: string
  pickerTitle?: string
  previewClassName?: string
  disabled?: boolean
}) {
  const { t } = useTranslation('media')
  const { user } = useSession()
  const [open, setOpen] = useState(false)
  const canBrowse = hasPermission(user, Permissions.Media.View)
  const blocked = disabled || !canBrowse

  return (
    <>
      {value ? (
        <div className="flex items-center gap-4 rounded-xl border p-3">
          <div
            className={cn(
              'flex size-20 shrink-0 items-center justify-center overflow-hidden rounded-lg border bg-[repeating-conic-gradient(#ffffff0a_0_25%,transparent_0_50%)] bg-[length:12px_12px] p-1.5',
              previewClassName,
            )}
          >
            <MediaImage item={value} sizes="80px" className="max-h-full w-auto object-contain" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium">{value.title}</p>
            <p className="text-xs text-muted-foreground">
              {fileStyle(value.version.extension).label}
              {value.version.width ? ` · ${value.version.width} × ${value.version.height}` : ''}
            </p>
          </div>
          <div className="flex shrink-0 gap-1.5">
            <Button type="button" id={id} variant="outline" size="sm" onClick={() => setOpen(true)} disabled={blocked}>
              {t('field.change')}
            </Button>
            <Button type="button" variant="ghost" size="sm" onClick={() => onChange(null)} disabled={disabled}>
              {t('actions.remove', { ns: 'common' })}
            </Button>
          </div>
        </div>
      ) : (
        <button
          type="button"
          id={id}
          onClick={() => setOpen(true)}
          disabled={blocked}
          className="flex w-full items-center gap-3 rounded-xl border border-dashed p-4 text-left transition-colors hover:border-primary hover:bg-primary/5 disabled:pointer-events-none disabled:opacity-60"
        >
          <span className="flex size-10 items-center justify-center rounded-lg bg-muted text-muted-foreground">
            <ImagePlus className="size-5" />
          </span>
          <span>
            <span className="block text-sm font-medium">{placeholder ?? t('field.placeholder')}</span>
            <span className="block text-xs text-muted-foreground">{t('field.hint')}</span>
          </span>
        </button>
      )}
      {!canBrowse && !disabled && (
        <p className="mt-1.5 text-xs text-muted-foreground">{t('field.noAccess')}</p>
      )}

      <MediaPicker open={open} onOpenChange={setOpen} accept={accept} title={pickerTitle} onSelect={(items) => onChange(items[0] ?? null)} />
    </>
  )
}
