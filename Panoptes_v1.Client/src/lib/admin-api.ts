import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api, toQuery, type Paged } from '@/lib/api'

// ---------- Users ----------
export type UserListItem = {
  id: string
  email: string
  roles: string[]
  emailConfirmed: boolean
  isLockedOut: boolean
  isSelf: boolean
  canManage: boolean
  avatarUrl: string | null
}

export type UserDetail = {
  id: string
  email: string
  role: string | null
  isAdmin: boolean
  emailConfirmed: boolean
  isLockedOut: boolean
  lockoutEnd: string | null
  twoFactorEnabled: boolean
  isSelf: boolean
  canManage: boolean
  avatarUrl: string | null
}

export type UserFilters = { search?: string; role?: string; status?: string; page: number; pageSize?: number }

export function useUsers(filters: UserFilters) {
  return useQuery({
    queryKey: ['users', filters],
    queryFn: () => api<Paged<UserListItem>>(`/api/users${toQuery({ ...filters, pageSize: filters.pageSize ?? 20 })}`),
    placeholderData: keepPreviousData,
  })
}

export function useUser(id: string) {
  return useQuery({ queryKey: ['user', id], queryFn: () => api<UserDetail>(`/api/users/${id}`) })
}

export function useRoleOptions() {
  return useQuery({
    queryKey: ['role-options'],
    queryFn: () => api<{ all: string[]; assignable: string[] }>('/api/users/role-options'),
  })
}

// Μετά από κάθε αλλαγή χρήστη ανανεώνουμε λίστες, λεπτομέρειες και ό,τι εξαρτάται από αυτούς
function useInvalidateUsers() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all(
      [['users'], ['user'], ['roles'], ['role'], ['dashboard'], ['audit-logs']].map((queryKey) =>
        queryClient.invalidateQueries({ queryKey }),
      ),
    )
}

export function useCreateUser() {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: (input: { email: string; password: string; role: string | null }) =>
      api<UserDetail>('/api/users', { method: 'POST', body: input }),
    onSuccess: invalidate,
  })
}

export function useUpdateUser(id: string) {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: (input: { email: string; role: string | null }) => api<UserDetail>(`/api/users/${id}`, { method: 'PUT', body: input }),
    onSuccess: invalidate,
  })
}

export function useUserAction() {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'unlock' | 'promote' | 'delete' }): Promise<UserDetail | void> =>
      action === 'delete'
        ? api<void>(`/api/users/${id}`, { method: 'DELETE' })
        : api<UserDetail>(`/api/users/${id}/${action}`, { method: 'POST' }),
    onSuccess: invalidate,
  })
}

export function useResetPassword(id: string) {
  return useMutation({
    mutationFn: (newPassword: string) => api<void>(`/api/users/${id}/reset-password`, { method: 'POST', body: { newPassword } }),
  })
}

// ---------- Roles ----------
export type RoleListItem = {
  id: string
  name: string
  isSystem: boolean
  permissionCount: number
  userCount: number
  canManage: boolean
}

export type RoleDetail = {
  id: string
  name: string
  isSystem: boolean
  canManage: boolean
  readOnlyReason: string | null
  permissions: string[]
  grantablePermissions: string[]
  catalog: { module: string; permissions: { name: string; action: string }[] }[]
  members: { id: string; email: string; avatarUrl: string | null }[]
}

export function useRoles() {
  return useQuery({ queryKey: ['roles'], queryFn: () => api<RoleListItem[]>('/api/roles') })
}

export function useRole(id: string) {
  return useQuery({ queryKey: ['role', id], queryFn: () => api<RoleDetail>(`/api/roles/${id}`) })
}

function useInvalidateRoles() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all(
      [['roles'], ['role'], ['role-options'], ['users'], ['user'], ['dashboard'], ['session']].map((queryKey) =>
        queryClient.invalidateQueries({ queryKey }),
      ),
    )
}

export function useCreateRole() {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: (name: string) => api<RoleDetail>('/api/roles', { method: 'POST', body: { name } }),
    onSuccess: invalidate,
  })
}

export function useRenameRole(id: string) {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: (name: string) => api<RoleDetail>(`/api/roles/${id}`, { method: 'PUT', body: { name } }),
    onSuccess: invalidate,
  })
}

export function useSetRolePermissions(id: string) {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: (permissions: string[]) => api<RoleDetail>(`/api/roles/${id}/permissions`, { method: 'PUT', body: { permissions } }),
    onSuccess: invalidate,
  })
}

export function useDeleteRole() {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: (id: string) => api<void>(`/api/roles/${id}`, { method: 'DELETE' }),
    onSuccess: invalidate,
  })
}

// ---------- Audit logs ----------
export type AuditLogItem = {
  id: number
  type: 'Create' | 'Update' | 'Delete' | 'Manual' | 'None'
  tableName: string | null
  recordId: string | null
  dateTime: string
  userEmail: string | null
  source: string | null // Χωρίς χρήστη: ποιο κομμάτι του συστήματος (π.χ. "jobs:email.send")
  traceId: string | null
  changes: { field: string; oldValue: string | null; newValue: string | null }[]
}

const auditTypes: readonly string[] = ['Create', 'Update', 'Delete', 'Manual', 'None'] satisfies AuditLogItem['type'][]

// Το όνομα του τύπου στη γλώσσα του χρήστη (ό,τι άγνωστο στέλνει ο server εμφανίζεται όπως είναι)
export function useAuditTypeLabel() {
  const { t } = useTranslation('admin')
  return (type: string) => (auditTypes.includes(type) ? t(`audit.types.${type as AuditLogItem['type']}`) : type)
}

export type AuditFilters = { search?: string; type?: string; page: number }

export function useAuditLogs(filters: AuditFilters) {
  return useQuery({
    queryKey: ['audit-logs', filters],
    queryFn: () => api<Paged<AuditLogItem>>(`/api/audit-logs${toQuery({ ...filters, pageSize: 25 })}`),
    placeholderData: keepPreviousData,
  })
}
