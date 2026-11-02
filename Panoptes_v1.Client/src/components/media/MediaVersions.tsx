import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Download, History, MoreHorizontal, RefreshCw, RotateCcw, Trash2 } from 'lucide-react'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { DateTime } from '@/components/admin/DateTime'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'
import { problemMessage } from '@/lib/api'
import {
  formatBytes,
  useDeleteVersion,
  useMediaSettings,
  useMediaVersions,
  useRestoreVersion,
  type MediaItem,
  type MediaVersion,
} from '@/lib/media-api'
import { MediaThumb } from './MediaThumb'

// Ιστορικό εκδόσεων (όπως το "Manage versions" του Google Drive και το version history του Dropbox):
// η τρέχουσα πάνω, Restore σε κάθε παλιότερη, Download/Delete στο μενού.
export function MediaVersions({ item, onReplace }: { item: MediaItem; onReplace: (item: MediaItem) => void }) {
  const { t } = useTranslation('media')
  const versions = useMediaVersions(item.id)
  const settings = useMediaSettings()
  const restore = useRestoreVersion(item.id)
  const remove = useDeleteVersion(item.id)
  const [deleting, setDeleting] = useState<MediaVersion | null>(null)

  const trashed = !!item.deletedAt
  const canRestore = item.canEdit && !trashed
  const canDelete = item.canDelete && !trashed
  const maxVersions = settings.data?.maxVersions

  async function restoreVersion(version: MediaVersion) {
    try {
      await restore.mutateAsync(version.id)
      toast.success(t('versions.restored', { number: version.number }), { description: t('versions.restoredDescription') })
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-3">
        <p className="text-sm text-muted-foreground">
          {maxVersions ? t('versions.limit', { max: maxVersions }) : t('versions.earlier')}
        </p>
        {canRestore && (
          <Button variant="outline" size="sm" className="shrink-0" onClick={() => onReplace(item)}>
            <RefreshCw /> {t('versions.uploadNew')}
          </Button>
        )}
      </div>

      {versions.isPending ? (
        <div className="space-y-2">
          <Skeleton className="h-16 w-full rounded-lg" />
          <Skeleton className="h-16 w-full rounded-lg" />
        </div>
      ) : versions.isError ? (
        <p className="text-sm text-destructive">{problemMessage(versions.error)}</p>
      ) : (
        <>
          <ul className="divide-y rounded-lg border" aria-label={t('details.tabs.versions')}>
            {versions.data.map((version) => (
              <li key={version.id} className="flex items-center gap-3 p-3" aria-label={t('versions.version', { number: version.number })}>
                <MediaThumb item={{ ...item, version }} className="size-11 shrink-0 rounded-md" iconClassName="size-5" />
                <div className="min-w-0 flex-1">
                  <p className="flex items-center gap-2 text-sm font-medium">
                    {t('versions.version', { number: version.number })}
                    {version.isCurrent && <Badge variant="success">{t('versions.current')}</Badge>}
                  </p>
                  <p className="truncate text-xs text-muted-foreground">
                    <DateTime value={version.createdAt} /> · {formatBytes(version.sizeBytes)}
                  </p>
                  <p className="truncate text-xs text-muted-foreground">{version.createdBy ?? t('versions.unknown')}</p>
                </div>
                {!version.isCurrent && canRestore && (
                  <Button variant="ghost" size="sm" onClick={() => void restoreVersion(version)} disabled={restore.isPending}>
                    <RotateCcw /> {t('restore')}
                  </Button>
                )}
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="ghost" size="icon" className="size-8" aria-label={t('versions.actions', { number: version.number })}>
                      <MoreHorizontal />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem asChild>
                      <a href={version.urls.download}>
                        <Download /> {t('actions.download', { ns: 'common' })}
                      </a>
                    </DropdownMenuItem>
                    {!version.isCurrent && canDelete && (
                      <>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem variant="destructive" onSelect={() => setDeleting(version)}>
                          <Trash2 /> {t('versions.delete')}
                        </DropdownMenuItem>
                      </>
                    )}
                  </DropdownMenuContent>
                </DropdownMenu>
              </li>
            ))}
          </ul>
          {versions.data.length === 1 && (
            <div className="flex flex-col items-center gap-2 rounded-lg border border-dashed px-6 py-8 text-center">
              <History className="size-6 text-muted-foreground" />
              <p className="text-sm font-medium">{t('versions.noEarlier')}</p>
              <p className="text-xs text-muted-foreground">{t('versions.noEarlierHint')}</p>
            </div>
          )}
        </>
      )}

      <ConfirmDialog
        open={!!deleting}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t('versions.deleteTitle', { number: deleting?.number ?? '' })}
        description={t('versions.deleteDescription')}
        confirmLabel={t('versions.delete')}
        destructive
        onConfirm={async () => {
          try {
            await remove.mutateAsync(deleting!.id)
            toast.success(t('versions.deleted', { number: deleting!.number }))
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />
    </div>
  )
}
