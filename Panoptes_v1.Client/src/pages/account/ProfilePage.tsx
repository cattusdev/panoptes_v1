import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { MailWarning } from 'lucide-react'
import { PhoneInput } from '@/components/form/PhoneInput'
import { SettingsCard } from '@/components/admin/Common'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { Alert, AlertAction, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldContent, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { useCooldown } from '@/hooks/use-cooldown'
import { defaultLanguage, languages, resolveLanguage, setLanguage } from '@/i18n'
import { useAccount, useChangeEmail, useSendVerificationEmail, useUpdateProfile, type Account } from '@/lib/account-api'
import { fieldErrors, problemMessage } from '@/lib/api'
import { useSession, useUpdateLanguage } from '@/lib/auth'
import { isValidPhone } from '@/lib/phone'
import { useGeneralSettings } from '@/lib/settings-api'
import { ProfilePhotoCard } from './ProfilePhotoCard'

export function ProfilePage() {
  const account = useAccount()
  if (account.isPending) return <Skeleton className="h-72 rounded-[14px]" />
  if (account.isError) return <p className="text-sm text-destructive">{problemMessage(account.error)}</p>
  return <Profile key={account.data.phoneNumber ?? ''} account={account.data} />
}

function Profile({ account }: { account: Account }) {
  const { t } = useTranslation('account')
  const updateProfile = useUpdateProfile()
  const sendVerification = useSendVerificationEmail()
  const cooldown = useCooldown(30)
  const [phone, setPhone] = useState(account.phoneNumber ?? '')
  const [phoneError, setPhoneError] = useState<string | null>(null)
  const [changeOpen, setChangeOpen] = useState(false)
  const dirty = phone !== (account.phoneNumber ?? '')

  async function saveProfile(e: FormEvent) {
    e.preventDefault()
    setPhoneError(null)
    // Ίδιος έλεγχος με τον server (libphonenumber): κενό = χωρίς τηλέφωνο
    if (phone && !isValidPhone(phone)) {
      setPhoneError(t('validation.phone', { ns: 'common' }))
      return
    }
    try {
      await updateProfile.mutateAsync(phone)
      toast.success(t('profile.details.saved'))
    } catch (error) {
      setPhoneError(fieldErrors(error).phoneNumber ?? problemMessage(error))
    }
  }

  async function resendVerification() {
    try {
      await sendVerification.mutateAsync()
      cooldown.start()
      toast.success(t('profile.verify.sent', { email: account.email }))
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  return (
    <div className="grid gap-4">
      {!account.emailConfirmed && (
        <Alert variant="warning">
          <MailWarning />
          <AlertTitle>{t('profile.verify.title')}</AlertTitle>
          <AlertDescription>{t('profile.verify.description')}</AlertDescription>
          <AlertAction>
            <Button size="sm" variant="outline" onClick={resendVerification} disabled={cooldown.active || sendVerification.isPending}>
              {cooldown.active ? t('profile.verify.resendIn', { seconds: cooldown.remaining }) : t('profile.verify.send')}
            </Button>
          </AlertAction>
        </Alert>
      )}

      <ProfilePhotoCard />

      <SettingsCard title={t('profile.email.title')} description={t('profile.email.description')}>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <span className="font-medium" data-testid="account-email">
              {account.email}
            </span>
            {account.emailConfirmed ? <Badge variant="success">{t('profile.email.verified')}</Badge> : <Badge variant="warning">{t('profile.email.unverified')}</Badge>}
          </div>
          <Button variant="outline" onClick={() => setChangeOpen(true)}>
            {t('profile.email.change')}
          </Button>
        </div>
      </SettingsCard>

      <SettingsCard title={t('profile.details.title')} description={t('profile.details.description')}>
        <form onSubmit={saveProfile}>
          <Field data-invalid={!!phoneError || undefined}>
            <FieldLabel htmlFor="phone">{t('profile.details.phone')}</FieldLabel>
            <PhoneInput
              id="phone"
              value={phone}
              onChange={(value) => {
                setPhone(value)
                setPhoneError(null)
              }}
              aria-invalid={!!phoneError || undefined}
              className="max-w-sm"
            />
            {phoneError ? <FieldError>{phoneError}</FieldError> : <FieldDescription>{t('profile.details.phoneHint')}</FieldDescription>}
          </Field>
          <Button type="submit" className="mt-5" disabled={!dirty || updateProfile.isPending}>
            {updateProfile.isPending && <Spinner />}
            {t('actions.saveChanges', { ns: 'common' })}
          </Button>
        </form>
      </SettingsCard>

      <LanguageCard />

      <ChangeEmailDialog open={changeOpen} onOpenChange={setChangeOpen} currentEmail={account.email} />
    </div>
  )
}

function ChangeEmailDialog({ open, onOpenChange, currentEmail }: { open: boolean; onOpenChange: (open: boolean) => void; currentEmail: string }) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <ChangeEmailForm currentEmail={currentEmail} onDone={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}

function ChangeEmailForm({ currentEmail, onDone }: { currentEmail: string; onDone: () => void }) {
  const { t } = useTranslation('account')
  const changeEmail = useChangeEmail()
  const [email, setEmail] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      await changeEmail.mutateAsync(email)
      toast.success(t('profile.changeEmail.sent'), { description: t('profile.changeEmail.sentDescription', { email }) })
      onDone()
    } catch (err) {
      setError(fieldErrors(err).newEmail ?? problemMessage(err))
    }
  }

  return (
    <form onSubmit={submit}>
      <DialogHeader className="mb-5">
        <DialogTitle>{t('profile.changeEmail.title')}</DialogTitle>
        <DialogDescription>{t('profile.changeEmail.description', { email: currentEmail })}</DialogDescription>
      </DialogHeader>
      <FieldGroup>
        <Field data-invalid={!!error || undefined}>
          <FieldLabel htmlFor="new-email">{t('profile.changeEmail.newEmail')}</FieldLabel>
          <Input
            id="new-email"
            type="email"
            required
            autoFocus
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-invalid={!!error || undefined}
          />
          <FieldError>{error}</FieldError>
        </Field>
      </FieldGroup>
      <DialogFooter className="mt-6">
        <Button type="button" variant="outline" onClick={onDone}>
          {t('actions.cancel', { ns: 'common' })}
        </Button>
        <Button type="submit" disabled={changeEmail.isPending || !email}>
          {changeEmail.isPending && <Spinner />}
          {t('profile.changeEmail.submit')}
        </Button>
      </DialogFooter>
    </form>
  )
}

// Η γλώσσα του χρήστη (όπως Linear / GitHub "Preferences → Language"): αποθηκεύεται αμέσως, χωρίς κουμπί
function LanguageCard() {
  const { t, i18n } = useTranslation('account')
  const { user } = useSession()
  const settings = useGeneralSettings()
  const updateLanguage = useUpdateLanguage()
  const appDefault = settings.data?.defaultLanguage ?? defaultLanguage
  const appDefaultName = languages.find((language) => language.code === appDefault)?.name ?? appDefault
  // Όσο αποθηκεύεται δείχνουμε ήδη τη νέα επιλογή
  const value = updateLanguage.isPending ? (updateLanguage.variables ?? '') : (user?.language ?? '')

  async function change(next: string) {
    const language = next || null
    if (language === (user?.language ?? null)) return
    try {
      await updateLanguage.mutateAsync(language)
      await setLanguage(resolveLanguage(language, appDefault))
      // Το t του hook είναι δεμένο στην παλιά γλώσσα: το μήνυμα βγαίνει στη νέα
      toast.success(i18n.t('language.saved', { ns: 'account' }))
    } catch (error) {
      toast.error(problemMessage(error))
    }
  }

  return (
    <SettingsCard title={t('language.title')} description={t('language.description')}>
      <FieldGroup>
        <Field orientation="responsive">
          <FieldContent>
            <FieldLabel htmlFor="account-language">{t('language.label')}</FieldLabel>
            <FieldDescription>{t('language.hint')}</FieldDescription>
          </FieldContent>
          <div className="@md/field-group:w-72! @md/field-group:shrink-0">
            <OptionSelect
              id="account-language"
              value={value}
              onChange={(next) => void change(next)}
              emptyLabel={t('language.appDefault', { language: appDefaultName })}
              options={languages.map((language) => ({ value: language.code, label: language.name }))}
              disabled={!user || updateLanguage.isPending}
            />
          </div>
        </Field>
      </FieldGroup>
    </SettingsCard>
  )
}
