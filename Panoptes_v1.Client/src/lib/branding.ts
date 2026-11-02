import { useEffect } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api'
import type { MediaItem } from '@/lib/media-api'

export type Branding = { appName: string; logoUrl: string | null; faviconUrl: string | null }
export type BrandingSettings = { appName: string; logo: MediaItem | null; favicon: MediaItem | null }

export const defaultBranding: Branding = { appName: 'Panoptes v1.0', logoUrl: null, faviconUrl: null }

const storageKey = 'panoptes-v1.branding'

// Το τελευταίο branding που είδαμε: εμφανίζεται αμέσως στο επόμενο άνοιγμα (χωρίς "αναβόσβημα" του default)
function cached(): Branding | undefined {
  try {
    const value = localStorage.getItem(storageKey)
    return value ? (JSON.parse(value) as Branding) : undefined
  } catch {
    return undefined
  }
}

export function useBranding(): Branding {
  const query = useQuery({
    queryKey: ['branding'],
    queryFn: async () => {
      const branding = await api<Branding>('/api/branding')
      try {
        localStorage.setItem(storageKey, JSON.stringify(branding))
      } catch {
        // Δεν πειράζει: απλά δεν το θυμόμαστε
      }
      return branding
    },
    placeholderData: cached,
    staleTime: 5 * 60_000,
  })
  return query.data ?? defaultBranding
}

// Τίτλος καρτέλας και favicon από το branding
export function useBrandingHead() {
  const { appName, faviconUrl } = useBranding()
  useEffect(() => {
    document.title = appName
    const link = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
    if (!link) return
    link.href = faviconUrl ?? '/img/logo.svg'
    link.type = faviconUrl ? '' : 'image/svg+xml'
  }, [appName, faviconUrl])
}

export function useBrandingSettings() {
  return useQuery({ queryKey: ['branding-settings'], queryFn: () => api<BrandingSettings>('/api/branding/settings') })
}

export function useUpdateBranding() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { appName: string; logoMediaId: string | null; faviconMediaId: string | null }) =>
      api<BrandingSettings>('/api/branding', { method: 'PUT', body: input }),
    onSuccess: () =>
      Promise.all([['branding'], ['branding-settings'], ['media'], ['media-item']].map((queryKey) => queryClient.invalidateQueries({ queryKey }))),
  })
}
