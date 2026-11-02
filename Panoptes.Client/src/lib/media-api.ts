import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { currentLanguage } from '@/i18n'
import { api, toQuery } from '@/lib/api'

export type MediaKind = 'Image' | 'Document' | 'Archive'
export type MediaVisibility = 'Private' | 'Public'
export type MediaVariant = 'thumb' | 'medium' | 'large' | 'original'

export type MediaUrls = { thumb: string | null; medium: string | null; large: string | null; original: string; download: string }

export type MediaVersion = {
  id: string
  number: number
  fileName: string
  extension: string
  contentType: string
  sizeBytes: number
  width: number | null
  height: number | null
  dominantColor: string | null
  createdAt: string
  createdBy: string | null
  isCurrent: boolean
  urls: MediaUrls
}

export type MediaItem = {
  id: string
  title: string
  altText: string | null
  description: string | null
  kind: MediaKind
  visibility: MediaVisibility
  folderId: string | null
  ownerId: string
  ownerEmail: string | null
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  version: MediaVersion
  canEdit: boolean
  canDelete: boolean
}

export type MediaFolder = { id: string; name: string; parentId: string | null; itemCount: number; canEdit: boolean; canDelete: boolean }

export type MediaSettings = {
  chunkSize: number
  maxPixels: number
  maxVersions: number
  trashRetentionDays: number
  allowSvg: boolean
  kinds: { kind: MediaKind; extensions: string[]; maxBytes: number }[]
}

export type MediaSort = 'newest' | 'oldest' | 'name'

export type MediaFilters = {
  folderId?: string | null
  search?: string
  kind?: MediaKind | ''
  owner?: 'mine' | ''
  visibility?: MediaVisibility | ''
  sort?: MediaSort
  trash?: boolean
}

type MediaPage = { items: MediaItem[]; nextCursor: string | null }

export function useMediaSettings() {
  return useQuery({ queryKey: ['media-settings'], queryFn: () => api<MediaSettings>('/api/media/settings'), staleTime: 5 * 60_000 })
}

// Infinite scroll: κάθε σελίδα φέρνει τον cursor της επόμενης
export function useMediaList(filters: MediaFilters, { enabled = true }: { enabled?: boolean } = {}) {
  return useInfiniteQuery({
    queryKey: ['media', filters],
    queryFn: ({ pageParam, signal }) =>
      api<MediaPage>(
        `/api/media${toQuery({
          folderId: filters.search ? undefined : filters.folderId,
          search: filters.search,
          kind: filters.kind,
          owner: filters.owner,
          visibility: filters.visibility,
          sort: filters.sort,
          trash: filters.trash ? 'true' : undefined,
          cursor: pageParam,
          limit: 60,
        })}`,
        { signal },
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page) => page.nextCursor ?? undefined,
    enabled,
  })
}

export function useMediaItem(id: string | null) {
  return useQuery({ queryKey: ['media-item', id], queryFn: () => api<MediaItem>(`/api/media/${id}`), enabled: !!id })
}

export function useMediaFolders() {
  return useQuery({ queryKey: ['media-folders'], queryFn: () => api<MediaFolder[]>('/api/media/folders') })
}

export function fetchFolderSummary(id: string) {
  return api<{ files: number; folders: number }>(`/api/media/folders/${id}/summary`)
}

// Κάθε αλλαγή επηρεάζει λίστες, φακέλους (πλήθος αρχείων) και τις λεπτομέρειες
export function useInvalidateMedia() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all(
      [['media'], ['media-item'], ['media-versions'], ['media-folders'], ['audit-logs']].map((queryKey) =>
        queryClient.invalidateQueries({ queryKey }),
      ),
    )
}

export function useUpdateMedia(id: string) {
  const invalidate = useInvalidateMedia()
  return useMutation({
    mutationFn: (input: { title: string; altText: string | null; description: string | null; visibility: MediaVisibility }) =>
      api<MediaItem>(`/api/media/${id}`, { method: 'PUT', body: input }),
    onSuccess: invalidate,
  })
}

export type BulkAction = 'trash' | 'restore' | 'purge'

export function useBulkMediaAction() {
  const invalidate = useInvalidateMedia()
  return useMutation({
    mutationFn: ({ action, ids }: { action: BulkAction; ids: string[] }) => api(`/api/media/${action}`, { method: 'POST', body: { ids } }),
    onSuccess: invalidate,
  })
}

export function useEmptyTrash() {
  const invalidate = useInvalidateMedia()
  return useMutation({
    mutationFn: () => api<{ deleted: number }>('/api/media/trash/empty', { method: 'POST' }),
    onSuccess: invalidate,
  })
}

export function useMediaVersions(itemId: string | null) {
  return useQuery({
    queryKey: ['media-versions', itemId],
    queryFn: () => api<MediaVersion[]>(`/api/media/${itemId}/versions`),
    enabled: !!itemId,
  })
}

export function useRestoreVersion(itemId: string) {
  const invalidate = useInvalidateMedia()
  return useMutation({
    mutationFn: (versionId: string) => api<MediaItem>(`/api/media/${itemId}/versions/${versionId}/restore`, { method: 'POST' }),
    onSuccess: invalidate,
  })
}

export function useDeleteVersion(itemId: string) {
  const invalidate = useInvalidateMedia()
  return useMutation({
    mutationFn: (versionId: string) => api(`/api/media/${itemId}/versions/${versionId}`, { method: 'DELETE' }),
    onSuccess: invalidate,
  })
}

export function useMoveMedia() {
  const invalidate = useInvalidateMedia()
  return useMutation({
    mutationFn: ({ ids, folderId }: { ids: string[]; folderId: string | null }) =>
      api('/api/media/move', { method: 'POST', body: { ids, folderId } }),
    onSuccess: invalidate,
  })
}

export function useSaveFolder() {
  const invalidate = useInvalidateMedia()
  return useMutation({
    mutationFn: ({ id, name, parentId }: { id?: string; name: string; parentId: string | null }) =>
      id
        ? api<MediaFolder>(`/api/media/folders/${id}`, { method: 'PUT', body: { name, parentId } })
        : api<MediaFolder>('/api/media/folders', { method: 'POST', body: { name, parentId } }),
    onSuccess: invalidate,
  })
}

export function useMoveFolder() {
  const invalidate = useInvalidateMedia()
  return useMutation({
    mutationFn: ({ id, parentId }: { id: string; parentId: string | null }) =>
      api<MediaFolder>(`/api/media/folders/${id}/move`, { method: 'POST', body: { parentId } }),
    onSuccess: invalidate,
  })
}

export function useDeleteFolder() {
  const invalidate = useInvalidateMedia()
  return useMutation({
    mutationFn: ({ id, withContents }: { id: string; withContents: boolean }) =>
      api(`/api/media/folders/${id}${withContents ? '?withContents=true' : ''}`, { method: 'DELETE' }),
    onSuccess: invalidate,
  })
}

// ---------- Βοηθητικά ----------

// Οι μονάδες μένουν B/KB/MB/GB· το δεκαδικό ακολουθεί τη γλώσσα (1.5 MB / 1,5 MB), χωρίς διαχωριστικό χιλιάδων
export function formatBytes(bytes: number) {
  const format = (value: number, minDigits: number, maxDigits: number) =>
    new Intl.NumberFormat(currentLanguage(), { minimumFractionDigits: minDigits, maximumFractionDigits: maxDigits, useGrouping: false }).format(value)
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${format(bytes / (1024 * 1024), 0, 1)} MB`
  return `${format(bytes / (1024 * 1024 * 1024), 1, 1)} GB`
}

// Το πλήρες path ενός φακέλου (ρίζα -> φάκελος)
export function folderPath(folders: MediaFolder[] | undefined, id: string | null | undefined): MediaFolder[] {
  const path: MediaFolder[] = []
  const byId = new Map((folders ?? []).map((f) => [f.id, f]))
  let current = id ? byId.get(id) : undefined
  while (current && path.length < 20) {
    path.unshift(current)
    current = current.parentId ? byId.get(current.parentId) : undefined
  }
  return path
}

export function absoluteUrl(url: string) {
  return new URL(url, window.location.origin).toString()
}
