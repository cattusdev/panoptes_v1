import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api'

export type SessionUser = {
  id: string
  email: string
  isAdmin: boolean
  // Τα "ενεργά" permissions όπως τα υπολογίζει ο server (Admin = όλα)
  permissions: string[]
  avatarUrl: string | null
  // Η γλώσσα που διάλεξε (My account), null = η προεπιλογή της εφαρμογής
  language: string | null
}

type Session = { user: SessionUser | null }

export type LoginResult = 'success' | 'invalid' | 'lockedOut' | 'requiresTwoFactor' | 'notAllowed'

export const sessionQueryKey = ['session'] as const

export function useSession() {
  const query = useQuery({
    queryKey: sessionQueryKey,
    queryFn: () => api<Session>('/api/auth/session'),
    staleTime: 60_000,
  })
  return { ...query, user: query.data?.user ?? null }
}

export function hasPermission(user: SessionUser | null, permission: string) {
  return !!user && user.permissions.includes(permission)
}

// Admin area: Admin ή έστω ένα permission (ίδιος κανόνας με το AdminArea policy του server)
export function canAccessAdmin(user: SessionUser | null) {
  return !!user && (user.isAdmin || user.permissions.length > 0)
}

export function useLogin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { email: string; password: string; rememberMe: boolean }) =>
      api<{ result: LoginResult; user: SessionUser | null }>('/api/auth/login', { method: 'POST', body: input }),
    onSuccess: (data) => {
      if (data.result === 'success') queryClient.setQueryData(sessionQueryKey, { user: data.user })
    },
  })
}

export function useLogout() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api<void>('/api/auth/logout', { method: 'POST' }),
    onSuccess: () => {
      queryClient.clear()
      queryClient.setQueryData(sessionQueryKey, { user: null })
    },
  })
}

// Επιτρέπουμε μόνο τοπικά returnUrl (όχι //evil.com ή /\evil.com)
export function safeReturnUrl(value: string | null): string | null {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) return null
  return value
}

// Η γλώσσα του χρήστη (null = η προεπιλογή της εφαρμογής). Αποθηκεύεται στο προφίλ.
export function useUpdateLanguage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (language: string | null) => api<void>('/api/account/language', { method: 'PUT', body: { language } }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: sessionQueryKey }),
  })
}
