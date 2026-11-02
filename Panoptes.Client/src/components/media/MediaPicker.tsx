import { useCallback, useRef, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { hasPermission, useSession } from '@/lib/auth'
import { folderPath, useInvalidateMedia, useMediaFolders, type MediaItem, type MediaKind } from '@/lib/media-api'
import { Permissions } from '@/lib/permissions'
import { useLocalBrowserState } from './browser-state'
import { MediaUploader } from './LazyMediaUploader'
import { MediaBrowser } from './MediaBrowser'

export type MediaPickerProps = {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSelect: (items: MediaItem[]) => void
  accept?: MediaKind[]
  multiple?: boolean
  title?: string
  description?: string
}

// Επιλογή αρχείων από τη βιβλιοθήκη (ή ανέβασμα νέου), για οποιαδήποτε φόρμα της εφαρμογής.
// Ίδια βιβλιοθήκη με τη σελίδα Media (φάκελοι, αναζήτηση, φίλτρα), σε dialog.
export function MediaPicker(props: MediaPickerProps) {
  return (
    <Dialog open={props.open} onOpenChange={props.onOpenChange}>
      <DialogContent className="flex h-[85vh] flex-col gap-0 p-0 sm:max-w-6xl">
        {props.open && <PickerBody {...props} />}
      </DialogContent>
    </Dialog>
  )
}

function PickerBody({ onOpenChange, onSelect, accept, multiple = false, title, description }: MediaPickerProps) {
  const { t } = useTranslation('media')
  const { user } = useSession()
  const state = useLocalBrowserState()
  const folders = useMediaFolders()
  const invalidate = useInvalidateMedia()
  const [tab, setTab] = useState<'library' | 'upload'>('library')
  const [picked, setPicked] = useState<MediaItem[]>([])
  const known = useRef(new Map<string, MediaItem>())

  const canUpload = hasPermission(user, Permissions.Media.Upload)
  const folderName = folderPath(folders.data, state.folderId).at(-1)?.name ?? t('library')
  const images = accept?.length === 1 && accept[0] === 'Image'
  const defaultTitle = images
    ? t(multiple ? 'picker.chooseImages' : 'picker.chooseImage')
    : t(multiple ? 'picker.chooseFiles' : 'picker.chooseFile')

  const remember = useCallback((items: MediaItem[]) => {
    for (const item of items) known.current.set(item.id, item)
  }, [])

  function pick(item: MediaItem) {
    remember([item])
    setPicked((current) => {
      const has = current.some((p) => p.id === item.id)
      if (multiple) return has ? current.filter((p) => p.id !== item.id) : [...current, item]
      return has ? [] : [item]
    })
  }

  // Από τα checkboxes (μόνο ids)
  const onSelectedChange = useCallback(
    (ids: string[]) => {
      const items = ids.map((id) => known.current.get(id)).filter((i): i is MediaItem => !!i)
      setPicked(multiple ? items : items.slice(-1))
    },
    [multiple],
  )

  function confirm() {
    onSelect(picked)
    onOpenChange(false)
  }

  return (
    <>
      <DialogHeader className="border-b px-6 pt-6 pb-0">
        <DialogTitle>{title ?? defaultTitle}</DialogTitle>
        <DialogDescription>{description ?? t('picker.description')}</DialogDescription>
        <Tabs value={tab} onValueChange={(v) => setTab(v as 'library' | 'upload')} className="mt-3">
          <TabsList variant="line">
            <TabsTrigger value="library">{t('library')}</TabsTrigger>
            {canUpload && <TabsTrigger value="upload">{t('actions.upload', { ns: 'common' })}</TabsTrigger>}
          </TabsList>
        </Tabs>
      </DialogHeader>

      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        {tab === 'library' ? (
          <MediaBrowser
            state={state}
            mode="pick"
            accept={accept}
            selected={picked.map((p) => p.id)}
            onSelectedChange={onSelectedChange}
            onOpenItem={pick}
            onItemsLoaded={remember}
          />
        ) : (
          <div className="mx-auto max-w-xl space-y-3 pt-6">
            <p className="text-sm text-muted-foreground">
              <Trans t={t} i18nKey="picker.uploadTarget" values={{ folder: folderName }} components={{ b: <strong className="text-foreground" /> }} />
            </p>
            <MediaUploader
              target={{ folderId: state.folderId, visibility: 'Private' }}
              accept={accept}
              multiple={multiple}
              dropAnywhere
              onUploaded={(item) => {
                void invalidate()
                remember([item])
                setPicked((current) => (multiple ? [...current, item] : [item]))
              }}
            />
          </div>
        )}
      </div>

      <DialogFooter className="mx-0 mb-0 items-center px-6 py-4 sm:justify-between">
        <p className="text-sm text-muted-foreground" aria-live="polite">
          {picked.length === 0
            ? t(images ? 'picker.noImageSelected' : 'picker.noFileSelected')
            : multiple
              ? t('selected', { count: picked.length })
              : t('picker.selectedItem', { title: picked[0].title })}
        </p>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t('actions.cancel', { ns: 'common' })}
          </Button>
          <Button onClick={confirm} disabled={picked.length === 0}>
            {t('actions.select', { ns: 'common' })}
          </Button>
        </div>
      </DialogFooter>
    </>
  )
}
