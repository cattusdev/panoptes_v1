import { useCallback, useState, type DragEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { FolderInput, FolderPlus, RotateCcw, Trash2, Upload, X } from 'lucide-react'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { PageHeader } from '@/components/admin/PageHeader'
import { useUrlBrowserState } from '@/components/media/browser-state'
import { FolderNameDialog, MoveDialog } from '@/components/media/FolderDialogs'
import type { FolderAction } from '@/components/media/FolderTree'
import { MediaBrowser } from '@/components/media/MediaBrowser'
import { MediaDetailsSheet } from '@/components/media/MediaDetailsSheet'
import { ReplaceFileDialog, UploadDialog } from '@/components/media/UploadDialog'
import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import { problemMessage } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import {
  fetchFolderSummary,
  folderPath,
  useBulkMediaAction,
  useDeleteFolder,
  useEmptyTrash,
  useMediaFolders,
  useMoveFolder,
  useMoveMedia,
  type MediaFolder,
  type MediaItem,
} from '@/lib/media-api'
import { Permissions } from '@/lib/permissions'

export function MediaPage() {
  const { t } = useTranslation('media')
  const { user } = useSession()
  const state = useUrlBrowserState()
  const folders = useMediaFolders()
  const move = useMoveMedia()
  const moveFolder = useMoveFolder()
  const deleteFolder = useDeleteFolder()
  const bulk = useBulkMediaAction()
  const emptyTrash = useEmptyTrash()

  const [selected, setSelected] = useState<string[]>([])
  const [detailsId, setDetailsId] = useState<string | null>(null)
  const [replaceItem, setReplaceItem] = useState<MediaItem | null>(null)
  const [upload, setUpload] = useState<{ files?: File[] } | null>(null)
  const [folderDialog, setFolderDialog] = useState<{ folder?: MediaFolder } | null>(null)
  const [movingFolder, setMovingFolder] = useState<MediaFolder | null>(null)
  const [movingItems, setMovingItems] = useState<string[] | null>(null)
  const [deleting, setDeleting] = useState<{ folder: MediaFolder; files: number; folders: number } | null>(null)
  const [purging, setPurging] = useState<string[] | null>(null)
  const [emptying, setEmptying] = useState(false)
  const [dragging, setDragging] = useState(false)

  const canUpload = hasPermission(user, Permissions.Media.Upload) && !state.trash
  const canDelete = hasPermission(user, Permissions.Media.Delete)
  const managesAll = hasPermission(user, Permissions.Media.ManageAll)
  const path = folderPath(folders.data, state.folderId)
  const currentName = path.at(-1)?.name ?? t('library')
  const nameOf = (folderId: string | null) => folderPath(folders.data, folderId).at(-1)?.name ?? t('library')
  const onSelectedChange = useCallback((ids: string[]) => setSelected(ids), [])

  // "This folder contains 3 files and 1 subfolder." (ολόκληρη πρόταση ανά περίπτωση)
  function folderContents(files: number, subfolders: number) {
    if (files > 0 && subfolders > 0)
      return t('page.folderContainsBoth', { files: t('page.fileCount', { count: files }), folders: t('page.subfolderCount', { count: subfolders }) })
    return files > 0 ? t('page.folderContainsFiles', { count: files }) : t('page.folderContainsFolders', { count: subfolders })
  }

  async function moveItems(ids: string[], folderId: string | null) {
    await move.mutateAsync({ ids, folderId })
    toast.success(t('page.moved', { count: ids.length, folder: nameOf(folderId) }))
    setSelected([])
  }

  async function runBulk(action: 'trash' | 'restore' | 'purge', ids: string[]) {
    try {
      await bulk.mutateAsync({ action, ids })
      setSelected([])
      if (action === 'trash') {
        toast.success(t('page.trashed', { count: ids.length }), {
          action: { label: t('actions.undo', { ns: 'common' }), onClick: () => void bulk.mutateAsync({ action: 'restore', ids }) },
        })
      } else {
        toast.success(action === 'restore' ? t('page.restored', { count: ids.length }) : t('page.purged', { count: ids.length }))
      }
      return true
    } catch (error) {
      toast.error(problemMessage(error))
      return false
    }
  }

  async function onFolderAction(action: FolderAction, folder: MediaFolder) {
    if (action === 'rename') return setFolderDialog({ folder })
    if (action === 'move') return setMovingFolder(folder)

    try {
      const summary = await fetchFolderSummary(folder.id)
      if (summary.files === 0 && summary.folders === 0) {
        await removeFolder(folder, false)
      } else {
        setDeleting({ folder, ...summary })
      }
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  async function removeFolder(folder: MediaFolder, withContents: boolean) {
    await deleteFolder.mutateAsync({ id: folder.id, withContents })
    toast.success(t('page.folderDeleted', { name: folder.name }), withContents ? { description: t('page.folderDeletedDescription') } : undefined)
    // Αν ήμασταν μέσα του (ή σε υποφάκελο), πάμε στον γονέα του
    if (path.some((f) => f.id === folder.id)) state.openFolder(folder.parentId)
  }

  // Αρχεία από τον υπολογιστή: drop οπουδήποτε στη σελίδα ανεβαίνουν στον τρέχοντα φάκελο.
  // Τα dialogs/sheets είναι σε portal, αλλά τα React events τους φτάνουν κι εδώ: μετράει μόνο ό,τι σέρνεται πάνω στην ίδια τη σελίδα
  // (αλλιώς το overlay άνοιγε πάνω από το Upload dialog και, με το modal ανοιχτό, δεν έκλεινε ποτέ).
  const filesOverPage = (e: DragEvent) => e.dataTransfer.types.includes('Files') && e.currentTarget.contains(e.target as Node)
  const pageDrop = canUpload
    ? {
        onDragEnter: (e: DragEvent) => filesOverPage(e) && setDragging(true),
        onDragOver: (e: DragEvent) => filesOverPage(e) && e.preventDefault(),
      }
    : {}

  return (
    <div {...pageDrop}>
      <PageHeader
        title={t('nav.media', { ns: 'common' })}
        description={t('page.description')}
        actions={
          canUpload && (
            <>
              <Button variant="outline" onClick={() => setFolderDialog({})}>
                <FolderPlus /> {t('folderDialog.newTitle')}
              </Button>
              <Button onClick={() => setUpload({})}>
                <Upload /> {t('actions.upload', { ns: 'common' })}
              </Button>
            </>
          )
        }
      />

      <MediaBrowser
        state={state}
        mode="manage"
        selected={selected}
        onSelectedChange={onSelectedChange}
        onOpenItem={(item) => setDetailsId(item.id)}
        onMoveItems={(ids, folderId) => void moveItems(ids, folderId).catch((e: unknown) => toast.error(problemMessage(e)))}
        onFolderAction={(action, folder) => void onFolderAction(action, folder)}
        emptyAction={
          canUpload && (
            <Button variant="outline" onClick={() => setUpload({})}>
              <Upload /> {t('upload.title')}
            </Button>
          )
        }
        trashAction={
          canDelete && (
            <Button variant="outline" size="sm" className="text-destructive hover:text-destructive" onClick={() => setEmptying(true)}>
              <Trash2 /> {t('page.emptyTrash')}
            </Button>
          )
        }
        renderSelection={(items, clear, selectAll) => {
          const ids = items.map((i) => i.id)
          const canMove = items.every((i) => i.canEdit)
          const canDelete = items.every((i) => i.canDelete)
          return (
            <div className="pointer-events-none fixed inset-x-0 bottom-5 z-30 flex justify-center px-4">
              <div role="toolbar" aria-label={t('page.selectionToolbar')} className="pointer-events-auto flex items-center gap-1 rounded-full border bg-popover py-1.5 pr-1.5 pl-4 shadow-xl">
                <span className="pr-2 text-sm font-medium whitespace-nowrap">{t('selected', { count: items.length })}</span>
                <Separator orientation="vertical" className="!h-5" />
                {state.trash ? (
                  canDelete && (
                    <>
                      <Button variant="ghost" size="sm" onClick={() => void runBulk('restore', ids)}>
                        <RotateCcw /> {t('restore')}
                      </Button>
                      <Button variant="ghost" size="sm" className="text-destructive hover:text-destructive" onClick={() => setPurging(ids)}>
                        <Trash2 /> {t('deleteForever')}
                      </Button>
                    </>
                  )
                ) : (
                  <>
                    {canMove && (
                      <Button variant="ghost" size="sm" onClick={() => setMovingItems(ids)}>
                        <FolderInput /> {t('page.move')}
                      </Button>
                    )}
                    {canDelete && (
                      <Button variant="ghost" size="sm" className="text-destructive hover:text-destructive" onClick={() => void runBulk('trash', ids)}>
                        <Trash2 /> {t('actions.delete', { ns: 'common' })}
                      </Button>
                    )}
                  </>
                )}
                <Button variant="ghost" size="sm" className="max-sm:hidden" onClick={selectAll}>
                  {t('page.selectAll')}
                </Button>
                <Button variant="ghost" size="icon" className="rounded-full" aria-label={t('page.clearSelection')} onClick={clear}>
                  <X />
                </Button>
              </div>
            </div>
          )
        }}
      />

      {dragging && (
        <div
          data-testid="drop-overlay"
          className="pointer-events-auto fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-6 backdrop-blur-sm"
          onDragOver={(e) => e.preventDefault()}
          onDragLeave={() => setDragging(false)}
          onDrop={(e) => {
            e.preventDefault()
            setDragging(false)
            const files = Array.from(e.dataTransfer.files)
            if (files.length > 0) setUpload({ files })
          }}
        >
          <div className="pointer-events-none flex flex-col items-center gap-3 rounded-2xl border-2 border-dashed border-primary bg-primary/10 px-16 py-12 text-center">
            <Upload className="size-8 text-primary" />
            <p className="text-lg font-semibold">{t('upload.dropTitle')}</p>
            <p className="text-sm text-muted-foreground">{t('upload.dropHint', { folder: currentName })}</p>
          </div>
        </div>
      )}

      <UploadDialog
        open={!!upload}
        onOpenChange={(open) => !open && setUpload(null)}
        folderId={state.folderId}
        folderName={currentName}
        initialFiles={upload?.files}
      />
      <ReplaceFileDialog item={replaceItem} onOpenChange={(open) => !open && setReplaceItem(null)} />
      <MediaDetailsSheet itemId={detailsId} onOpenChange={(open) => !open && setDetailsId(null)} onReplace={setReplaceItem} />

      <FolderNameDialog
        open={!!folderDialog}
        onOpenChange={(open) => !open && setFolderDialog(null)}
        folder={folderDialog?.folder}
        parentId={state.folderId}
      />

      <MoveDialog
        open={!!movingItems}
        onOpenChange={(open) => !open && setMovingItems(null)}
        title={t('page.moveFilesTitle', { count: movingItems?.length ?? 0 })}
        currentFolderId={state.folderId}
        onMove={(folderId) => moveItems(movingItems ?? [], folderId)}
      />

      <MoveDialog
        open={!!movingFolder}
        onOpenChange={(open) => !open && setMovingFolder(null)}
        title={t('page.moveFolderTitle', { name: movingFolder?.name ?? '' })}
        excludeFolderId={movingFolder?.id}
        currentFolderId={movingFolder?.parentId ?? null}
        onMove={async (parentId) => {
          await moveFolder.mutateAsync({ id: movingFolder!.id, parentId })
          toast.success(t('page.folderMoved', { name: movingFolder!.name, folder: nameOf(parentId) }))
        }}
      />

      <ConfirmDialog
        open={!!deleting}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t('page.deleteFolderTitle', { name: deleting?.folder.name ?? '' })}
        description={deleting && `${folderContents(deleting.files, deleting.folders)} ${t('page.deleteFolderConsequence')}`}
        confirmLabel={t('tree.deleteFolder')}
        destructive
        onConfirm={async () => {
          try {
            await removeFolder(deleting!.folder, true)
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />

      <ConfirmDialog
        open={emptying}
        onOpenChange={setEmptying}
        title={t('page.emptyTrashTitle')}
        description={managesAll ? t('page.emptyTrashAll') : t('page.emptyTrashOwn')}
        confirmLabel={t('page.emptyTrash')}
        destructive
        onConfirm={async () => {
          try {
            const { deleted } = await emptyTrash.mutateAsync()
            setSelected([])
            toast.success(deleted > 0 ? t('page.purged', { count: deleted }) : t('page.nothingToDelete'), {
              description: deleted > 0 ? undefined : t('page.nothingToDeleteDescription'),
            })
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />

      <ConfirmDialog
        open={!!purging}
        onOpenChange={(open) => !open && setPurging(null)}
        title={t('page.purgeTitle', { count: purging?.length ?? 0 })}
        description={t('page.purgeDescription')}
        confirmLabel={t('deleteForever')}
        destructive
        onConfirm={async () => {
          if (!(await runBulk('purge', purging ?? []))) throw new Error('Delete failed')
        }}
      />
    </div>
  )
}
