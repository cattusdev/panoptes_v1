import { useEffect, useMemo, useRef, useState, type DragEvent, type MouseEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Folder, FolderTree as FolderTreeIcon, Globe, ImageOff, LayoutGrid, List, SearchX, Trash2 } from 'lucide-react'
import { SearchInput, TableEmpty } from '@/components/admin/Common'
import { DataTable, type Column } from '@/components/admin/DataTable'
import { DateTime } from '@/components/admin/DateTime'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { Badge } from '@/components/ui/badge'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Sheet, SheetContent, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import {
  folderPath,
  formatBytes,
  useMediaFolders,
  useMediaList,
  useMediaSettings,
  type MediaFolder,
  type MediaItem,
  type MediaKind,
  type MediaSort,
  type MediaVisibility,
} from '@/lib/media-api'
import { cn } from '@/lib/utils'
import type { BrowserState } from './browser-state'
import { FolderMenu, FolderTree, type FolderAction } from './FolderTree'
import { MEDIA_DRAG_TYPE, childrenOf, fileStyle, useMediaDropTarget } from './media-utils'
import { MediaThumb } from './MediaThumb'

// Οι τιμές πάνε στο API ως έχουν· μεταφράζονται μόνο οι ετικέτες
const kinds: MediaKind[] = ['Image', 'Document', 'Archive']
const visibilities: MediaVisibility[] = ['Private', 'Public']
const sorts: MediaSort[] = ['newest', 'oldest', 'name']

type Layout = 'grid' | 'list'
const layoutKey = 'panoptes.media.layout'

function readLayout(): Layout {
  try {
    return localStorage.getItem(layoutKey) === 'list' ? 'list' : 'grid'
  } catch {
    return 'grid'
  }
}

export type MediaBrowserProps = {
  state: BrowserState
  mode: 'manage' | 'pick'
  selected: string[]
  onSelectedChange: (ids: string[]) => void
  onOpenItem: (item: MediaItem) => void
  accept?: MediaKind[] // pick: μόνο αυτά τα είδη
  onMoveItems?: (ids: string[], folderId: string | null) => void // drag & drop σε φάκελο
  onFolderAction?: (action: FolderAction, folder: MediaFolder) => void
  emptyAction?: ReactNode
  trashAction?: ReactNode // π.χ. "Empty trash", μόνο όταν ο κάδος έχει αρχεία
  renderSelection?: (items: MediaItem[], clear: () => void, selectAll: () => void) => ReactNode
  onItemsLoaded?: (items: MediaItem[]) => void // Ο picker κρατά τα αρχεία που είδε (για την επιλογή)
}

// Η βιβλιοθήκη: φάκελοι αριστερά, εργαλεία και αρχεία δεξιά (grid ή λίστα, infinite scroll, επιλογή πολλών).
export function MediaBrowser({
  state,
  mode,
  selected,
  onSelectedChange,
  onOpenItem,
  accept,
  onMoveItems,
  onFolderAction,
  emptyAction,
  trashAction,
  renderSelection,
  onItemsLoaded,
}: MediaBrowserProps) {
  const { t } = useTranslation('media')
  const folders = useMediaFolders()
  const settings = useMediaSettings()
  const [layout, setLayout] = useState<Layout>(readLayout)
  const [mobileTree, setMobileTree] = useState(false)
  const [anchor, setAnchor] = useState<number | null>(null)

  const forcedKind = accept?.length === 1 ? accept[0] : undefined
  const filters = { ...state.filters, kind: forcedKind ?? state.filters.kind }
  const list = useMediaList(filters)

  const items = useMemo(() => {
    const all = list.data?.pages.flatMap((p) => p.items) ?? []
    return accept ? all.filter((i) => accept.includes(i.kind)) : all
  }, [list.data, accept])

  useEffect(() => {
    if (items.length > 0) onItemsLoaded?.(items)
  }, [items, onItemsLoaded])

  // Όταν αλλάζει ο φάκελος ή τα φίλτρα, η επιλογή μηδενίζεται (στη σελίδα· στον picker κρατιέται)
  const filtersKey = JSON.stringify(filters)
  const previousKey = useRef(filtersKey)
  useEffect(() => {
    if (previousKey.current === filtersKey) return
    previousKey.current = filtersKey
    setAnchor(null)
    if (mode === 'manage') onSelectedChange([])
  }, [filtersKey, mode, onSelectedChange])

  // Infinite scroll
  const sentinel = useRef<HTMLDivElement>(null)
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = list
  useEffect(() => {
    const element = sentinel.current
    if (!element || !hasNextPage) return
    const observer = new IntersectionObserver((entries) => {
      if (entries[0]?.isIntersecting && !isFetchingNextPage) void fetchNextPage()
    }, { rootMargin: '400px' })
    observer.observe(element)
    return () => observer.disconnect()
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])

  function changeLayout(value: string) {
    if (value !== 'grid' && value !== 'list') return
    setLayout(value)
    try {
      localStorage.setItem(layoutKey, value)
    } catch {
      // Δεν το θυμόμαστε: δεν πειράζει
    }
  }

  // Κλικ: άνοιγμα· Ctrl/⌘ ή ενεργή επιλογή: εναλλαγή· Shift: διάστημα
  function handleClick(item: MediaItem, index: number, e?: MouseEvent) {
    if (e?.shiftKey && anchor !== null) {
      const [from, to] = anchor < index ? [anchor, index] : [index, anchor]
      onSelectedChange([...new Set([...selected, ...items.slice(from, to + 1).map((i) => i.id)])])
    } else if (e?.metaKey || e?.ctrlKey || (mode === 'manage' && selected.length > 0)) {
      toggle(item.id)
    } else {
      onOpenItem(item)
    }
    setAnchor(index)
  }

  function toggle(id: string) {
    onSelectedChange(selected.includes(id) ? selected.filter((s) => s !== id) : [...selected, id])
  }

  function dragStart(e: DragEvent, item: MediaItem) {
    const ids = selected.includes(item.id) ? selected : [item.id]
    e.dataTransfer.setData(MEDIA_DRAG_TYPE, JSON.stringify(ids))
    e.dataTransfer.effectAllowed = 'move'
  }

  const allFolders = folders.data ?? []
  const subfolders = !state.trash && !filters.search ? childrenOf(allFolders, state.folderId) : []
  const filtered = !!(filters.search || filters.kind || filters.owner || filters.visibility)
  const canDrag = mode === 'manage' && !!onMoveItems && !state.trash
  const selectedItems = items.filter((i) => selected.includes(i.id))

  const kindOptions = kinds.map((value) => ({ value, label: t(`kinds.${value}`) }))
  const ownerOptions = [{ value: 'mine', label: t('browser.uploadedByMe') }]
  const visibilityOptions = visibilities.map((value) => ({ value, label: t(`visibility.${value}`) }))
  const sortOptions = sorts.map((value) => ({ value, label: t(`browser.sort.${value}`) }))

  const tree = (
    <FolderTree
      folders={allFolders}
      currentId={state.folderId}
      trash={state.trash}
      onOpen={(id) => {
        state.openFolder(id)
        setMobileTree(false)
      }}
      onOpenTrash={() => {
        state.openTrash()
        setMobileTree(false)
      }}
      showTrash={mode === 'manage'}
      onDropItems={canDrag ? (folderId, ids) => onMoveItems?.(ids, folderId) : undefined}
      onAction={mode === 'manage' ? onFolderAction : undefined}
    />
  )

  const columns: Column<MediaItem>[] = [
    {
      key: 'select',
      header: <span className="sr-only">{t('actions.select', { ns: 'common' })}</span>,
      className: 'w-10',
      cell: (item) => (
        <Checkbox
          aria-label={t('browser.selectItem', { title: item.title })}
          checked={selected.includes(item.id)}
          onClick={(e) => e.stopPropagation()}
          onCheckedChange={() => toggle(item.id)}
        />
      ),
    },
    {
      key: 'name',
      header: t('name'),
      cell: (item) => (
        <div className="flex min-w-0 items-center gap-3">
          <MediaThumb item={item} className="size-9 shrink-0 rounded-md" iconClassName="size-4" />
          <span className="truncate font-medium">{item.title}</span>
        </div>
      ),
    },
    { key: 'type', header: t('fields.type'), className: 'hidden sm:table-cell', cell: (item) => fileStyle(item.version.extension).label },
    { key: 'size', header: t('fields.size'), className: 'hidden md:table-cell tabular-nums', cell: (item) => formatBytes(item.version.sizeBytes) },
    { key: 'owner', header: t('fields.uploadedBy'), className: 'hidden lg:table-cell', cell: (item) => item.ownerEmail ?? '—' },
    { key: 'date', header: state.trash ? t('fields.deleted') : t('fields.uploaded'), className: 'hidden md:table-cell', cell: (item) => <DateTime value={item.deletedAt ?? item.createdAt} mode="relative" /> },
    {
      key: 'visibility',
      header: t('fields.access'),
      className: 'hidden sm:table-cell',
      cell: (item) =>
        item.visibility === 'Public' ? <Badge variant="info">{t('visibility.Public')}</Badge> : <Badge variant="muted">{t('visibility.Private')}</Badge>,
    },
  ]

  const path = folderPath(allFolders, state.folderId)

  return (
    <div className="grid gap-6 md:grid-cols-[220px_minmax(0,1fr)]">
      <aside className="hidden md:block">
        <div className={cn('sticky', mode === 'manage' ? 'top-24' : 'top-0')}>{tree}</div>
      </aside>

      <section className="min-w-0">
        {/* Πού είμαστε */}
        <div className="mb-3 flex min-h-8 items-center gap-2">
          <Button variant="outline" size="sm" className="md:hidden" onClick={() => setMobileTree(true)}>
            <FolderTreeIcon /> {t('folders')}
          </Button>
          <Breadcrumb>
            <BreadcrumbList>
              {state.trash ? (
                <BreadcrumbItem>
                  <BreadcrumbPage>{t('trash')}</BreadcrumbPage>
                </BreadcrumbItem>
              ) : filters.search ? (
                <BreadcrumbItem>
                  <BreadcrumbPage>{t('browser.resultsFor', { search: filters.search })}</BreadcrumbPage>
                </BreadcrumbItem>
              ) : (
                <>
                  <BreadcrumbItem>
                    {path.length === 0 ? (
                      <BreadcrumbPage>{t('library')}</BreadcrumbPage>
                    ) : (
                      <BreadcrumbLink asChild>
                        <button type="button" onClick={() => state.openFolder(null)}>
                          {t('library')}
                        </button>
                      </BreadcrumbLink>
                    )}
                  </BreadcrumbItem>
                  {path.map((folder, i) => (
                    <BreadcrumbCrumb key={folder.id} folder={folder} last={i === path.length - 1} onOpen={state.openFolder} />
                  ))}
                </>
              )}
            </BreadcrumbList>
          </Breadcrumb>
        </div>

        {/* Κάδος: πόσο μένουν τα αρχεία (όπως το Google Drive) */}
        {state.trash && (
          <div className="mb-4 flex min-h-12 flex-wrap items-center justify-between gap-2 rounded-lg border bg-muted/40 px-4 py-2 text-sm">
            <p className="text-muted-foreground">
              {settings.data
                ? t('browser.trashRetention', { days: settings.data.trashRetentionDays })
                : t('browser.trashRetentionUnknown')}
            </p>
            {items.length > 0 && trashAction}
          </div>
        )}

        {/* Αναζήτηση και φίλτρα */}
        <div className="mb-4 flex flex-col gap-2 lg:flex-row lg:items-center">
          <SearchInput value={state.search} onChange={state.setSearch} placeholder={t('browser.searchPlaceholder')} />
          <div className="flex flex-wrap gap-2 lg:ml-auto">
            {!forcedKind && (
              <OptionSelect
                ariaLabel={t('browser.filterType')}
                value={filters.kind ?? ''}
                onChange={(v) => state.setFilter('kind', v)}
                emptyLabel={t('browser.allTypes')}
                options={accept ? kindOptions.filter((o) => accept.includes(o.value as MediaKind)) : kindOptions}
                className="w-[8.5rem]"
              />
            )}
            <OptionSelect
              ariaLabel={t('browser.filterOwner')}
              value={filters.owner ?? ''}
              onChange={(v) => state.setFilter('owner', v)}
              emptyLabel={t('browser.anyone')}
              options={ownerOptions}
              className="w-[10rem]"
            />
            <OptionSelect
              ariaLabel={t('browser.filterAccess')}
              value={filters.visibility ?? ''}
              onChange={(v) => state.setFilter('visibility', v)}
              emptyLabel={t('browser.anyAccess')}
              options={visibilityOptions}
              className="w-[8.5rem]"
            />
            <OptionSelect
              ariaLabel={t('browser.sortLabel')}
              value={filters.sort ?? 'newest'}
              onChange={(v) => state.setFilter('sort', v === 'newest' ? '' : v)}
              options={sortOptions}
              className="w-[9.5rem]"
            />
            <ToggleGroup type="single" variant="outline" value={layout} onValueChange={changeLayout} aria-label={t('browser.layout')}>
              <ToggleGroupItem value="grid" aria-label={t('browser.gridView')}>
                <LayoutGrid />
              </ToggleGroupItem>
              <ToggleGroupItem value="list" aria-label={t('browser.listView')}>
                <List />
              </ToggleGroupItem>
            </ToggleGroup>
          </div>
        </div>

        {/* Υποφάκελοι */}
        {subfolders.length > 0 && (
          <div className="mb-5 grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-2.5">
            {subfolders.map((folder) => (
              <FolderTile
                key={folder.id}
                folder={folder}
                onOpen={() => state.openFolder(folder.id)}
                onDrop={canDrag ? (ids) => onMoveItems?.(ids, folder.id) : undefined}
                menu={mode === 'manage' && onFolderAction && (folder.canEdit || folder.canDelete) ? (
                  <FolderMenu folder={folder} onAction={onFolderAction} />
                ) : undefined}
              />
            ))}
          </div>
        )}

        {/* Αρχεία */}
        {list.isPending ? (
          <GridSkeleton />
        ) : items.length === 0 ? (
          subfolders.length === 0 && (
            <div className="rounded-xl border border-dashed">
              {state.trash ? (
                <TableEmpty icon={Trash2} title={t('browser.empty.trashTitle')} description={t('browser.empty.trashDescription')} />
              ) : filtered ? (
                <TableEmpty icon={SearchX} title={t('browser.empty.noMatchTitle')} description={t('browser.empty.noMatchDescription')} />
              ) : (
                <div>
                  <TableEmpty
                    icon={state.folderId ? Folder : ImageOff}
                    title={state.folderId ? t('browser.empty.folderTitle') : t('browser.empty.libraryTitle')}
                    description={mode === 'manage' ? t('browser.empty.manageDescription') : t('browser.empty.pickDescription')}
                  />
                  {emptyAction && <div className="-mt-6 flex justify-center pb-8">{emptyAction}</div>}
                </div>
              )}
            </div>
          )
        ) : layout === 'grid' ? (
          <div className="grid grid-cols-[repeat(auto-fill,minmax(150px,1fr))] gap-3">
            {items.map((item, index) => (
              <MediaCard
                key={item.id}
                item={item}
                selected={selected.includes(item.id)}
                selecting={selected.length > 0}
                draggable={canDrag && item.canEdit}
                onDragStart={(e) => dragStart(e, item)}
                onClick={(e) => handleClick(item, index, e)}
                onToggle={() => {
                  toggle(item.id)
                  setAnchor(index)
                }}
              />
            ))}
          </div>
        ) : (
          <DataTable
            columns={columns}
            rows={items}
            getRowId={(item) => item.id}
            rowLabel={(item) => item.title}
            onRowClick={(item) => handleClick(item, items.indexOf(item))}
            empty={null}
          />
        )}

        <div ref={sentinel} className="flex h-12 items-center justify-center text-muted-foreground">
          {isFetchingNextPage && <Spinner />}
        </div>
      </section>

      {renderSelection && selectedItems.length > 0 &&
        renderSelection(selectedItems, () => onSelectedChange([]), () => onSelectedChange(items.map((i) => i.id)))}

      <Sheet open={mobileTree} onOpenChange={setMobileTree}>
        <SheetContent side="left" className="w-72">
          <SheetHeader>
            <SheetTitle>{t('folders')}</SheetTitle>
          </SheetHeader>
          <div className="px-4">{tree}</div>
        </SheetContent>
      </Sheet>
    </div>
  )
}

function BreadcrumbCrumb({ folder, last, onOpen }: { folder: MediaFolder; last: boolean; onOpen: (id: string) => void }) {
  return (
    <>
      <BreadcrumbSeparator />
      <BreadcrumbItem>
        {last ? (
          <BreadcrumbPage>{folder.name}</BreadcrumbPage>
        ) : (
          <BreadcrumbLink asChild>
            <button type="button" onClick={() => onOpen(folder.id)}>
              {folder.name}
            </button>
          </BreadcrumbLink>
        )}
      </BreadcrumbItem>
    </>
  )
}

function FolderTile({ folder, onOpen, onDrop, menu }: { folder: MediaFolder; onOpen: () => void; onDrop?: (ids: string[]) => void; menu?: ReactNode }) {
  const { t } = useTranslation('media')
  const drop = useMediaDropTarget(onDrop)
  return (
    <div
      {...drop.props}
      className={cn(
        'group/row flex items-center gap-1 rounded-xl border bg-card pr-1.5 transition-colors hover:border-muted-foreground/40',
        drop.over && 'border-primary bg-primary/10',
      )}
    >
      <button type="button" onClick={onOpen} aria-label={t('browser.openFolder', { name: folder.name })} className="flex min-w-0 flex-1 items-center gap-2.5 px-3 py-2.5 text-left">
        <Folder className="size-5 shrink-0 fill-muted-foreground/25 text-muted-foreground" />
        <span className="truncate text-sm font-medium">{folder.name}</span>
        {folder.itemCount > 0 && <span className="ml-auto text-xs text-muted-foreground tabular-nums">{folder.itemCount}</span>}
      </button>
      {menu}
    </div>
  )
}

function MediaCard({
  item,
  selected,
  selecting,
  draggable,
  onDragStart,
  onClick,
  onToggle,
}: {
  item: MediaItem
  selected: boolean
  selecting: boolean
  draggable: boolean
  onDragStart: (e: DragEvent) => void
  onClick: (e: MouseEvent) => void
  onToggle: () => void
}) {
  const { t } = useTranslation('media')
  const { version } = item
  return (
    <div
      role="group"
      aria-label={item.title}
      draggable={draggable}
      onDragStart={onDragStart}
      className={cn(
        'group relative overflow-hidden rounded-xl border bg-card transition-colors hover:border-muted-foreground/40',
        selected && 'border-primary ring-1 ring-primary',
      )}
    >
      <button type="button" onClick={onClick} aria-label={t('browser.openItem', { title: item.title })} className="block w-full text-left">
        <MediaThumb item={item} className="aspect-square w-full" />
        <div className="px-2.5 py-2">
          <p className="truncate text-sm font-medium" title={item.title}>
            {item.title}
          </p>
          <p className="truncate text-xs text-muted-foreground">
            {fileStyle(version.extension).label} · {formatBytes(version.sizeBytes)}
          </p>
        </div>
      </button>
      <Checkbox
        aria-label={t('browser.selectItem', { title: item.title })}
        checked={selected}
        onCheckedChange={onToggle}
        className={cn(
          'absolute top-2 left-2 size-5 border-white/70 bg-black/45 backdrop-blur transition-opacity data-[state=checked]:border-primary data-[state=checked]:bg-primary',
          selecting || selected ? 'opacity-100' : 'opacity-0 group-hover:opacity-100 focus-visible:opacity-100',
        )}
      />
      {item.visibility === 'Public' && (
        <span title={t('browser.publicHint')} className="absolute top-2 right-2 rounded-md bg-black/55 p-1 text-white backdrop-blur">
          <Globe className="size-3.5" />
          <span className="sr-only">{t('visibility.Public')}</span>
        </span>
      )}
    </div>
  )
}

function GridSkeleton() {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(150px,1fr))] gap-3">
      {Array.from({ length: 12 }, (_, i) => (
        <div key={i} className="overflow-hidden rounded-xl border">
          <Skeleton className="aspect-square w-full rounded-none" />
          <div className="space-y-1.5 p-2.5">
            <Skeleton className="h-3.5 w-3/4" />
            <Skeleton className="h-3 w-1/2" />
          </div>
        </div>
      ))}
    </div>
  )
}
