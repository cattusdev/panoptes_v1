import { useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Download, FileText, Library, Unlink, Upload } from 'lucide-react'
import { TableEmpty } from '@/components/admin/Common'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { DataTable, type Column } from '@/components/admin/DataTable'
import { DateTime } from '@/components/admin/DateTime'
import { MediaUploader } from '@/components/media/LazyMediaUploader'
import { MediaPicker } from '@/components/media/MediaPicker'
import { MediaThumb } from '@/components/media/MediaThumb'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Spinner } from '@/components/ui/spinner'
import { problemMessage } from '@/lib/api'
import { formatBytes } from '@/lib/media-api'
import { useAttachClientFile, useDetachClientFile, useClientFiles, type Client, type RecordFile } from '../api'
import { useClientFileAbilities } from '../file-abilities'

// Ανέβασμα από την καρτέλα: κάθε αρχείο μπαίνει στη βιβλιοθήκη (ιδιωτικό) και συνδέεται αμέσως με τον πελάτη.
// Undo: πρώτα αποσύνδεση, μετά αναίρεση του upload.
export function ClientUploadDialog({ client, open, onOpenChange }: { client: Client; open: boolean; onOpenChange: (open: boolean) => void }) {
  const { t } = useTranslation('crm')
  const attach = useAttachClientFile(client.id)
  const detach = useDetachClientFile(client.id)
  const [busy, setBusy] = useState(false)
  const [count, setCount] = useState(0)

  function close(next: boolean) {
    if (!next && busy) {
      toast.info(t('files.upload.busy'))
      return
    }
    if (!next) setCount(0)
    onOpenChange(next)
  }

  return (
    <Dialog open={open} onOpenChange={close}>
      <DialogContent className="sm:max-w-lg" onInteractOutside={(e) => busy && e.preventDefault()}>
        <DialogHeader>
          <DialogTitle>{t('files.upload.title')}</DialogTitle>
          <DialogDescription>{t('files.upload.description', { client: client.displayName })}</DialogDescription>
        </DialogHeader>
        {open && (
          <MediaUploader
            target={{ folderId: null, visibility: 'Private' }}
            dropAnywhere
            onBusyChange={setBusy}
            onUploaded={async (item) => {
              try {
                await attach.mutateAsync(item.id)
                setCount((c) => c + 1)
              } catch (error) {
                toast.error(t('files.upload.attachFailed', { file: item.title, message: problemMessage(error) }))
              }
            }}
            beforeRevert={(item) => detach.mutateAsync(item.id)}
            onReverted={() => setCount((c) => Math.max(0, c - 1))}
          />
        )}
        <DialogFooter>
          <Button onClick={() => close(false)} disabled={busy}>
            {busy && <Spinner />}
            {busy ? t('files.upload.uploading') : count > 0 ? t('files.upload.doneCount', { count }) : t('files.upload.done')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

// Τα αρχεία του πελάτη: όνομα, με τι είναι συνδεδεμένο, πότε ανέβηκε, από ποιον. Η αποσύνδεση λέει ότι σβήνει μόνο τη σύνδεση.
export function ClientFilesList({ client, onUpload }: { client: Client; onUpload: () => void }) {
  const { t } = useTranslation('crm')
  const abilities = useClientFileAbilities(client)
  const files = useClientFiles(client.id, abilities.canView)
  const attach = useAttachClientFile(client.id)
  const detach = useDetachClientFile(client.id)
  const [pickerOpen, setPickerOpen] = useState(false)
  const [unlinking, setUnlinking] = useState<RecordFile | null>(null)

  async function attachPicked(ids: string[]) {
    let attached = 0
    for (const id of ids) {
      try {
        await attach.mutateAsync(id)
        attached++
      } catch (error) {
        toast.error(problemMessage(error))
      }
    }
    if (attached > 0) toast.success(t('files.attached', { count: attached }))
  }

  const columns: Column<RecordFile>[] = [
    {
      key: 'name',
      header: t('files.columns.name'),
      cell: (f) => (
        <div className="flex min-w-0 items-center gap-3">
          <MediaThumb item={f.item} className="size-10 shrink-0 rounded-md" />
          <div className="min-w-0">
            <a href={f.item.version.urls.original} target="_blank" rel="noopener noreferrer" className="block truncate font-medium hover:underline">
              {f.item.title}
            </a>
            <div className="text-xs text-muted-foreground">
              {f.item.version.extension.toUpperCase()} · {formatBytes(f.item.version.sizeBytes)}
            </div>
          </div>
        </div>
      ),
    },
    {
      key: 'linkedTo',
      header: t('files.columns.linkedTo'),
      cell: () => <span className="text-sm text-muted-foreground">{t('files.thisClient')}</span>,
    },
    {
      key: 'uploaded',
      header: t('files.columns.uploaded'),
      className: 'w-36',
      cell: (f) => <DateTime value={f.item.createdAt} mode="date" className="text-sm text-muted-foreground" />,
    },
    {
      key: 'by',
      header: t('files.columns.by'),
      cell: (f) => <span className="text-sm text-muted-foreground">{f.item.ownerEmail ?? '—'}</span>,
    },
    {
      key: 'actions',
      header: <span className="sr-only">{t('files.columns.actions')}</span>,
      className: 'w-24 text-right',
      cell: (f) => (
        <div className="flex justify-end gap-1">
          <Button asChild variant="ghost" size="icon" aria-label={t('files.download', { file: f.item.title })}>
            <a href={f.item.version.urls.download}>
              <Download />
            </a>
          </Button>
          {abilities.canUnlink && (
            <Button variant="ghost" size="icon" aria-label={t('files.unlinkFile', { file: f.item.title })} onClick={() => setUnlinking(f)}>
              <Unlink />
            </Button>
          )}
        </div>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-3">
      {(abilities.canUpload || abilities.canAttach) && (
        <div className="flex flex-wrap justify-end gap-2">
          {abilities.canAttach && (
            <Button variant="outline" onClick={() => setPickerOpen(true)}>
              <Library /> {t('files.attachFromLibrary')}
            </Button>
          )}
          {abilities.canUpload && (
            <Button onClick={onUpload}>
              <Upload /> {t('files.upload.button')}
            </Button>
          )}
        </div>
      )}

      <DataTable
        columns={columns}
        rows={files.data}
        getRowId={(f) => `${f.entityType}:${f.entityId}:${f.item.id}`}
        rowLabel={(f) => f.item.title}
        isLoading={files.isPending}
        empty={<TableEmpty icon={FileText} title={t('files.empty.title')} description={t('files.empty.description')} />}
      />

      <MediaPicker
        open={pickerOpen}
        onOpenChange={setPickerOpen}
        multiple
        title={t('files.pickerTitle')}
        description={t('files.pickerDescription', { client: client.displayName })}
        onSelect={(items) => void attachPicked(items.map((i) => i.id))}
      />

      <ConfirmDialog
        open={!!unlinking}
        onOpenChange={(open) => !open && setUnlinking(null)}
        title={t('files.unlinkConfirm.title')}
        description={
          <Trans
            t={t}
            i18nKey="files.unlinkConfirm.description"
            values={{ file: unlinking?.item.title, client: client.displayName }}
            components={{ strong: <strong /> }}
          />
        }
        confirmLabel={t('files.unlink')}
        onConfirm={async () => {
          if (!unlinking) return
          try {
            await detach.mutateAsync(unlinking.item.id)
            toast.success(t('files.unlinked', { file: unlinking.item.title }))
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />
    </div>
  )
}
