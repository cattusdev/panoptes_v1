import { z } from 'zod'
import i18n from '@/i18n'
import { schemas } from '@/lib/forms'
import { addressKinds, contactMethodKinds, type Address, type ContactMethod, type Person, type PersonInput } from './api'

// Τα schemas και οι μετατροπές των φορμών πελάτη και προσώπου. Στις φόρμες τα κενά πεδία είναι "" · στο API null.

export const contactMethodSchema = z.object({
  kind: z.enum(contactMethodKinds),
  value: z.string().trim().max(200),
  label: z.string().trim().max(50),
})

export const addressSchema = z.object({
  kind: z.enum(addressKinds),
  line1: z.string().trim().max(200),
  line2: z.string().trim().max(200),
  city: z.string().trim().max(100),
  postalCode: z.string().trim().max(20),
  region: z.string().trim().max(100),
  country: z.string().trim().max(100),
})

export type ContactMethodValues = z.input<typeof contactMethodSchema>
export type AddressValues = z.input<typeof addressSchema>

const emailCheck = z.email()

// Email γραμμές με έγκυρη διεύθυνση, διευθύνσεις με τουλάχιστον την πρώτη γραμμή (για το superRefine της φόρμας)
export function refineContactDetails(values: { contactMethods: ContactMethodValues[]; addresses?: AddressValues[] }, ctx: z.RefinementCtx) {
  values.contactMethods.forEach((method, index) => {
    if (method.kind === 'Email' && method.value.trim() && !emailCheck.safeParse(method.value.trim()).success)
      ctx.addIssue({ code: 'custom', path: ['contactMethods', index, 'value'], message: i18n.t('validation.email') })
  })
  values.addresses?.forEach((address, index) => {
    const filled = [address.line2, address.city, address.postalCode, address.region, address.country].some((v) => v.trim())
    if (filled && !address.line1.trim())
      ctx.addIssue({ code: 'custom', path: ['addresses', index, 'line1'], message: i18n.t('validation.required') })
  })
}

export function toContactMethods(values: ContactMethodValues[]): ContactMethod[] {
  return values.filter((m) => m.value.trim()).map((m) => ({ kind: m.kind, value: m.value.trim(), label: m.label.trim() || null }))
}

export function fromContactMethods(methods: ContactMethod[]): ContactMethodValues[] {
  return methods.map((m) => ({ kind: m.kind, value: m.value, label: m.label ?? '' }))
}

export function toAddresses(values: AddressValues[]): Address[] {
  return values
    .filter((a) => a.line1.trim())
    .map((a) => ({
      kind: a.kind,
      line1: a.line1.trim(),
      line2: a.line2.trim() || null,
      city: a.city.trim() || null,
      postalCode: a.postalCode.trim() || null,
      region: a.region.trim() || null,
      country: a.country.trim() || null,
    }))
}

export function fromAddresses(addresses: Address[]): AddressValues[] {
  return addresses.map((a) => ({
    kind: a.kind,
    line1: a.line1,
    line2: a.line2 ?? '',
    city: a.city ?? '',
    postalCode: a.postalCode ?? '',
    region: a.region ?? '',
    country: a.country ?? '',
  }))
}

// --- Πρόσωπο: κοινό στη φόρμα "New contact" και στο "Add contact" του πελάτη ---

export const personSchema = z
  .object({
    firstName: schemas.text().max(100),
    lastName: z.string().trim().max(100),
    contactMethods: z.array(contactMethodSchema),
    notes: z.string().trim().max(4000),
  })
  .superRefine(refineContactDetails)

export type PersonValues = z.input<typeof personSchema>

export const emptyPerson: PersonValues = {
  firstName: '',
  lastName: '',
  contactMethods: [
    { kind: 'Email', value: '', label: '' },
    { kind: 'Mobile', value: '', label: '' },
  ],
  notes: '',
}

export function personValues(person: Person): PersonValues {
  return {
    firstName: person.firstName,
    lastName: person.lastName ?? '',
    contactMethods: fromContactMethods(person.contactMethods),
    notes: person.notes ?? '',
  }
}

export function toPersonInput(values: PersonValues, version?: number): PersonInput {
  return {
    firstName: values.firstName.trim(),
    lastName: values.lastName.trim() || null,
    contactMethods: toContactMethods(values.contactMethods),
    notes: values.notes.trim() || null,
    version,
  }
}
