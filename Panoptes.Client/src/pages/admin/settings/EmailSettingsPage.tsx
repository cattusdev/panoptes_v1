import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { Send } from 'lucide-react'
import { SettingsCard } from '@/components/admin/Common'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { PasswordInput } from '@/components/form/PasswordInput'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel, FieldLegend, FieldSet } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Spinner } from '@/components/ui/spinner'
import { Switch } from '@/components/ui/switch'
import { fieldErrors, problemMessage } from '@/lib/api'
import { hasPermission, useSession } from '@/lib/auth'
import { Permissions } from '@/lib/permissions'
import {
  useEmailSettings,
  useSendTestEmail,
  useUpdateEmailSettings,
  type EmailSettings,
  type EmailSettingsInput,
  type EmailTransportSource,
  type SmtpSecurity,
} from '@/lib/settings-api'
import { ReadOnlyNotice } from './ReadOnlyNotice'

const securityOptions: SmtpSecurity[] = ['StartTls', 'SslOnConnect', 'None', 'Auto']

const statusVariant: Record<EmailTransportSource, 'success' | 'info' | 'muted' | 'warning' | 'destructive'> = {
  Custom: 'success',
  Configuration: 'info',
  Pickup: 'muted',
  DevelopmentFolder: 'warning',
  NotConfigured: 'destructive',
}

// SMTP από το UI (όπως Supabase Auth → SMTP, Ghost): διακόπτης "δικός μας server", password μόνο για εγγραφή, δοκιμαστικό email
export function EmailSettingsPage() {
  const settings = useEmailSettings()
  if (settings.isPending) return <Skeleton className="h-96 rounded-[14px]" />
  if (settings.isError) return <p className="text-sm text-destructive">{problemMessage(settings.error)}</p>
  return <EmailForm key={JSON.stringify(settings.data)} settings={settings.data} />
}

function EmailForm({ settings }: { settings: EmailSettings }) {
  const { t } = useTranslation('settings')
  const { user } = useSession()
  const update = useUpdateEmailSettings()
  const sendTest = useSendTestEmail()

  const [enabled, setEnabled] = useState(settings.enabled)
  const [host, setHost] = useState(settings.host)
  const [port, setPort] = useState(String(settings.port))
  const [security, setSecurity] = useState<SmtpSecurity>(settings.security)
  const [username, setUsername] = useState(settings.username)
  const [password, setPassword] = useState('')
  const [clearPassword, setClearPassword] = useState(false)
  const [senderName, setSenderName] = useState(settings.senderName)
  const [senderEmail, setSenderEmail] = useState(settings.senderEmail)
  const [testTo, setTestTo] = useState(user?.email ?? '')
  const [testError, setTestError] = useState<string | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})

  const canEdit = hasPermission(user, Permissions.Settings.Edit)
  const dirty =
    enabled !== settings.enabled ||
    host !== settings.host ||
    port !== String(settings.port) ||
    security !== settings.security ||
    username !== settings.username ||
    senderName !== settings.senderName ||
    senderEmail !== settings.senderEmail ||
    password !== '' ||
    clearPassword
  const incomplete = enabled && (!host.trim() || !senderEmail.trim() || !port)
  const source = settings.status.source
  // Για Custom / Configuration ο server δίνει "host:port", για τα υπόλοιπα το κείμενο είναι εδώ
  const statusDetail = source === 'Custom' || source === 'Configuration' ? settings.status.detail : t(`email.statusDetail.${source}`)

  function input(): EmailSettingsInput {
    return {
      enabled,
      host,
      port: Number(port),
      security,
      username,
      senderName,
      senderEmail,
      password: password || null,
      clearPassword,
    }
  }

  // Το port ακολουθεί την ασφάλεια όταν είναι το "συνηθισμένο" της άλλης επιλογής
  function changeSecurity(value: string) {
    const next = value as SmtpSecurity
    setSecurity(next)
    if (next === 'SslOnConnect' && port === '587') setPort('465')
    if (next === 'StartTls' && port === '465') setPort('587')
  }

  function showErrors(error: unknown) {
    const byField = fieldErrors(error)
    if (Object.keys(byField).length > 0) {
      setErrors(byField)
      return true
    }
    return false
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    setErrors({})
    try {
      await update.mutateAsync(input())
      toast.success(t('email.smtp.saved'), { description: enabled ? t('email.smtp.savedOn') : t('email.smtp.savedOff') })
    } catch (error) {
      if (!showErrors(error)) toast.error(problemMessage(error))
    }
  }

  async function test() {
    setErrors({})
    setTestError(null)
    try {
      const result = await sendTest.mutateAsync({ ...input(), to: testTo })
      toast.success(t('email.test.sent', { to: result.to }), { description: t('email.test.sentDescription') })
    } catch (error) {
      if (!showErrors(error)) setTestError(problemMessage(error))
    }
  }

  const fieldsDisabled = !canEdit

  return (
    <div className="grid gap-4">
      {!canEdit && <ReadOnlyNotice />}

      <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 rounded-[14px] border bg-card px-5.5 py-4 text-sm" data-testid="email-status">
        <span className="text-muted-foreground">{t('email.sentThrough')}</span>
        <Badge variant={statusVariant[source]} dot>
          {t(`email.status.${source}`)}
        </Badge>
        <span className="min-w-0 truncate text-muted-foreground">{statusDetail}</span>
      </div>

      <form onSubmit={save}>
        <SettingsCard
          title={t('email.smtp.title')}
          description={t('email.smtp.description')}
          footer={
            canEdit && (
              <>
                {incomplete && <span className="mr-auto">{t('email.smtp.incomplete')}</span>}
                <Button type="submit" disabled={!dirty || update.isPending || incomplete}>
                  {update.isPending && <Spinner />}
                  {t('actions.saveChanges', { ns: 'common' })}
                </Button>
              </>
            )
          }
        >
          <FieldGroup>
            <Field orientation="horizontal" className="items-start justify-between rounded-lg border p-3.5">
              <div className="space-y-1">
                <FieldLabel htmlFor="smtp-enabled">{t('email.smtp.enabled')}</FieldLabel>
                <FieldDescription>{t('email.smtp.enabledHint')}</FieldDescription>
              </div>
              <Switch id="smtp-enabled" checked={enabled} onCheckedChange={setEnabled} disabled={fieldsDisabled} />
            </Field>

            {enabled && (
              <>
                <FieldSet>
                  <FieldLegend variant="label">{t('email.smtp.sender')}</FieldLegend>
                  <FieldDescription>{t('email.smtp.senderHint')}</FieldDescription>
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Field data-invalid={!!errors.senderEmail || undefined}>
                      <FieldLabel htmlFor="smtp-sender-email">{t('email.smtp.fromEmail')}</FieldLabel>
                      <Input
                        id="smtp-sender-email"
                        type="email"
                        placeholder="no-reply@example.com"
                        value={senderEmail}
                        onChange={(e) => setSenderEmail(e.target.value)}
                        disabled={fieldsDisabled}
                        aria-invalid={!!errors.senderEmail || undefined}
                      />
                      <FieldError>{errors.senderEmail}</FieldError>
                    </Field>
                    <Field data-invalid={!!errors.senderName || undefined}>
                      <FieldLabel htmlFor="smtp-sender-name">{t('email.smtp.fromName')}</FieldLabel>
                      <Input
                        id="smtp-sender-name"
                        placeholder={t('email.smtp.fromNamePlaceholder')}
                        maxLength={100}
                        value={senderName}
                        onChange={(e) => setSenderName(e.target.value)}
                        disabled={fieldsDisabled}
                        aria-invalid={!!errors.senderName || undefined}
                      />
                      <FieldError>{errors.senderName}</FieldError>
                    </Field>
                  </div>
                </FieldSet>

                <FieldSet>
                  <FieldLegend variant="label">{t('email.smtp.server')}</FieldLegend>
                  <FieldDescription>{t('email.smtp.serverHint')}</FieldDescription>
                  <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_120px]">
                    <Field data-invalid={!!errors.host || undefined}>
                      <FieldLabel htmlFor="smtp-host">{t('email.smtp.host')}</FieldLabel>
                      <Input
                        id="smtp-host"
                        placeholder="smtp.example.com"
                        value={host}
                        onChange={(e) => setHost(e.target.value)}
                        disabled={fieldsDisabled}
                        aria-invalid={!!errors.host || undefined}
                      />
                      <FieldError>{errors.host}</FieldError>
                    </Field>
                    <Field data-invalid={!!errors.port || undefined}>
                      <FieldLabel htmlFor="smtp-port">{t('email.smtp.port')}</FieldLabel>
                      <Input
                        id="smtp-port"
                        type="number"
                        inputMode="numeric"
                        min={1}
                        max={65535}
                        value={port}
                        onChange={(e) => setPort(e.target.value)}
                        disabled={fieldsDisabled}
                        aria-invalid={!!errors.port || undefined}
                      />
                      <FieldError>{errors.port}</FieldError>
                    </Field>
                  </div>
                  <Field>
                    <FieldLabel htmlFor="smtp-security">{t('email.smtp.security')}</FieldLabel>
                    <OptionSelect
                      id="smtp-security"
                      value={security}
                      onChange={changeSecurity}
                      options={securityOptions.map((value) => ({ value, label: t(`email.securityOptions.${value}`) }))}
                      disabled={fieldsDisabled}
                    />
                  </Field>
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Field data-invalid={!!errors.username || undefined}>
                      <FieldLabel htmlFor="smtp-username">{t('email.smtp.username')}</FieldLabel>
                      <Input
                        id="smtp-username"
                        autoComplete="off"
                        value={username}
                        onChange={(e) => setUsername(e.target.value)}
                        disabled={fieldsDisabled}
                        aria-invalid={!!errors.username || undefined}
                      />
                      <FieldDescription>{t('email.smtp.usernameHint')}</FieldDescription>
                      <FieldError>{errors.username}</FieldError>
                    </Field>
                    <Field data-invalid={!!errors.password || undefined}>
                      <FieldLabel htmlFor="smtp-password">{t('email.smtp.password')}</FieldLabel>
                      <PasswordInput
                        id="smtp-password"
                        autoComplete="new-password"
                        placeholder={settings.hasPassword && !clearPassword ? '••••••••' : ''}
                        value={password}
                        onChange={(e) => {
                          setPassword(e.target.value)
                          if (e.target.value) setClearPassword(false)
                        }}
                        disabled={fieldsDisabled}
                        aria-invalid={!!errors.password || undefined}
                      />
                      <PasswordHint
                        hasPassword={settings.hasPassword}
                        clearPassword={clearPassword}
                        canEdit={canEdit}
                        onClear={() => {
                          setPassword('')
                          setClearPassword(true)
                        }}
                        onUndo={() => setClearPassword(false)}
                      />
                      <FieldError>{errors.password}</FieldError>
                    </Field>
                  </div>
                </FieldSet>
              </>
            )}
          </FieldGroup>
        </SettingsCard>
      </form>

      {enabled && canEdit && (
        <SettingsCard
          title={t('email.test.title')}
          description={t('email.test.description')}
          footer={
            <Button type="button" variant="outline" onClick={() => void test()} disabled={sendTest.isPending || incomplete || !testTo.trim()}>
              {sendTest.isPending ? <Spinner /> : <Send />}
              {t('email.test.send')}
            </Button>
          }
        >
          <div className="grid gap-4">
            <Field data-invalid={!!errors.to || undefined}>
              <FieldLabel htmlFor="test-email-to">{t('email.test.to')}</FieldLabel>
              <Input
                id="test-email-to"
                type="email"
                value={testTo}
                onChange={(e) => setTestTo(e.target.value)}
                className="sm:max-w-sm"
                aria-invalid={!!errors.to || undefined}
              />
              <FieldError>{errors.to}</FieldError>
            </Field>
            {testError && (
              <Alert variant="destructive">
                <AlertTitle>{t('email.test.failed')}</AlertTitle>
                <AlertDescription>{testError}</AlertDescription>
              </Alert>
            )}
          </div>
        </SettingsCard>
      )}
    </div>
  )
}

function PasswordHint({
  hasPassword,
  clearPassword,
  canEdit,
  onClear,
  onUndo,
}: {
  hasPassword: boolean
  clearPassword: boolean
  canEdit: boolean
  onClear: () => void
  onUndo: () => void
}) {
  const { t } = useTranslation('settings')
  if (!hasPassword) return null
  if (clearPassword)
    return (
      <FieldDescription>
        {t('email.smtp.passwordRemoved')}{' '}
        <button type="button" onClick={onUndo} className="font-medium text-foreground underline-offset-4 hover:underline">
          {t('actions.undo', { ns: 'common' })}
        </button>
      </FieldDescription>
    )
  return (
    <FieldDescription>
      {t('email.smtp.passwordSaved')}
      {canEdit && (
        <>
          {' '}
          <button type="button" onClick={onClear} className="font-medium text-foreground underline-offset-4 hover:underline">
            {t('email.smtp.removePassword')}
          </button>
        </>
      )}
    </FieldDescription>
  )
}
