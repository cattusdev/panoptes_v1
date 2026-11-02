import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import { useDebouncedValue } from '@/hooks/use-debounced-value'

const pageSizes = [10, 20, 50, 100]

// Τα φίλτρα μιας λίστας ζουν στο URL (μοιράζεσαι / κάνεις bookmark τη λίστα όπως τη βλέπεις):
//   filters: οι τιμές των keys ("" = κανένα) · page, pageSize, sort · set(key, value): κάθε αλλαγή εκτός της σελίδας γυρνάει στη σελίδα 1
//   searchInput/setSearchInput: το κουτί αναζήτησης· γράφεται στο URL (filters.search) με debounce
export function useUrlFilters<K extends string>(keys: readonly K[], { defaultPageSize = 20, defaultSort = '' } = {}) {
  const [params, setParams] = useSearchParams()

  const filters = Object.fromEntries(keys.map((key) => [key, params.get(key) ?? ''])) as Record<K, string>
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1)
  const requestedSize = Number(params.get('pageSize'))
  const pageSize = pageSizes.includes(requestedSize) ? requestedSize : defaultPageSize
  const sort = params.get('sort') ?? defaultSort

  const set = useCallback(
    (key: K | 'page' | 'pageSize' | 'sort', value: string) => {
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          if (value) next.set(key, value)
          else next.delete(key)
          if (key !== 'page') next.delete('page')
          return next
        },
        { replace: true },
      )
    },
    [setParams],
  )

  const urlSearch = params.get('search') ?? ''
  const [searchInput, setSearchInput] = useState(urlSearch)
  const debouncedSearch = useDebouncedValue(searchInput)
  useEffect(() => {
    if (debouncedSearch !== urlSearch) set('search' as K, debouncedSearch)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedSearch])

  return {
    filters,
    page,
    pageSize,
    sort,
    set,
    searchInput,
    setSearchInput,
    // Υπάρχει ενεργό φίλτρο; (για το "κανένα αποτέλεσμα" vs "κενή λίστα")
    isFiltered: keys.some((key) => filters[key] !== ''),
    // Για τη TanStack/DataTable: "" = η προεπιλογή
    setSort: (value: string) => set('sort', value === defaultSort ? '' : value),
  }
}
