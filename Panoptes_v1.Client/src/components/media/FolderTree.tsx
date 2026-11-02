import { useMemo, useState, type ComponentType, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronRight, Folder, FolderOpen, FolderInput, Library, MoreHorizontal, Pencil, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { folderPath, type MediaFolder } from '@/lib/media-api'
import { childrenOf, useMediaDropTarget } from './media-utils'
import { cn } from '@/lib/utils'

export type FolderAction = 'rename' | 'move' | 'delete'

export function FolderTree({
  folders,
  currentId,
  trash,
  onOpen,
  onOpenTrash,
  onDropItems,
  onAction,
  showTrash = true,
}: {
  folders: MediaFolder[]
  currentId: string | null
  trash: boolean
  onOpen: (id: string | null) => void
  onOpenTrash?: () => void
  onDropItems?: (folderId: string | null, ids: string[]) => void
  onAction?: (action: FolderAction, folder: MediaFolder) => void
  showTrash?: boolean
}) {
  const { t } = useTranslation('media')
  // Ανοιχτοί φάκελοι: ό,τι άνοιξε ο χρήστης + πάντα ο δρόμος προς τον τρέχοντα
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const ancestors = useMemo(() => new Set(folderPath(folders, currentId).map((f) => f.id)), [folders, currentId])
  const isOpen = (id: string) => expanded.has(id) || (ancestors.has(id) && id !== currentId)

  function toggle(id: string) {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (isOpen(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  function renderLevel(parentId: string | null, depth: number): ReactNode {
    return childrenOf(folders, parentId).map((folder) => {
      const hasChildren = folders.some((f) => f.parentId === folder.id)
      const open = isOpen(folder.id)
      return (
        <li key={folder.id}>
          <TreeRow
            label={folder.name}
            icon={open || folder.id === currentId ? FolderOpen : Folder}
            count={folder.itemCount}
            depth={depth}
            active={!trash && folder.id === currentId}
            expandable={hasChildren}
            open={open}
            onToggle={() => toggle(folder.id)}
            onClick={() => onOpen(folder.id)}
            onDrop={onDropItems && ((ids) => onDropItems(folder.id, ids))}
            menu={
              onAction && (folder.canEdit || folder.canDelete) ? (
                <FolderMenu folder={folder} onAction={onAction} />
              ) : undefined
            }
          />
          {hasChildren && open && <ul>{renderLevel(folder.id, depth + 1)}</ul>}
        </li>
      )
    })
  }

  return (
    <nav aria-label={t('folders')} className="text-sm">
      {/* grid-cols-1 = minmax(0, 1fr): τα μεγάλα ονόματα κόβονται (truncate) αντί να βγαίνουν πάνω από τη λίστα αρχείων */}
      <ul className="grid grid-cols-1 gap-0.5">
        <li>
          <TreeRow
            label={t('library')}
            icon={Library}
            depth={0}
            active={!trash && currentId === null}
            onClick={() => onOpen(null)}
            onDrop={onDropItems && ((ids) => onDropItems(null, ids))}
          />
          <ul>{renderLevel(null, 1)}</ul>
        </li>
        {showTrash && onOpenTrash && (
          <li className="mt-2 border-t pt-2">
            <TreeRow label={t('trash')} icon={Trash2} depth={0} active={trash} onClick={onOpenTrash} />
          </li>
        )}
      </ul>
    </nav>
  )
}

function TreeRow({
  label,
  icon: Icon,
  count,
  depth,
  active,
  expandable,
  open,
  onToggle,
  onClick,
  onDrop,
  menu,
}: {
  label: string
  icon: ComponentType<{ className?: string }>
  count?: number
  depth: number
  active: boolean
  expandable?: boolean
  open?: boolean
  onToggle?: () => void
  onClick: () => void
  onDrop?: (ids: string[]) => void
  menu?: ReactNode
}) {
  const { t } = useTranslation('media')
  const drop = useMediaDropTarget(onDrop)
  return (
    <div
      {...drop.props}
      className={cn(
        'group/row flex h-8 items-center rounded-lg pr-1 transition-colors hover:bg-white/4',
        active && 'bg-accent text-accent-foreground',
        drop.over && 'bg-primary/15 ring-1 ring-primary',
      )}
      style={{ paddingLeft: `${depth * 14 + 4}px` }}
    >
      {expandable ? (
        <button
          type="button"
          onClick={onToggle}
          aria-label={open ? t('tree.collapse', { name: label }) : t('tree.expand', { name: label })}
          aria-expanded={open}
          className="flex size-5 shrink-0 items-center justify-center rounded text-muted-foreground hover:text-foreground"
        >
          <ChevronRight className={cn('size-3.5 transition-transform', open && 'rotate-90')} />
        </button>
      ) : (
        <span className="size-5 shrink-0" />
      )}
      <button
        type="button"
        onClick={onClick}
        aria-current={active ? 'page' : undefined}
        className="flex min-w-0 flex-1 items-center gap-2 py-1 text-left"
      >
        <Icon className={cn('size-4 shrink-0', active ? 'text-primary' : 'text-muted-foreground')} />
        <span className="truncate">{label}</span>
        {!!count && <span className="ml-auto pl-1 text-xs text-muted-foreground tabular-nums">{count}</span>}
      </button>
      {menu}
    </div>
  )
}

export function FolderMenu({ folder, onAction, className }: { folder: MediaFolder; onAction: (action: FolderAction, folder: MediaFolder) => void; className?: string }) {
  const { t } = useTranslation('media')
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          aria-label={t('tree.folderActions', { name: folder.name })}
          className={cn('size-6 shrink-0 opacity-0 group-hover/row:opacity-100 focus-visible:opacity-100 data-[state=open]:opacity-100', className)}
        >
          <MoreHorizontal className="size-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start">
        {folder.canEdit && (
          <>
            <DropdownMenuItem onSelect={() => onAction('rename', folder)}>
              <Pencil /> {t('tree.rename')}
            </DropdownMenuItem>
            <DropdownMenuItem onSelect={() => onAction('move', folder)}>
              <FolderInput /> {t('tree.moveTo')}
            </DropdownMenuItem>
          </>
        )}
        {folder.canDelete && (
          <>
            {folder.canEdit && <DropdownMenuSeparator />}
            <DropdownMenuItem variant="destructive" onSelect={() => onAction('delete', folder)}>
              <Trash2 /> {t('tree.deleteFolder')}
            </DropdownMenuItem>
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
