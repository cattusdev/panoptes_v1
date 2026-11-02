import { useEffect, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { ChevronDown, Pencil, Plus, ShieldPlus, Trash2, Users } from 'lucide-react'
import { SearchInput, TableEmpty, UserAvatar, UserStatusBadge } from '@/components/admin/Common'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { DataTable, TablePagination, type Column } from '@/components/admin/DataTable'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { AccessTabs } from '@/components/admin/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { useRoleOptions, useUserAction, useUsers, type UserListItem } from '@/lib/admin-api'
import { problemMessage } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import { Permissions } from '@/lib/permissions'
import { CreateUserDialog } from './CreateUserDialog'

const statuses = ['active', 'locked', 'unconfirmed'] as const

type PendingAction = { user: UserListItem; action: 'promote' | 'delete' } | null

export function UsersPage() {
  const { t } = useTranslation('admin')
  const navigate = useNavigate()
  const { user: me } = useSession()
  const [params, setParams] = useSearchParams()
  const [search, setSearch] = useState(params.get('search') ?? '')
  const debouncedSearch = useDebouncedValue(search)
  const [createOpen, setCreateOpen] = useState(false)
  const [pending, setPending] = useState<PendingAction>(null)

  const role = params.get('role') ?? ''
  const status = params.get('status') ?? ''
  const page = Number(params.get('page') ?? '1') || 1

  // Τα φίλτρα ζουν στο URL (μπορείς να το μοιραστείς / bookmark)
  function setFilter(key: string, value: string) {
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
  }

  useEffect(() => {
    if (debouncedSearch !== (params.get('search') ?? '')) setFilter('search', debouncedSearch)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedSearch])

  const users = useUsers({ search: debouncedSearch, role, status, page })
  const roleOptions = useRoleOptions()
  const userAction = useUserAction()

  const canCreate = hasPermission(me, Permissions.Users.Create)
  const canDelete = hasPermission(me, Permissions.Users.Delete)
  const filtered = !!(debouncedSearch || role || status)
  const statusOptions = statuses.map((value) => ({ value, label: t(`status.${value}`) }))

  const columns: Column<UserListItem>[] = [
    {
      key: 'user',
      header: t('users.list.columns.user'),
      cell: (u) => (
        <div className="flex items-center gap-3">
          <UserAvatar email={u.email} avatarUrl={u.avatarUrl} active={!u.isLockedOut} />
          <div className="flex min-w-0 flex-wrap items-center gap-2">
            <span className="font-medium">{u.email}</span>
            {u.roles.includes('Admin') && <Badge variant="purple">{t('shared.administrator')}</Badge>}
            {u.isSelf && <Badge variant="info">{t('shared.you')}</Badge>}
          </div>
        </div>
      ),
    },
    {
      key: 'role',
      header: t('users.list.columns.role'),
      cell: (u) =>
        u.roles.length ? (
          <div className="flex flex-wrap gap-1">
            {u.roles.map((r) => (
              <Badge key={r} variant="success">
                {r}
              </Badge>
            ))}
          </div>
        ) : (
          <Badge variant="muted">{t('users.list.noAccess')}</Badge>
        ),
    },
    { key: 'status', header: t('users.list.columns.status'), cell: (u) => <UserStatusBadge isLockedOut={u.isLockedOut} emailConfirmed={u.emailConfirmed} /> },
    {
      key: 'actions',
      header: <span className="sr-only">{t('shared.actions')}</span>,
      className: 'w-24 text-right',
      cell: (u) => {
        const canPromote = me?.isAdmin && !u.roles.includes('Admin')
        const canRemove = canDelete && u.canManage && !u.isSelf
        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild onClick={(e) => e.stopPropagation()}>
              <Button variant="ghost" size="sm" aria-label={t('shared.actionsFor', { name: u.email })} className="font-semibold">
                {t('users.list.view')} <ChevronDown />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" onClick={(e) => e.stopPropagation()}>
              <DropdownMenuItem onSelect={() => navigate(`/admin/users/${u.id}`)}>
                <Pencil /> {u.canManage ? t('users.list.editUser') : t('users.list.viewUser')}
              </DropdownMenuItem>
              {canPromote && (
                <DropdownMenuItem onSelect={() => setPending({ user: u, action: 'promote' })}>
                  <ShieldPlus /> {t('users.list.promote')}
                </DropdownMenuItem>
              )}
              {canRemove && (
                <>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem variant="destructive" onSelect={() => setPending({ user: u, action: 'delete' })}>
                    <Trash2 /> {t('users.list.deleteUser')}
                  </DropdownMenuItem>
                </>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        )
      },
    },
  ]

  async function runPending() {
    if (!pending) return
    const { user, action } = pending
    try {
      await userAction.mutateAsync({ id: user.id, action })
      toast.success(action === 'promote' ? t('users.list.promoted', { email: user.email }) : t('users.list.deleted', { email: user.email }))
    } catch (error) {
      toast.error(problemMessage(error))
      throw error
    }
  }

  return (
    <>
      <AccessTabs />

      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <span className="text-sm text-muted-foreground">
          {users.data
            ? filtered
              ? t('users.list.matchingCount', { count: users.data.total })
              : t('users.list.totalCount', { count: users.data.total })
            : ' '}
        </span>
        {canCreate && (
          <Button onClick={() => setCreateOpen(true)}>
            <Plus /> {t('users.list.newUser')}
          </Button>
        )}
      </div>

      <div className="mb-3 flex flex-col gap-2 sm:flex-row">
        <SearchInput value={search} onChange={setSearch} placeholder={t('users.list.search')} />
        <div className="flex gap-2 sm:ml-auto">
          <OptionSelect
            ariaLabel={t('users.list.filterByRole')}
            value={role}
            onChange={(v) => setFilter('role', v)}
            emptyLabel={t('users.list.allRoles')}
            options={(roleOptions.data?.all ?? []).map((r) => ({ value: r, label: r }))}
            className="sm:w-40"
          />
          <OptionSelect
            ariaLabel={t('users.list.filterByStatus')}
            value={status}
            onChange={(v) => setFilter('status', v)}
            emptyLabel={t('users.list.allStatuses')}
            options={statusOptions}
            className="sm:w-40"
          />
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={users.data?.items}
        getRowId={(u) => u.id}
        rowLabel={(u) => u.email}
        isLoading={users.isPending}
        onRowClick={(u) => navigate(`/admin/users/${u.id}`)}
        empty={
          <TableEmpty
            icon={Users}
            title={filtered ? t('users.list.empty.filteredTitle') : t('users.list.empty.title')}
            description={filtered ? t('users.list.empty.filteredDescription') : t('users.list.empty.description')}
          />
        }
      />

      {users.data && (
        <TablePagination
          page={users.data.page}
          pageSize={users.data.pageSize}
          total={users.data.total}
          onPageChange={(p) => setFilter('page', String(p))}
        />
      )}

      <CreateUserDialog open={createOpen} onOpenChange={setCreateOpen} />

      <ConfirmDialog
        open={!!pending}
        onOpenChange={(open) => !open && setPending(null)}
        title={pending?.action === 'promote' ? t('users.list.promoteConfirm.title') : t('users.list.deleteConfirm.title')}
        description={
          <Trans
            t={t}
            i18nKey={pending?.action === 'promote' ? 'users.list.promoteConfirm.description' : 'users.list.deleteConfirm.description'}
            values={{ email: pending?.user.email }}
            components={{ strong: <strong /> }}
          />
        }
        confirmLabel={pending?.action === 'promote' ? t('users.list.promoteConfirm.confirm') : t('users.list.deleteUser')}
        destructive={pending?.action === 'delete'}
        onConfirm={runPending}
      />
    </>
  )
}
