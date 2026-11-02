import { useMemo, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { CircleAlert, Lock, Pencil, Shield, ShieldCheck } from 'lucide-react'
import { DangerZone, SettingsCard, UserAvatar } from '@/components/admin/Common'
import { ConfirmDialog } from '@/components/admin/ConfirmDialog'
import { PageHeader } from '@/components/admin/PageHeader'
import { IconTile } from '@/components/admin/StatCard'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Switch } from '@/components/ui/switch'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import adminEn from '@/i18n/locales/en/admin.json'
import { useDeleteRole, useRenameRole, useRole, useSetRolePermissions, type RoleDetail } from '@/lib/admin-api'
import { problemMessage } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import { Permissions } from '@/lib/permissions'
import { RoleNameDialog } from './RoleNameDialog'

// Το link "πίσω" στη λίστα ρόλων
function useBack() {
  const { t } = useTranslation()
  return { to: '/admin/roles', label: t('nav.roles') }
}

type Catalog = typeof adminEn.roles.catalog
type CatalogModule = keyof Catalog['modules']
type CatalogItem = { [M in keyof Catalog['items']]: `${M}.${keyof Catalog['items'][M] & string}` }[keyof Catalog['items']]

const isCatalogModule = (module: string): module is CatalogModule => Object.hasOwn(adminEn.roles.catalog.modules, module)

function isCatalogItem(key: string): key is CatalogItem {
  const [module, action] = key.split('.')
  const items: Record<string, Record<string, unknown>> = adminEn.roles.catalog.items
  return Object.hasOwn(items, module) && Object.hasOwn(items[module], action)
}

// "AuditLogs" -> "Audit Logs"
const splitWords = (module: string) => module.replace(/([a-z])([A-Z])/g, '$1 $2')

// Ονόματα και περιγραφές των permissions στη γλώσσα του χρήστη, με key το permission ("Users.View").
// Ό,τι δεν έχει μετάφραση (νέο module στο backend) βγαίνει από τα ονόματα που στέλνει ο server.
function usePermissionText() {
  const { t } = useTranslation('admin')
  return {
    module: (module: string) => (isCatalogModule(module) ? t(`roles.catalog.modules.${module}`) : splitWords(module)),
    permission: (module: string, permission: { name: string; action: string }) => {
      const key = permission.name.replace(/^Permissions\./, '')
      if (isCatalogItem(key)) {
        return { label: t(`roles.catalog.items.${key}.label`), description: t(`roles.catalog.items.${key}.description`) }
      }
      return { label: `${permission.action} ${splitWords(module).toLowerCase()}`, description: permission.name }
    },
  }
}

export function RoleDetailPage() {
  const { t } = useTranslation('admin')
  const back = useBack()
  const { id = '' } = useParams()
  const role = useRole(id)

  if (role.isPending) {
    return (
      <>
        <PageHeader title={<Skeleton className="h-8 w-48" />} back={back} />
        <Skeleton className="h-96 rounded-[14px]" />
      </>
    )
  }

  if (role.isError) {
    return (
      <>
        <PageHeader title={t('roles.detail.title')} back={back} />
        <Alert variant="error">
          <CircleAlert />
          <AlertTitle>{t('roles.detail.loadError')}</AlertTitle>
          <AlertDescription>{problemMessage(role.error)}</AlertDescription>
        </Alert>
      </>
    )
  }

  // key: όταν αλλάζουν τα αποθηκευμένα permissions, ξεκινάμε από αυτά
  return <RoleDetailView key={role.data.permissions.join('|')} role={role.data} />
}

function RoleDetailView({ role }: { role: RoleDetail }) {
  const { t } = useTranslation('admin')
  const back = useBack()
  const permissionText = usePermissionText()
  const navigate = useNavigate()
  const { user: me } = useSession()
  const setPermissions = useSetRolePermissions(role.id)
  const renameRole = useRenameRole(role.id)
  const deleteRole = useDeleteRole()

  const [selected, setSelected] = useState(() => new Set(role.permissions))
  const [renameOpen, setRenameOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)

  const canEdit = role.canManage && hasPermission(me, Permissions.Roles.Edit)
  const canDelete = role.canManage && hasPermission(me, Permissions.Roles.Delete)
  const grantable = useMemo(() => new Set(role.grantablePermissions), [role.grantablePermissions])
  const canSeeUsers = hasPermission(me, Permissions.Users.View)

  const dirty = selected.size !== role.permissions.length || role.permissions.some((p) => !selected.has(p))

  function toggle(permission: string, on: boolean) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (on) next.add(permission)
      else next.delete(permission)
      return next
    })
  }

  async function save() {
    try {
      await setPermissions.mutateAsync([...selected])
      toast.success(t('roles.detail.saved', { name: role.name }), { description: t('roles.detail.savedDescription') })
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  return (
    <div className={dirty ? 'pb-20' : undefined}>
      <PageHeader
        back={back}
        title={
          <span className="flex items-center gap-3">
            <IconTile icon={role.isSystem ? ShieldCheck : Shield} tint={role.isSystem ? 'purple' : 'blue'} />
            {role.name}
            {role.isSystem && <Badge variant="purple">{t('roles.detail.system')}</Badge>}
          </span>
        }
        description={t('roles.detail.members', { count: role.members.length })}
        actions={
          canEdit && (
            <Button variant="outline" onClick={() => setRenameOpen(true)}>
              <Pencil /> {t('roles.detail.rename')}
            </Button>
          )
        }
      />

      {!role.canManage && role.readOnlyReason && (
        <Alert variant="info" className="mb-5">
          <Lock />
          <AlertTitle>{t('roles.detail.readOnly')}</AlertTitle>
          <AlertDescription>{role.readOnlyReason}</AlertDescription>
        </Alert>
      )}

      <div className="grid gap-4 xl:grid-cols-[1fr_340px]">
        <SettingsCard title={t('roles.detail.permissionsTitle')} description={t('roles.detail.permissionsDescription')}>
          <div className="grid gap-6">
            {role.catalog.map((module) => (
              <section key={module.module}>
                <h3 className="mb-2 text-xs font-semibold tracking-[0.08em] text-muted-foreground uppercase">
                  {permissionText.module(module.module)}
                </h3>
                <div className="divide-y rounded-lg border">
                  {module.permissions.map((permission) => {
                    const { label, description } = permissionText.permission(module.module, permission)
                    const notGrantable = !grantable.has(permission.name)
                    const disabled = !canEdit || role.isSystem || (notGrantable && !selected.has(permission.name))
                    const toggleSwitch = (
                      <Switch
                        id={permission.name}
                        data-permission={permission.name}
                        aria-label={label}
                        checked={selected.has(permission.name)}
                        disabled={disabled}
                        onCheckedChange={(on) => toggle(permission.name, on)}
                      />
                    )
                    return (
                      <div key={permission.name} className="flex items-center justify-between gap-4 px-4 py-3">
                        <label htmlFor={permission.name} className="min-w-0">
                          <div className="text-sm font-medium first-letter:uppercase">{label}</div>
                          <div className="text-xs text-muted-foreground">{description}</div>
                        </label>
                        {canEdit && notGrantable && !role.isSystem ? (
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <span>{toggleSwitch}</span>
                            </TooltipTrigger>
                            <TooltipContent>{t('roles.detail.cantGrant')}</TooltipContent>
                          </Tooltip>
                        ) : (
                          toggleSwitch
                        )}
                      </div>
                    )
                  })}
                </div>
              </section>
            ))}
          </div>
        </SettingsCard>

        <SettingsCard title={t('roles.detail.membersTitle')} description={t('roles.detail.membersDescription')} className="self-start">
          {role.members.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('roles.detail.noMembers')}</p>
          ) : (
            <ul className="grid gap-2.5">
              {role.members.map((member) => (
                <li key={member.id} className="flex items-center gap-3 text-sm">
                  <UserAvatar email={member.email} avatarUrl={member.avatarUrl} active />
                  {canSeeUsers ? (
                    <Link to={`/admin/users/${member.id}`} className="truncate hover:underline">
                      {member.email}
                    </Link>
                  ) : (
                    <span className="truncate">{member.email}</span>
                  )}
                </li>
              ))}
            </ul>
          )}
        </SettingsCard>
      </div>

      {canDelete && (
        <div className="mt-4">
          <DangerZone
            title={t('roles.detail.delete.title')}
            description={t('roles.detail.delete.description')}
            action={
              <Button variant="destructive" onClick={() => setDeleteOpen(true)}>
                {t('roles.detail.delete.button')}
              </Button>
            }
          />
        </div>
      )}

      {/* "Unsaved changes" μπάρα (όπως τα settings στο GitHub / Linear) */}
      {dirty && (
        <div className="fixed inset-x-0 bottom-5 z-30 flex justify-center px-4">
          <div className="flex w-full max-w-xl items-center justify-between gap-3 rounded-xl border bg-popover px-4 py-3 shadow-2xl">
            <span className="text-sm">{t('roles.detail.unsaved')}</span>
            <div className="flex gap-2">
              <Button variant="ghost" onClick={() => setSelected(new Set(role.permissions))} disabled={setPermissions.isPending}>
                {t('roles.detail.reset')}
              </Button>
              <Button onClick={save} disabled={setPermissions.isPending}>
                {setPermissions.isPending && <Spinner />}
                {t('actions.saveChanges', { ns: 'common' })}
              </Button>
            </div>
          </div>
        </div>
      )}

      <RoleNameDialog
        open={renameOpen}
        onOpenChange={setRenameOpen}
        title={t('roles.detail.renameDialog.title')}
        description={t('roles.detail.renameDialog.description')}
        submitLabel={t('roles.detail.renameDialog.submit')}
        initialName={role.name}
        onSubmit={async (name) => {
          await renameRole.mutateAsync(name)
          toast.success(t('roles.detail.renameDialog.renamed', { name }))
        }}
      />

      <ConfirmDialog
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        title={t('roles.detail.delete.confirmTitle')}
        description={
          <Trans
            t={t}
            i18nKey="roles.detail.delete.confirmDescription"
            count={role.members.length}
            values={{ name: role.name }}
            components={{ strong: <strong /> }}
          />
        }
        confirmLabel={t('roles.detail.delete.button')}
        destructive
        onConfirm={async () => {
          try {
            await deleteRole.mutateAsync(role.id)
            toast.success(t('roles.detail.delete.deleted', { name: role.name }))
            navigate('/admin/roles', { replace: true })
          } catch (error) {
            toast.error(problemMessage(error))
            throw error
          }
        }}
      />
    </div>
  )
}
