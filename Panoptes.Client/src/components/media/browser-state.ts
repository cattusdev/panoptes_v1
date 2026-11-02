import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import type { MediaFilters, MediaKind, MediaSort, MediaVisibility } from '@/lib/media-api'

// Η κατάσταση του media browser (φάκελος, αναζήτηση, φίλτρα). Η σελίδα την κρατά στο URL,
// ο picker τοπικά: ο MediaBrowser δουλεύει το ίδιο και με τις δύο.
export type BrowserState = {
  folderId: string | null
  trash: boolean
  search: string // Ό,τι γράφει ο χρήστης (άμεσα)
  filters: MediaFilters // Με debounced αναζήτηση: αυτό πάει στο API
  openFolder: (id: string | null) => void
  openTrash: () => void
  setSearch: (value: string) => void
  setFilter: (key: 'kind' | 'owner' | 'visibility' | 'sort', value: string) => void
}

type Values = { folder: string | null; trash: boolean; q: string; kind: string; owner: string; visibility: string; sort: string }

function toFilters(values: Values, search: string): MediaFilters {
  return {
    folderId: values.folder,
    trash: values.trash,
    search: search.trim() || undefined,
    kind: (values.kind as MediaKind) || '',
    owner: values.owner === 'mine' ? 'mine' : '',
    visibility: (values.visibility as MediaVisibility) || '',
    sort: (values.sort as MediaSort) || 'newest',
  }
}

// Στο URL: ?folder=&view=trash&q=&kind=&owner=&visibility=&sort= (bookmark / back button)
export function useUrlBrowserState(): BrowserState {
  const [params, setParams] = useSearchParams()
  const [search, setSearchValue] = useState(params.get('q') ?? '')
  const debounced = useDebouncedValue(search)

  const values: Values = {
    folder: params.get('folder'),
    trash: params.get('view') === 'trash',
    q: params.get('q') ?? '',
    kind: params.get('kind') ?? '',
    owner: params.get('owner') ?? '',
    visibility: params.get('visibility') ?? '',
    sort: params.get('sort') ?? '',
  }

  function update(change: (next: URLSearchParams) => void, replace = true) {
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        change(next)
        return next
      },
      { replace },
    )
  }

  useEffect(() => {
    if (debounced !== (params.get('q') ?? '')) update((next) => (debounced ? next.set('q', debounced) : next.delete('q')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debounced])

  return {
    folderId: values.folder,
    trash: values.trash,
    search,
    filters: toFilters(values, debounced),
    openFolder: (id) => {
      setSearchValue('')
      update((next) => {
        next.delete('view')
        next.delete('q')
        if (id) next.set('folder', id)
        else next.delete('folder')
      }, false)
    },
    openTrash: () => {
      setSearchValue('')
      update((next) => {
        next.set('view', 'trash')
        next.delete('folder')
        next.delete('q')
      }, false)
    },
    setSearch: setSearchValue,
    setFilter: (key, value) => update((next) => (value ? next.set(key, value) : next.delete(key))),
  }
}

// Τοπικά (π.χ. μέσα σε dialog): χωρίς αλλαγές στο URL της σελίδας
export function useLocalBrowserState(initial: Partial<Values> = {}): BrowserState {
  const [values, setValues] = useState<Values>({ folder: null, trash: false, q: '', kind: '', owner: '', visibility: '', sort: '', ...initial })
  const [search, setSearchValue] = useState('')
  const debounced = useDebouncedValue(search)

  return {
    folderId: values.folder,
    trash: values.trash,
    search,
    filters: toFilters(values, debounced),
    openFolder: (id) => {
      setSearchValue('')
      setValues((v) => ({ ...v, folder: id, trash: false }))
    },
    openTrash: () => {
      setSearchValue('')
      setValues((v) => ({ ...v, folder: null, trash: true }))
    },
    setSearch: setSearchValue,
    setFilter: (key, value) => setValues((v) => ({ ...v, [key]: value })),
  }
}
