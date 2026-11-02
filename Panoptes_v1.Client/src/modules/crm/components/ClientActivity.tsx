import { Trans, useTranslation } from 'react-i18next'
import { ActivityTimeline } from '@/components/admin/ActivityTimeline'
import type { ActivityItem } from '../api'

const knownFields = [
  'kind',
  'displayName',
  'legalName',
  'taxId',
  'taxOffice',
  'status',
  'defaultCurrency',
  'tags',
  'addresses',
  'contactMethods',
  'notes',
  'role',
  'isPrimary',
  'isBillingContact',
] as const

type KnownField = (typeof knownFields)[number]

const verbs = [
  'client.created',
  'client.updated',
  'client.archived',
  'client.unarchived',
  'client.duplicateConfirmed',
  'client.contactAdded',
  'client.contactUpdated',
  'client.contactRemoved',
] as const

type Verb = (typeof verbs)[number]

// Το κείμενο κάθε γραμμής του ιστορικού του πελάτη (οι τιμές έρχονται από το ActivityEvent.Data)
function useDescribe() {
  const { t } = useTranslation('crm')

  return (item: ActivityItem, actor: string) => {
    const data = item.data as {
      ref?: string
      person?: string
      role?: string | null
      reason?: string
      fields?: string[]
      matches?: { ref: string; name: string }[]
    }
    const fields = (data.fields ?? [])
      .map((field) => (knownFields.includes(field as KnownField) ? t(`activity.fields.${field as KnownField}`) : field))
      .join(', ')
    const values = {
      actor,
      ref: data.ref ?? '',
      person: data.person ?? '',
      role: data.role ?? '',
      reason: data.reason ?? '',
      fields,
      matches: (data.matches ?? []).map((m) => `${m.name} (${m.ref})`).join(', '),
    }
    if (!verbs.includes(item.verb as Verb)) return <Trans t={t} i18nKey="activity.verbs.unknown" values={{ actor }} components={{ strong: <strong /> }} />
    const verb = item.verb.replace('client.', '') as Verb extends `client.${infer V}` ? V : never
    const key = verb === 'contactAdded' && data.role ? 'contactAddedWithRole' : verb
    return <Trans t={t} i18nKey={`activity.verbs.${key}`} values={values} components={{ strong: <strong /> }} />
  }
}

export function ClientActivityList({ items, isLoading }: { items: ActivityItem[] | undefined; isLoading?: boolean }) {
  const { t } = useTranslation('crm')
  const describe = useDescribe()
  return (
    <ActivityTimeline
      items={items}
      isLoading={isLoading}
      describe={describe}
      emptyTitle={t('activity.empty.title')}
      emptyDescription={t('activity.empty.description')}
    />
  )
}
