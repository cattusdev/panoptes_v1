import { useState, type FormEvent, type ReactNode } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Copy, Download, ExternalLink, Globe, Lock, RefreshCw, RotateCcw, Trash2 } from 'lucide-react'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { DateTime } from '@/components/admin/DateTime'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Separator } from '@/components/ui/separator'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { fieldErrors, problemMessage } from '@/lib/api'
import {
  absoluteUrl,
  folderPath,
  formatBytes,
  useBulkMediaAction,
  useMediaFolders,
  useMediaItem,
  useMediaVersions,
  useUpdateMedia,
  type MediaItem,
  type MediaVisibility,
} from '@/lib/media-api'
import { fileStyle } from './media-utils'
import { MediaThumb } from './MediaThumb'
import { MediaVersions } from './MediaVersions'

type Tab = 'details' | 'versions'

// Λεπτομέρειες αρχείου (όπως Supabase Storage / WordPress): προεπισκόπηση, στοιχεία, links, ενέργειες
export function MediaDetailsSheet({
  itemId,
  onOpenChange,
  onReplace,
}: {
  itemId: string | null
  onOpenChange: (open: boolean) => void
  onReplace: (item: MediaItem) => void
}) {
  const { t } = useTranslation('media')
  const item = useMediaItem(itemId)
  // Η καρτέλα ζει εδώ: το Details ξαναστήνεται όταν αλλάζει το αρχείο (π.χ. μετά από Restore μιας έκδοσης)
  const [tab, setTab] = useState<Tab>('details')
  const [tabFor, setTabFor] = useState(itemId)
  if (tabFor !== itemId) {
    setTabFor(itemId)
    setTab('details')
  }

  return (
    <Sheet open={!!itemId} onOpenChange={onOpenChange}>
      <SheetContent className="w-full gap-0 overflow-y-auto data-[side=right]:sm:max-w-md">
        {item.data ? (
          <Details
            key={`${item.data.id}-${item.data.updatedAt}`}
            item={item.data}
            tab={tab}
            onTabChange={setTab}
            onReplace={onReplace}
            onClose={() => onOpenChange(false)}
          />
        ) : (
          <div className="space-y-4 p-6">
            <SheetHeader className="p-0">
              <SheetTitle>
                <Skeleton className="h-6 w-2/3" />
              </SheetTitle>
              <SheetDescription className="sr-only">{t('details.loading')}</SheetDescription>
            </SheetHeader>
            <Skeleton className="aspect-video w-full rounded-xl" />
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-full" />
          </div>
        )}
      </SheetContent>
    </Sheet>
  )
}

function Details({
  item,
  tab,
  onTabChange,
  onReplace,
  onClose,
}: {
  item: MediaItem
  tab: Tab
  onTabChange: (tab: Tab) => void
  onReplace: (item: MediaItem) => void
  onClose: () => void
}) {
  const { t } = useTranslation('media')
  const folders = useMediaFolders()
  const versions = useMediaVersions(item.id)
  const update = useUpdateMedia(item.id)
  const bulk = useBulkMediaAction()
  const [title, setTitle] = useState(item.title)
  const [altText, setAltText] = useState(item.altText ?? '')
  const [description, setDescription] = useState(item.description ?? '')
  const [visibility, setVisibility] = useState<MediaVisibility>(item.visibility)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [confirmPurge, setConfirmPurge] = useState(false)

  const { version } = item
  const style = fileStyle(version.extension)
  const trashed = !!item.deletedAt
  const editable = item.canEdit && !trashed
  const dirty =
    title !== item.title || altText !== (item.altText ?? '') || description !== (item.description ?? '') || visibility !== item.visibility
  const folder = folderPath(folders.data, item.folderId).map((f) => f.name).join(' / ') || t('library')

  async function save(e: FormEvent) {
    e.preventDefault()
    setErrors({})
    try {
      await update.mutateAsync({ title, altText: altText || null, description: description || null, visibility })
      toast.success(t('details.saved'))
    } catch (error) {
      const byField = fieldErrors(error)
      if (Object.keys(byField).length > 0) setErrors(byField)
      else toast.error(problemMessage(error))
    }
  }

  // true = έγινε (το ConfirmDialog μένει ανοιχτό αν αποτύχει)
  async function run(action: 'trash' | 'restore' | 'purge') {
    try {
      await bulk.mutateAsync({ action, ids: [item.id] })
      onClose()
      if (action === 'trash') {
        toast.success(t('details.trashed', { title: item.title }), {
          action: { label: t('actions.undo', { ns: 'common' }), onClick: () => void bulk.mutateAsync({ action: 'restore', ids: [item.id] }) },
        })
      } else {
        toast.success(action === 'restore' ? t('details.restored', { title: item.title }) : t('details.purged', { title: item.title }))
      }
      return true
    } catch (error) {
      toast.error(problemMessage(error))
      return false
    }
  }

  async function copy(url: string) {
    try {
      await navigator.clipboard.writeText(absoluteUrl(url))
      toast.success(t('details.linkCopied'))
    } catch {
      toast.error(t('details.copyFailed'))
    }
  }

  type Size = { key: 'thumb' | 'medium' | 'large' | 'original'; url: string; note: string }
  const sizes = [
    { key: 'thumb', url: version.urls.thumb, note: '320 × 320' },
    { key: 'medium', url: version.urls.medium, note: t('details.upToWidth', { width: 800 }) },
    { key: 'large', url: version.urls.large, note: t('details.upToWidth', { width: 1600 }) },
    { key: 'original', url: version.urls.original, note: version.width ? `${version.width} × ${version.height}` : formatBytes(version.sizeBytes) },
  ].filter((s): s is Size => !!s.url)

  const preview = version.urls.medium ?? (version.extension === 'svg' ? version.urls.original : null)
  const opensInBrowser = ['pdf', 'txt', 'csv'].includes(version.extension)

  return (
    <>
      <SheetHeader className="border-b p-6 pr-12">
        <SheetTitle className="truncate text-lg">{item.title}</SheetTitle>
        <SheetDescription>
          {style.label} · {formatBytes(version.sizeBytes)}
          {version.width ? ` · ${version.width} × ${version.height}` : ''}
        </SheetDescription>
      </SheetHeader>

      <Tabs value={tab} onValueChange={(v) => onTabChange(v as Tab)} className="border-b px-6">
        <TabsList variant="line" className="-mb-px">
          <TabsTrigger value="details">{t('details.tabs.details')}</TabsTrigger>
          <TabsTrigger value="versions">
            {t('details.tabs.versions')}
            {versions.data && <span className="text-muted-foreground tabular-nums">{versions.data.length}</span>}
          </TabsTrigger>
        </TabsList>
      </Tabs>

      {tab === 'versions' ? (
        <div className="p-6">
          <MediaVersions item={item} onReplace={onReplace} />
        </div>
      ) : (
        <div className="space-y-6 p-6">
          {/* Προεπισκόπηση */}
          <div className="overflow-hidden rounded-xl border bg-[repeating-conic-gradient(#ffffff0a_0_25%,transparent_0_50%)] bg-[length:16px_16px]">
            {preview ? (
              <img src={preview} alt={item.altText ?? ''} className="mx-auto max-h-72 w-auto object-contain" />
            ) : (
              <MediaThumb item={item} className="aspect-video w-full" iconClassName="size-12" />
            )}
          </div>

          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" asChild>
              <a href={version.urls.download}>
                <Download /> {t('actions.download', { ns: 'common' })}
              </a>
            </Button>
            {opensInBrowser && (
              <Button variant="outline" size="sm" asChild>
                <a href={version.urls.original} target="_blank" rel="noreferrer">
                  <ExternalLink /> {t('details.open')}
                </a>
              </Button>
            )}
            {editable && (
              <Button variant="outline" size="sm" onClick={() => onReplace(item)}>
                <RefreshCw /> {t('replaceFile')}
              </Button>
            )}
          </div>

          {trashed && (
            <div className="rounded-xl border border-warning/35 bg-warning/10 p-4 text-sm">
              <p className="font-medium text-warning">{t('details.inTrash')}</p>
              {item.canDelete && (
                <div className="mt-3 flex gap-2">
                  <Button size="sm" onClick={() => void run('restore')} disabled={bulk.isPending}>
                    <RotateCcw /> {t('restore')}
                  </Button>
                  <Button size="sm" variant="destructive" onClick={() => setConfirmPurge(true)}>
                    <Trash2 /> {t('deleteForever')}
                  </Button>
                </div>
              )}
            </div>
          )}

          {/* Στοιχεία */}
          {editable ? (
            <form onSubmit={save}>
              <FieldGroup>
                <Field data-invalid={!!errors.title || undefined}>
                  <FieldLabel htmlFor="media-title">{t('details.title')}</FieldLabel>
                  <Input id="media-title" required maxLength={255} value={title} onChange={(e) => setTitle(e.target.value)} aria-invalid={!!errors.title || undefined} />
                  <FieldError>{errors.title}</FieldError>
                </Field>
                {item.kind === 'Image' && (
                  <Field>
                    <FieldLabel htmlFor="media-alt">{t('details.altText')}</FieldLabel>
                    <Input id="media-alt" maxLength={500} value={altText} onChange={(e) => setAltText(e.target.value)} />
                    <FieldDescription>{t('details.altTextHint')}</FieldDescription>
                  </Field>
                )}
                <Field>
                  <FieldLabel htmlFor="media-description">{t('details.description')}</FieldLabel>
                  <Textarea id="media-description" rows={3} maxLength={2000} value={description} onChange={(e) => setDescription(e.target.value)} />
                </Field>
                <Field orientation="horizontal" className="items-start justify-between rounded-lg border p-3.5">
                  <div className="space-y-1">
                    <FieldLabel htmlFor="media-public">{t('details.publicLink')}</FieldLabel>
                    <FieldDescription>{t('details.publicLinkHint')}</FieldDescription>
                  </div>
                  <Switch id="media-public" checked={visibility === 'Public'} onCheckedChange={(on) => setVisibility(on ? 'Public' : 'Private')} />
                </Field>
              </FieldGroup>
              <div className="mt-4 flex justify-end">
                <Button type="submit" disabled={!dirty || update.isPending}>
                  {update.isPending && <Spinner />}
                  {t('actions.saveChanges', { ns: 'common' })}
                </Button>
              </div>
            </form>
          ) : (
            <dl className="space-y-3 text-sm">
              {item.altText && <Info label={t('details.altText')}>{item.altText}</Info>}
              {item.description && <Info label={t('details.description')}>{item.description}</Info>}
              {!trashed && <p className="text-muted-foreground">{t('details.readOnly')}</p>}
            </dl>
          )}

          {/* Links ανά μέγεθος */}
          {!trashed && (
            <section>
              <h3 className="mb-1 text-sm font-medium">{t('details.links')}</h3>
              <p className="mb-2.5 text-xs text-muted-foreground">
                {item.visibility === 'Public' ? t('details.publicLinks') : t('details.privateLinks')}
              </p>
              <ul className="divide-y rounded-lg border">
                {sizes.map((size) => (
                  <li key={size.key} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
                    <span>
                      {t(`details.sizes.${size.key}`)} <span className="text-muted-foreground">· {size.note}</span>
                    </span>
                    <Button variant="ghost" size="sm" onClick={() => void copy(size.url)} aria-label={t(`details.copyLink.${size.key}`)}>
                      <Copy /> {t('actions.copy', { ns: 'common' })}
                    </Button>
                  </li>
                ))}
              </ul>
            </section>
          )}

          <Separator />

          <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2.5 text-sm">
            <Info label={t('fields.access')}>
              {item.visibility === 'Public' ? (
                <Badge variant="info">
                  <Globe /> {t('visibility.Public')}
                </Badge>
              ) : (
                <Badge variant="muted">
                  <Lock /> {t('visibility.Private')}
                </Badge>
              )}
            </Info>
            <Info label={t('details.folder')}>{folder}</Info>
            <Info label={t('details.fileName')}>{version.fileName}</Info>
            <Info label={t('fields.uploadedBy')}>{item.ownerEmail ?? '—'}</Info>
            <Info label={t('fields.uploaded')}>
              <DateTime value={item.createdAt} />
            </Info>
            {version.number > 1 && (
              <Info label={t('details.version')}>
                <Trans t={t} i18nKey="details.versionValue" values={{ number: version.number }} components={{ time: <DateTime value={version.createdAt} /> }} />
              </Info>
            )}
          </dl>

          {item.canDelete && !trashed && (
            <>
              <Separator />
              <Button variant="outline" className="w-full text-destructive hover:text-destructive" onClick={() => void run('trash')} disabled={bulk.isPending}>
                <Trash2 /> {t('details.moveToTrash')}
              </Button>
            </>
          )}
        </div>
      )}

      <ConfirmDialog
        open={confirmPurge}
        onOpenChange={setConfirmPurge}
        title={t('details.purgeTitle')}
        description={<Trans t={t} i18nKey="details.purgeDescription" values={{ title: item.title }} components={{ b: <strong /> }} />}
        confirmLabel={t('deleteForever')}
        destructive
        onConfirm={async () => {
          if (!(await run('purge'))) throw new Error('Delete failed')
        }}
      />
    </>
  )
}

function Info({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="min-w-0 break-words">{children}</dd>
    </>
  )
}
