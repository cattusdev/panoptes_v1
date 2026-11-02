import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Camera, Trash2 } from 'lucide-react'
import { SettingsCard, UserAvatar } from '@/components/admin/Common'
import { MediaUploader } from '@/components/media/LazyMediaUploader'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { useRemoveAvatar, useSetAvatar } from '@/lib/account-api'
import { problemMessage } from '@/lib/api'
import { useSession } from '@/lib/auth'

// Φωτογραφία προφίλ (όπως GitHub/Linear): τετράγωνο κόψιμο, ισχύει αμέσως μόλις ανέβει
export function ProfilePhotoCard() {
  const { t } = useTranslation('account')
  const { user } = useSession()
  const setAvatar = useSetAvatar()
  const removeAvatar = useRemoveAvatar()
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  if (!user) return null

  async function remove() {
    try {
      await removeAvatar.mutateAsync()
      toast.success(t('photo.removed'))
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  return (
    <SettingsCard title={t('photo.title')} description={t('photo.description')}>
      <div className="flex flex-wrap items-center gap-5">
        <UserAvatar email={user.email} avatarUrl={user.avatarUrl} size="xl" />
        <div className="space-y-2">
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => setOpen(true)}>
              <Camera /> {user.avatarUrl ? t('photo.change') : t('photo.upload')}
            </Button>
            {user.avatarUrl && (
              <Button variant="ghost" onClick={remove} disabled={removeAvatar.isPending}>
                <Trash2 /> {t('actions.remove', { ns: 'common' })}
              </Button>
            )}
          </div>
          <p className="text-xs text-muted-foreground">{t('photo.hint')}</p>
        </div>
      </div>

      <Dialog open={open} onOpenChange={(next) => (!next && busy ? undefined : setOpen(next))}>
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>{t('photo.title')}</DialogTitle>
            <DialogDescription>{t('photo.dialogDescription')}</DialogDescription>
          </DialogHeader>
          {open && (
            <MediaUploader
              target={{ purpose: 'Avatar' }}
              accept={['Image']}
              multiple={false}
              squareCrop={512}
              labelIdle={t('photo.drop')}
              className="mx-auto w-48"
              onBusyChange={setBusy}
              onUploaded={async (item) => {
                try {
                  await setAvatar.mutateAsync(item.id)
                  toast.success(t('photo.updated'))
                  setBusy(false)
                  setOpen(false)
                } catch (error) {
                  toast.error(problemMessage(error))
                }
              }}
            />
          )}
        </DialogContent>
      </Dialog>
    </SettingsCard>
  )
}
