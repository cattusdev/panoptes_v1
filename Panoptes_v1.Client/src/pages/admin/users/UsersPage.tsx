import { useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { ChevronDown, Pencil, Plus, ShieldPlus, Trash2, Users } from 'lucide-react'
import { SearchInput, TableEmpty, UserAvatar, UserStatusBadge } from '@/components/admin/Common'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { ColumnsMenu, DataTable, TablePagination, type Column } from '@/components/admin/DataTable'
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
import { useColumnVisibility } from '@/hooks/use-column-visibility'
import { useUrlFilters } from '@/hooks/use-url-filters'
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
  const list = useUrlFilters(['search', 'role', 'status'], { defaultSort: 'email' })
  const { search, role, status } = list.filters
  const columnVisibility = useColumnVisibility('users')
  const [createOpen, setCreateOpen] = useState(false)
  const [pending, setPending] = useState<PendingAction>(null)

  const users = useUsers({ search, role, status, sort: list.sort, page: list.page, pageSize: list.pageSize })
  const roleOptions = useRoleOptions()
  const userAction = useUserAction()

  const canCreate = hasPermission(me, Permissions.Users.Create)
  const canDelete = hasPermission(me, Permissions.Users.Delete)
  const filtered = list.isFiltered
  const statusOptions = statuses.map((value) => ({ value, label: t(`status.${value}`) }))

  const columns: Column<UserListItem>[] = [
    {
      key: 'email',
      header: t('users.list.columns.user'),
      sortable: true,
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
      label: t('users.list.columns.role'),
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
    {
      key: 'status',
      header: t('users.list.columns.status'),
      label: t('users.list.columns.status'),
      cell: (u) => <UserStatusBadge isLockedOut={u.isLockedOut} emailConfirmed={u.emailConfirmed} />,
    },
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
        <SearchInput value={list.searchInput} onChange={list.setSearchInput} placeholder={t('users.list.search')} />
        <div className="flex gap-2 sm:ml-auto">
          <OptionSelect
            ariaLabel={t('users.list.filterByRole')}
            value={role}
            onChange={(v) => list.set('role', v)}
            emptyLabel={t('users.list.allRoles')}
            options={(roleOptions.data?.all ?? []).map((r) => ({ value: r, label: r }))}
            className="sm:w-40"
          />
          <OptionSelect
            ariaLabel={t('users.list.filterByStatus')}
            value={status}
            onChange={(v) => list.set('status', v)}
            emptyLabel={t('users.list.allStatuses')}
            options={statusOptions}
            className="sm:w-40"
          />
          <ColumnsMenu columns={columns} visibility={columnVisibility.visibility} onToggle={columnVisibility.toggle} />
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={users.data?.items}
        getRowId={(u) => u.id}
        rowLabel={(u) => u.email}
        isLoading={users.isPending}
        onRowClick={(u) => navigate(`/admin/users/${u.id}`)}
        sort={list.sort}
        onSortChange={list.setSort}
        columnVisibility={columnVisibility.visibility}
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
          onPageChange={(p) => list.set('page', String(p))}
          onPageSizeChange={(size) => list.set('pageSize', String(size))}
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
