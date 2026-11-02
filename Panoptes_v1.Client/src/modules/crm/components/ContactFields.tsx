import { useTranslation } from 'react-i18next'
import { Controller, useFieldArray, type ArrayPath, type Control, type FieldArray, type FieldValues, type Path } from 'react-hook-form'
import { Plus, Trash2 } from 'lucide-react'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { Button } from '@/components/ui/button'
import { FieldError, FieldLegend, FieldSet } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { addressKinds, contactMethodKinds } from '../api'
import type { AddressValues, ContactMethodValues } from '../forms'

// Τα κομμάτια των φορμών πελάτη και προσώπου: τρόποι επικοινωνίας και διευθύνσεις (λίστες με "Add" / αφαίρεση).
// Τα schemas και οι μετατροπές τους: ../forms.ts

type WithContactMethods = { contactMethods: ContactMethodValues[] }
type WithAddresses = { addresses: AddressValues[] }

// Email / τηλέφωνα / site: είδος, τιμή, προαιρετική ετικέτα ("Λογιστήριο")
export function ContactMethodsField<T extends FieldValues & WithContactMethods>({ control, idPrefix }: { control: Control<T>; idPrefix: string }) {
  const { t } = useTranslation('crm')
  const { fields, append, remove } = useFieldArray({ control, name: 'contactMethods' as ArrayPath<T> })
  const kindOptions = contactMethodKinds.map((kind) => ({ value: kind, label: t(`contactKinds.${kind}`) }))

  return (
    <FieldSet>
      <FieldLegend variant="label">{t('form.contactMethods')}</FieldLegend>
      <div className="flex flex-col gap-2">
        {fields.map((field, index) => (
          <div key={field.id} className="flex flex-col gap-1">
            <div className="flex gap-2">
              <Controller
                control={control}
                name={`contactMethods.${index}.kind` as Path<T>}
                render={({ field: kind }) => (
                  <OptionSelect
                    ariaLabel={t('form.contactKind')}
                    value={kind.value as string}
                    onChange={kind.onChange}
                    options={kindOptions}
                    className="w-32 shrink-0"
                  />
                )}
              />
              <Controller
                control={control}
                name={`contactMethods.${index}.value` as Path<T>}
                render={({ field: value, fieldState }) => (
                  <Input
                    {...value}
                    id={`${idPrefix}-contact-${index}`}
                    aria-label={t('form.contactValue')}
                    aria-invalid={fieldState.invalid || undefined}
                    placeholder={t('form.contactValue')}
                    className="min-w-0 flex-1"
                  />
                )}
              />
              <Controller
                control={control}
                name={`contactMethods.${index}.label` as Path<T>}
                render={({ field: label }) => (
                  <Input {...label} aria-label={t('form.contactLabel')} placeholder={t('form.contactLabel')} className="hidden w-32 sm:block" />
                )}
              />
              <Button type="button" variant="ghost" size="icon" aria-label={t('form.removeContact')} onClick={() => remove(index)}>
                <Trash2 />
              </Button>
            </div>
            <Controller
              control={control}
              name={`contactMethods.${index}.value` as Path<T>}
              render={({ fieldState }) => (fieldState.error?.message ? <FieldError>{fieldState.error.message}</FieldError> : <></>)}
            />
          </div>
        ))}
      </div>
      <div className="flex flex-wrap gap-2">
        {(['Email', 'Phone'] as const).map((kind) => (
          <Button
            key={kind}
            type="button"
            variant="outline"
            size="sm"
            onClick={() => append({ kind, value: '', label: '' } as FieldArray<T, ArrayPath<T>>)}
          >
            <Plus /> {t(`form.add${kind}`)}
          </Button>
        ))}
      </div>
    </FieldSet>
  )
}

// Διευθύνσεις: η τιμολόγησης πρώτη (το κάνει ο server)
export function AddressesField<T extends FieldValues & WithAddresses>({ control, idPrefix }: { control: Control<T>; idPrefix: string }) {
  const { t } = useTranslation('crm')
  const { fields, append, remove } = useFieldArray({ control, name: 'addresses' as ArrayPath<T> })
  const kindOptions = addressKinds.map((kind) => ({ value: kind, label: t(`addressKinds.${kind}`) }))
  const text = (index: number, key: Exclude<keyof AddressValues, 'kind'>, className?: string) => (
    <Controller
      control={control}
      name={`addresses.${index}.${key}` as Path<T>}
      render={({ field, fieldState }) => (
        <div className={className}>
          <Input
            {...field}
            id={`${idPrefix}-address-${index}-${key}`}
            aria-label={t(`address.${key}`)}
            aria-invalid={fieldState.invalid || undefined}
            placeholder={t(`address.${key}`)}
          />
          {fieldState.error?.message && <FieldError className="mt-1">{fieldState.error.message}</FieldError>}
        </div>
      )}
    />
  )

  return (
    <FieldSet>
      <FieldLegend variant="label">{t('form.addresses')}</FieldLegend>
      <div className="flex flex-col gap-4">
        {fields.map((field, index) => (
          <div key={field.id} className="grid grid-cols-6 gap-2 rounded-lg border p-3">
            <div className="col-span-6 flex items-center justify-between gap-2">
              <Controller
                control={control}
                name={`addresses.${index}.kind` as Path<T>}
                render={({ field: kind }) => (
                  <OptionSelect ariaLabel={t('form.addressKind')} value={kind.value as string} onChange={kind.onChange} options={kindOptions} className="w-40" />
                )}
              />
              <Button type="button" variant="ghost" size="icon" aria-label={t('form.removeAddress')} onClick={() => remove(index)}>
                <Trash2 />
              </Button>
            </div>
            {text(index, 'line1', 'col-span-6')}
            {text(index, 'line2', 'col-span-6')}
            {text(index, 'city', 'col-span-6 sm:col-span-3')}
            {text(index, 'postalCode', 'col-span-3 sm:col-span-1')}
            {text(index, 'region', 'col-span-3 sm:col-span-2')}
            {text(index, 'country', 'col-span-6')}
          </div>
        ))}
      </div>
      <div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() =>
            append({
              kind: fields.length === 0 ? 'Billing' : 'Office',
              line1: '',
              line2: '',
              city: '',
              postalCode: '',
              region: '',
              country: '',
            } as FieldArray<T, ArrayPath<T>>)
          }
        >
          <Plus /> {t('form.addAddress')}
        </Button>
      </div>
    </FieldSet>
  )
}
