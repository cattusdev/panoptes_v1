import { useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { formatBytes, useInvalidateMedia, useMediaSettings, type MediaItem, type MediaVisibility } from '@/lib/media-api'
import { MediaUploader } from './LazyMediaUploader'

const visibilities: MediaVisibility[] = ['Private', 'Public']

// Ανέβασμα πολλών αρχείων στον τρέχοντα φάκελο. Όσο ανεβαίνουν, το dialog δεν κλείνει (θα ακυρώνονταν).
export function UploadDialog({
  open,
  onOpenChange,
  folderId,
  folderName,
  initialFiles,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  folderId: string | null
  folderName: string
  initialFiles?: File[]
}) {
  const { t } = useTranslation('media')
  const invalidate = useInvalidateMedia()
  const settings = useMediaSettings()
  const [visibility, setVisibility] = useState<MediaVisibility>('Private')
  const [busy, setBusy] = useState(false)
  const [count, setCount] = useState(0)

  function close(next: boolean) {
    if (!next && busy) {
      toast.info(t('upload.busyTitle'), { description: t('upload.busyDescription') })
      return
    }
    if (!next) {
      setCount(0)
      setBusy(false)
    }
    onOpenChange(next)
  }

  const limits = settings.data?.kinds.map((k) => t(`upload.limits.${k.kind}`, { size: formatBytes(k.maxBytes) })).join(', ')
  const visibilityOptions = visibilities.map((value) => ({ value, label: t(`upload.visibilityOptions.${value}`) }))

  return (
    <Dialog open={open} onOpenChange={close}>
      <DialogContent className="sm:max-w-lg" onInteractOutside={(e) => busy && e.preventDefault()}>
        <DialogHeader>
          <DialogTitle>{t('upload.title')}</DialogTitle>
          <DialogDescription>
            <Trans t={t} i18nKey="upload.description" values={{ folder: folderName }} components={{ b: <strong className="text-foreground" /> }} />
          </DialogDescription>
        </DialogHeader>

        <Field>
          <FieldLabel htmlFor="upload-visibility">{t('upload.whoCanView')}</FieldLabel>
          <OptionSelect id="upload-visibility" value={visibility} onChange={(v) => setVisibility(v as MediaVisibility)} options={visibilityOptions} />
        </Field>

        {open && (
          <MediaUploader
            target={{ folderId, visibility }}
            initialFiles={initialFiles}
            dropAnywhere
            onBusyChange={setBusy}
            onUploaded={() => {
              setCount((c) => c + 1)
              void invalidate()
            }}
            onReverted={() => {
              setCount((c) => Math.max(0, c - 1))
              void invalidate()
            }}
          />
        )}
        {limits && <FieldDescription className="-mt-2">{t('upload.maxSize', { limits })}</FieldDescription>}

        <DialogFooter>
          <Button onClick={() => close(false)} disabled={busy}>
            {busy && <Spinner />}
            {busy ? t('upload.uploading') : count > 0 ? t('upload.doneCount', { count }) : t('upload.done')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

// Νέα έκδοση ενός αρχείου (ίδιο είδος). Το αρχείο κρατά το ίδιο id: όπου χρησιμοποιείται, ενημερώνεται.
export function ReplaceFileDialog({ item, onOpenChange }: { item: MediaItem | null; onOpenChange: (open: boolean) => void }) {
  const { t } = useTranslation('media')
  const invalidate = useInvalidateMedia()
  const [busy, setBusy] = useState(false)

  return (
    <Dialog open={!!item} onOpenChange={(next) => (!next && busy ? undefined : onOpenChange(next))}>
      <DialogContent className="sm:max-w-md" onInteractOutside={(e) => busy && e.preventDefault()}>
        <DialogHeader>
          <DialogTitle>{t('replaceFile')}</DialogTitle>
          <DialogDescription>
            <Trans t={t} i18nKey="upload.replaceDescription" values={{ title: item?.title }} components={{ b: <strong className="text-foreground" /> }} />
          </DialogDescription>
        </DialogHeader>
        {item && (
          <MediaUploader
            key={item.id}
            target={{ replaceItemId: item.id }}
            multiple={false}
            accept={[item.kind]}
            dropAnywhere
            onBusyChange={setBusy}
            onUploaded={() => {
              void invalidate()
              toast.success(t('upload.replaced'))
              onOpenChange(false)
            }}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}
