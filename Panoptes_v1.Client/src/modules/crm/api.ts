import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, toQuery, type Paged } from '@/lib/api'

// ---------- Τύποι (ίδιοι με τα DTOs του Modules/Crm/Api/CrmContracts.cs) ----------
export type ClientKind = 'Organization' | 'Individual'
export type ClientStatus = 'Active' | 'Inactive'
export type AddressKind = 'Billing' | 'Office' | 'Shipping' | 'Other'
export type ContactMethodKind = 'Email' | 'Phone' | 'Mobile' | 'Website'

export const addressKinds: AddressKind[] = ['Billing', 'Office', 'Shipping', 'Other']
export const contactMethodKinds: ContactMethodKind[] = ['Email', 'Phone', 'Mobile', 'Website']

export type ContactMethod = { kind: ContactMethodKind; value: string; label: string | null }

export type Address = {
  kind: AddressKind
  line1: string
  line2: string | null
  city: string | null
  postalCode: string | null
  region: string | null
  country: string | null
}

export type PrimaryContact = { personId: string; name: string; role: string | null; email: string | null; phone: string | null }

export type ClientListItem = {
  id: string
  ref: string
  kind: ClientKind
  displayName: string
  legalName: string | null
  taxId: string | null
  status: ClientStatus
  isArchived: boolean
  tags: string[]
  primaryContact: PrimaryContact | null
  createdAt: string
}

export type Client = {
  id: string
  ref: string
  kind: ClientKind
  displayName: string
  legalName: string | null
  taxId: string | null
  taxOffice: string | null
  status: ClientStatus
  defaultCurrency: string
  tags: string[]
  addresses: Address[]
  contactMethods: ContactMethod[]
  notes: string | null
  origin: 'Live' | 'Historical' | 'Import'
  archivedAt: string | null
  createdAt: string
  version: number
  canEdit: boolean
  canArchive: boolean
}

export type ClientInput = {
  kind: ClientKind
  displayName: string
  legalName: string | null
  taxId: string | null
  taxOffice: string | null
  status: ClientStatus
  defaultCurrency: string
  tags: string[]
  addresses: Address[]
  contactMethods: ContactMethod[]
  notes: string | null
  version?: number
  duplicateReason?: string | null
}

export type LinkedClient = { id: string; ref: string; displayName: string; isArchived: boolean }

export type ClientContact = {
  id: string
  personId: string
  firstName: string
  lastName: string | null
  name: string
  role: string | null
  isPrimary: boolean
  isBillingContact: boolean
  contactMethods: ContactMethod[]
  alsoContactFor: LinkedClient[]
  version: number
}

export type PersonListItem = {
  id: string
  firstName: string
  lastName: string | null
  name: string
  email: string | null
  phone: string | null
  clients: LinkedClient[]
}

export type Person = {
  id: string
  firstName: string
  lastName: string | null
  name: string
  contactMethods: ContactMethod[]
  notes: string | null
  clients: {
    contactId: string
    clientId: string
    clientRef: string
    clientName: string
    clientArchived: boolean
    role: string | null
    isPrimary: boolean
    isBillingContact: boolean
  }[]
  version: number
  canEdit: boolean
}

export type PersonInput = {
  firstName: string
  lastName: string | null
  contactMethods: ContactMethod[]
  notes: string | null
  version?: number
}

export type DuplicateReason = 'taxId' | 'name' | 'email' | 'phone'

export type ClientMatch = {
  id: string
  ref: string
  displayName: string
  legalName: string | null
  taxId: string | null
  isArchived: boolean
  reasons: DuplicateReason[]
}

export type PersonMatch = { id: string; name: string; email: string | null; reasons: DuplicateReason[] }

export type ActivityItem = {
  id: string
  entityType: string
  entityId: string
  verb: string
  data: Record<string, unknown>
  actor: { id: string; email: string } | null
  source: string | null
  occurredAt: string
}

// Ο ίδιος ΑΦΜ με άλλον πελάτη: ο server ζητά αιτιολογία (409, code crm.duplicateTaxId)
export function duplicateTaxIdMatches(error: unknown): ClientMatch[] | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null
  const data = (error.data ?? {}) as { code?: string; details?: { matches?: ClientMatch[] } }
  return data.code === 'crm.duplicateTaxId' ? (data.details?.matches ?? []) : null
}

// ---------- Πελάτες ----------
export type ClientFilters = { search?: string; status?: string; kind?: string; tag?: string; sort?: string; page: number; pageSize?: number }

export function useClients(filters: ClientFilters) {
  return useQuery({
    queryKey: ['crm', 'clients', filters],
    queryFn: () => api<Paged<ClientListItem>>(`/api/crm/clients${toQuery({ ...filters })}`),
    placeholderData: keepPreviousData,
  })
}

export function useClient(id: string) {
  return useQuery({ queryKey: ['crm', 'client', id], queryFn: () => api<Client>(`/api/crm/clients/${id}`), retry: false })
}

export function useClientTags() {
  return useQuery({ queryKey: ['crm', 'tags'], queryFn: () => api<string[]>('/api/crm/clients/tags'), staleTime: 60_000 })
}

// Μετά από κάθε αλλαγή: λίστες, λεπτομέρειες, επαφές, ιστορικό (όλα κάτω από το ['crm'])
function useInvalidateCrm() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: ['crm'] })
}

export function useCreateClient() {
  const invalidate = useInvalidateCrm()
  return useMutation({
    mutationFn: (input: ClientInput) => api<Client>('/api/crm/clients', { method: 'POST', body: input }),
    onSuccess: invalidate,
  })
}

export function useUpdateClient(id: string) {
  const invalidate = useInvalidateCrm()
  return useMutation({
    mutationFn: (input: ClientInput) => api<Client>(`/api/crm/clients/${id}`, { method: 'PUT', body: input }),
    onSuccess: invalidate,
  })
}

export function useArchiveClient(id: string) {
  const invalidate = useInvalidateCrm()
  return useMutation({
    mutationFn: ({ archive, version }: { archive: boolean; version: number }) =>
      api<Client>(`/api/crm/clients/${id}/${archive ? 'archive' : 'unarchive'}`, { method: 'POST', body: { version } }),
    onSuccess: invalidate,
  })
}

export type ClientDuplicateQuery = {
  excludeId?: string
  displayName?: string
  legalName?: string | null
  taxId?: string | null
  contactMethods?: ContactMethod[]
}

// Πιθανά διπλότυπα καθώς γράφει ο χρήστης (ο καλών κάνει debounce)
export function useClientDuplicates(query: ClientDuplicateQuery, enabled: boolean) {
  return useQuery({
    queryKey: ['crm', 'client-duplicates', query],
    queryFn: () => api<{ matches: ClientMatch[] }>('/api/crm/clients/duplicates', { method: 'POST', body: query }),
    enabled,
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  })
}

export function useClientActivity(id: string, page: number) {
  return useQuery({
    queryKey: ['crm', 'activity', id, page],
    queryFn: () => api<Paged<ActivityItem>>(`/api/crm/clients/${id}/activity${toQuery({ page, pageSize: 20 })}`),
    placeholderData: keepPreviousData,
  })
}

// ---------- Επαφές πελάτη ----------
export function useClientContacts(id: string) {
  return useQuery({ queryKey: ['crm', 'contacts', id], queryFn: () => api<ClientContact[]>(`/api/crm/clients/${id}/contacts`) })
}

export type AddContactInput = {
  personId?: string | null
  person?: PersonInput | null
  role: string | null
  isPrimary: boolean
  isBillingContact: boolean
}

export function useAddContact(clientId: string) {
  const invalidate = useInvalidateCrm()
  return useMutation({
    mutationFn: (input: AddContactInput) => api<ClientContact[]>(`/api/crm/clients/${clientId}/contacts`, { method: 'POST', body: input }),
    onSuccess: invalidate,
  })
}

export function useUpdateContact(clientId: string) {
  const invalidate = useInvalidateCrm()
  return useMutation({
    mutationFn: ({ id, ...input }: { id: string; role: string | null; isPrimary: boolean; isBillingContact: boolean; version: number }) =>
      api<ClientContact[]>(`/api/crm/clients/${clientId}/contacts/${id}`, { method: 'PUT', body: input }),
    onSuccess: invalidate,
  })
}

export function useRemoveContact(clientId: string) {
  const invalidate = useInvalidateCrm()
  return useMutation({
    mutationFn: (contactId: string) => api<ClientContact[]>(`/api/crm/clients/${clientId}/contacts/${contactId}`, { method: 'DELETE' }),
    onSuccess: invalidate,
  })
}

// ---------- Πρόσωπα ----------
export type PeopleFilters = { search?: string; clientId?: string; sort?: string; page: number; pageSize?: number }

export function usePeople(filters: PeopleFilters, enabled = true) {
  return useQuery({
    queryKey: ['crm', 'people', filters],
    queryFn: () => api<Paged<PersonListItem>>(`/api/crm/people${toQuery({ ...filters })}`),
    placeholderData: keepPreviousData,
    enabled,
  })
}

export function usePerson(id: string | null) {
  return useQuery({
    queryKey: ['crm', 'person', id],
    queryFn: () => api<Person>(`/api/crm/people/${id}`),
    enabled: !!id,
    retry: false,
  })
}

export function useCreatePerson() {
  const invalidate = useInvalidateCrm()
  return useMutation({
    mutationFn: (input: PersonInput) => api<Person>('/api/crm/people', { method: 'POST', body: input }),
    onSuccess: invalidate,
  })
}

export function useUpdatePerson(id: string) {
  const invalidate = useInvalidateCrm()
  return useMutation({
    mutationFn: (input: PersonInput) => api<Person>(`/api/crm/people/${id}`, { method: 'PUT', body: input }),
    onSuccess: invalidate,
  })
}

export function usePersonDuplicates(input: PersonInput, excludeId: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: ['crm', 'person-duplicates', input, excludeId],
    queryFn: () => api<PersonMatch[]>(`/api/crm/people/duplicates${toQuery({ excludeId })}`, { method: 'POST', body: input }),
    enabled,
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  })
}
