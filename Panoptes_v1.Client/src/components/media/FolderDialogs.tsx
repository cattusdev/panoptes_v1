import { useState, type ComponentType, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ChevronRight, Folder, Library } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Spinner } from '@/components/ui/spinner'
import { problemMessage } from '@/lib/api'
import { useMediaFolders, useSaveFolder, type MediaFolder } from '@/lib/media-api'
import { cn } from '@/lib/utils'
import { childrenOf } from './media-utils'

// Νέος φάκελος ή μετονομασία
export function FolderNameDialog({
  open,
  onOpenChange,
  folder,
  parentId,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  folder?: MediaFolder | null
  parentId: string | null
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-sm">
        {open && <FolderNameForm key={folder?.id ?? 'new'} folder={folder} parentId={parentId} onDone={() => onOpenChange(false)} />}
      </DialogContent>
    </Dialog>
  )
}

function FolderNameForm({ folder, parentId, onDone }: { folder?: MediaFolder | null; parentId: string | null; onDone: () => void }) {
  const { t } = useTranslation('media')
  const save = useSaveFolder()
  const [name, setName] = useState(folder?.name ?? '')
  const [error, setError] = useState('')

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    try {
      const saved = await save.mutateAsync({ id: folder?.id, name, parentId: folder ? folder.parentId : parentId })
      toast.success(folder ? t('folderDialog.renamed') : t('folderDialog.created', { name: saved.name }))
      onDone()
    } catch (err) {
      setError(problemMessage(err))
    }
  }

  return (
    <form onSubmit={onSubmit}>
      <DialogHeader className="mb-5">
        <DialogTitle>{folder ? t('folderDialog.renameTitle') : t('folderDialog.newTitle')}</DialogTitle>
        <DialogDescription>{folder ? t('folderDialog.renameDescription') : t('folderDialog.newDescription')}</DialogDescription>
      </DialogHeader>
      <Field data-invalid={!!error || undefined}>
        <FieldLabel htmlFor="folder-name">{t('name')}</FieldLabel>
        <Input id="folder-name" autoFocus required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} aria-invalid={!!error || undefined} />
        <FieldError>{error}</FieldError>
      </Field>
      <DialogFooter className="mt-6">
        <Button type="button" variant="outline" onClick={onDone}>
          {t('actions.cancel', { ns: 'common' })}
        </Button>
        <Button type="submit" disabled={save.isPending || !name.trim()}>
          {save.isPending && <Spinner />}
          {folder ? t('actions.save', { ns: 'common' }) : t('folderDialog.create')}
        </Button>
      </DialogFooter>
    </form>
  )
}

// Επιλογή προορισμού (για αρχεία ή φακέλους)
export function MoveDialog({
  open,
  onOpenChange,
  title,
  description,
  excludeFolderId,
  currentFolderId,
  onMove,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: ReactNode
  excludeFolderId?: string // Ένας φάκελος δεν μπαίνει στον εαυτό του ή στους υποφακέλους του (κρύβεται όλο το κλαδί)
  currentFolderId: string | null
  onMove: (folderId: string | null) => Promise<unknown>
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        {open && (
          <MoveForm
            title={title}
            description={description}
            excludeFolderId={excludeFolderId}
            currentFolderId={currentFolderId}
            onMove={onMove}
            onDone={() => onOpenChange(false)}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}

function MoveForm({
  title,
  description,
  excludeFolderId,
  currentFolderId,
  onMove,
  onDone,
}: {
  title: string
  description?: ReactNode
  excludeFolderId?: string
  currentFolderId: string | null
  onMove: (folderId: string | null) => Promise<unknown>
  onDone: () => void
}) {
  const { t } = useTranslation('media')
  const folders = useMediaFolders()
  const [target, setTarget] = useState<string | null>(currentFolderId)
  const [pending, setPending] = useState(false)
  const all = (folders.data ?? []).filter((f) => f.id !== excludeFolderId)

  async function move() {
    setPending(true)
    try {
      await onMove(target)
      onDone()
    } catch (err) {
      toast.error(problemMessage(err))
    } finally {
      setPending(false)
    }
  }

  function renderLevel(parentId: string | null, depth: number): ReactNode {
    return childrenOf(all, parentId).map((folder) => (
      <div key={folder.id}>
        <Destination label={folder.name} depth={depth} selected={target === folder.id} onSelect={() => setTarget(folder.id)} icon={Folder} />
        {renderLevel(folder.id, depth + 1)}
      </div>
    ))
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        {description && <DialogDescription>{description}</DialogDescription>}
      </DialogHeader>
      <ScrollArea className="h-72 rounded-lg border p-1.5">
        <div role="radiogroup" aria-label={t('moveDialog.destination')}>
          <Destination label={t('moveDialog.libraryTop')} depth={0} selected={target === null} onSelect={() => setTarget(null)} icon={Library} />
          {renderLevel(null, 1)}
        </div>
      </ScrollArea>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onDone}>
          {t('actions.cancel', { ns: 'common' })}
        </Button>
        <Button onClick={move} disabled={pending || target === currentFolderId}>
          {pending && <Spinner />}
          {t('moveDialog.moveHere')}
        </Button>
      </DialogFooter>
    </>
  )
}

function Destination({
  label,
  depth,
  selected,
  onSelect,
  icon: Icon,
}: {
  label: string
  depth: number
  selected: boolean
  onSelect: () => void
  icon: ComponentType<{ className?: string }>
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      onClick={onSelect}
      className={cn('flex w-full items-center gap-2 rounded-md py-1.5 pr-2 text-left text-sm hover:bg-white/4', selected && 'bg-primary/15 text-foreground')}
      style={{ paddingLeft: `${depth * 16 + 8}px` }}
    >
      {depth > 0 && <ChevronRight className="size-3 text-muted-foreground/60" />}
      <Icon className={cn('size-4', selected ? 'text-primary' : 'text-muted-foreground')} />
      <span className="truncate">{label}</span>
    </button>
  )
}
