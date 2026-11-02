import { useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { ChevronDown, Lock, Plus, Shield, ShieldCheck, ShieldEllipsis, Trash2 } from 'lucide-react'
import { TableEmpty } from '@/components/admin/Common'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { DataTable, type Column } from '@/components/admin/DataTable'
import { AccessTabs } from '@/components/admin/PageHeader'
import { IconTile } from '@/components/admin/StatCard'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useCreateRole, useDeleteRole, useRoles, type RoleListItem } from '@/lib/admin-api'
import { problemMessage } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import { Permissions } from '@/lib/permissions'
import { RoleNameDialog } from './RoleNameDialog'

const totalPermissions = Object.values(Permissions).flatMap((module) => Object.values(module)).length

export function RolesPage() {
  const { t } = useTranslation('admin')
  const navigate = useNavigate()
  const { user: me } = useSession()
  const roles = useRoles()
  const createRole = useCreateRole()
  const deleteRole = useDeleteRole()
  const [createOpen, setCreateOpen] = useState(false)
  const [toDelete, setToDelete] = useState<RoleListItem | null>(null)

  const canCreate = hasPermission(me, Permissions.Roles.Create)
  const canDelete = hasPermission(me, Permissions.Roles.Delete)

  const columns: Column<RoleListItem>[] = [
    {
      key: 'role',
      header: t('roles.list.columns.role'),
      cell: (r) => (
        <div className="flex items-center gap-3">
          <IconTile icon={r.isSystem ? ShieldCheck : Shield} tint={r.isSystem ? 'purple' : 'blue'} className="size-9 rounded-lg [&_svg]:size-4" />
          <span className="font-medium">{r.name}</span>
          {r.isSystem && <Badge variant="purple">{t('shared.system')}</Badge>}
        </div>
      ),
    },
    {
      key: 'permissions',
      header: t('roles.list.columns.permissions'),
      cell: (r) =>
        r.isSystem ? (
          <Badge variant="brand">{t('roles.list.allPermissions')}</Badge>
        ) : (
          <Badge variant={r.permissionCount > 0 ? 'success' : 'muted'}>
            {t('roles.list.permissionCount', { count: r.permissionCount, total: totalPermissions })}
          </Badge>
        ),
    },
    { key: 'members', header: t('roles.list.columns.members'), cell: (r) => r.userCount },
    {
      key: 'actions',
      header: <span className="sr-only">{t('shared.actions')}</span>,
      className: 'w-28 text-right',
      cell: (r) =>
        r.isSystem ? (
          <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
            <Lock className="size-3.5" /> {t('roles.list.protected')}
          </span>
        ) : (
          <DropdownMenu>
            <DropdownMenuTrigger asChild onClick={(e) => e.stopPropagation()}>
              <Button variant="ghost" size="sm" aria-label={t('shared.actionsFor', { name: r.name })} className="font-semibold">
                {t('roles.list.manage')} <ChevronDown />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" onClick={(e) => e.stopPropagation()}>
              <DropdownMenuItem onSelect={() => navigate(`/admin/roles/${r.id}`)}>
                <ShieldEllipsis /> {t('roles.list.permissions')}
              </DropdownMenuItem>
              {canDelete && r.canManage && (
                <>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem variant="destructive" onSelect={() => setToDelete(r)}>
                    <Trash2 /> {t('roles.list.deleteRole')}
                  </DropdownMenuItem>
                </>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        ),
    },
  ]

  return (
    <>
      <AccessTabs />

      <div className="mb-3 flex items-center justify-between gap-3">
        <span className="text-sm text-muted-foreground">{roles.data ? t('roles.list.totalCount', { count: roles.data.length }) : ' '}</span>
        {canCreate && (
          <Button onClick={() => setCreateOpen(true)}>
            <Plus /> {t('roles.list.newRole')}
          </Button>
        )}
      </div>

      <DataTable
        columns={columns}
        rows={roles.data}
        getRowId={(r) => r.id}
        rowLabel={(r) => r.name}
        isLoading={roles.isPending}
        onRowClick={(r) => navigate(`/admin/roles/${r.id}`)}
        empty={<TableEmpty icon={Shield} title={t('roles.list.empty.title')} description={t('roles.list.empty.description')} />}
      />

      <RoleNameDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        title={t('roles.list.create.title')}
        description={t('roles.list.create.description')}
        submitLabel={t('roles.list.create.submit')}
        onSubmit={async (name) => {
          const role = await createRole.mutateAsync(name)
          toast.success(t('roles.list.create.created', { name: role.name }))
          navigate(`/admin/roles/${role.id}`)
        }}
      />

      <ConfirmDialog
        open={!!toDelete}
        onOpenChange={(open) => !open && setToDelete(null)}
        title={t('roles.list.deleteConfirm.title')}
        description={
          <Trans
            t={t}
            i18nKey="roles.list.deleteConfirm.description"
            count={toDelete?.userCount ?? 0}
            values={{ name: toDelete?.name }}
            components={{ strong: <strong /> }}
          />
        }
        confirmLabel={t('roles.list.deleteConfirm.confirm')}
        destructive
        onConfirm={async () => {
          if (!toDelete) return
          try {
            await deleteRole.mutateAsync(toDelete.id)
            toast.success(t('roles.list.deleted', { name: toDelete.name }))
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />
    </>
  )
}
