import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api'

export type GeneralSettings = { defaultLanguage: string; dateFormat: string; timeFormat: string; timeZone: string | null }

export const defaultGeneralSettings: GeneralSettings = { defaultLanguage: 'en', dateFormat: 'd MMM yyyy', timeFormat: '24h', timeZone: null }

export type SmtpSecurity = 'Auto' | 'None' | 'StartTls' | 'SslOnConnect'

export type EmailTransportSource = 'Pickup' | 'Custom' | 'Configuration' | 'DevelopmentFolder' | 'NotConfigured'

export type EmailSettings = {
  enabled: boolean
  host: string
  port: number
  security: SmtpSecurity
  username: string
  senderName: string
  senderEmail: string
  hasPassword: boolean
  status: { source: EmailTransportSource; detail: string }
}

// password: null = κρατάμε το αποθηκευμένο
export type EmailSettingsInput = Omit<EmailSettings, 'hasPassword' | 'status'> & { password: string | null; clearPassword: boolean }

// Settings → Finance. trackingStartDate: DateOnly ("yyyy-MM-dd")
export type FinanceSettings = { defaultVatRate: number; trackingStartDate: string }

// reason: υποχρεωτικό μόνο όταν αλλάζει η ημερομηνία έναρξης
export type FinanceSettingsInput = FinanceSettings & { reason: string | null }

const storageKey = 'panoptes-v1.general-settings'

// Όπως στο branding: οι τελευταίες ρυθμίσεις εμφανίζονται αμέσως (χωρίς να αλλάζει η μορφή των ημερομηνιών μετά το φόρτωμα)
function cached(): GeneralSettings | undefined {
  try {
    const value = localStorage.getItem(storageKey)
    return value ? (JSON.parse(value) as GeneralSettings) : undefined
  } catch {
    return undefined
  }
}

function remember(settings: GeneralSettings) {
  try {
    localStorage.setItem(storageKey, JSON.stringify(settings))
  } catch {
    // Δεν πειράζει: απλά δεν το θυμόμαστε
  }
  return settings
}

// Δημόσιο (και χωρίς login): χρειάζεται όπου δείχνουμε ημερομηνίες
export function useGeneralSettings() {
  return useQuery({
    queryKey: ['general-settings'],
    queryFn: async () => remember(await api<GeneralSettings>('/api/settings/general')),
    placeholderData: cached,
    staleTime: 5 * 60_000,
  })
}

export function useUpdateGeneralSettings() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: GeneralSettings) => api<GeneralSettings>('/api/settings/general', { method: 'PUT', body: input }),
    onSuccess: (settings) => queryClient.setQueryData(['general-settings'], remember(settings)),
  })
}

export function useEmailSettings() {
  return useQuery({ queryKey: ['email-settings'], queryFn: () => api<EmailSettings>('/api/settings/email') })
}

export function useUpdateEmailSettings() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: EmailSettingsInput) => api<EmailSettings>('/api/settings/email', { method: 'PUT', body: input }),
    onSuccess: (settings) => queryClient.setQueryData(['email-settings'], settings),
  })
}

export function useSendTestEmail() {
  return useMutation({
    mutationFn: (input: EmailSettingsInput & { to: string }) =>
      api<{ to: string }>('/api/settings/email/test', { method: 'POST', body: input }),
  })
}

export function useFinanceSettings() {
  return useQuery({ queryKey: ['finance-settings'], queryFn: () => api<FinanceSettings>('/api/settings/finance') })
}

export function useUpdateFinanceSettings() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: FinanceSettingsInput) => api<FinanceSettings>('/api/settings/finance', { method: 'PUT', body: input }),
    onSuccess: (settings) => queryClient.setQueryData(['finance-settings'], settings),
  })
}
