import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, toQuery } from '@/lib/api'

// Η global αναζήτηση (/api/search): κάθε module επιστρέφει μόνο ό,τι βλέπει ο χρήστης
export type SearchHit = { type: string; id: string; title: string; subtitle: string | null; reference: string | null; isArchived: boolean }

export type SearchGroup = { type: string; hits: SearchHit[] }

export const searchMinLength = 2

export function useGlobalSearch(query: string) {
  const q = query.trim()
  return useQuery({
    queryKey: ['search', q],
    queryFn: ({ signal }) => api<{ query: string; groups: SearchGroup[] }>(`/api/search${toQuery({ q })}`, { signal }),
    enabled: q.length >= searchMinLength,
    placeholderData: keepPreviousData,
    staleTime: 15_000,
  })
}
