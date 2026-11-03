import { useTranslation } from 'react-i18next'
import { Globe, Mail, Phone, Smartphone } from 'lucide-react'
import { formatPhone, phoneHref } from '@/lib/phone'
import type { ContactMethod } from '../api'

const icons = { Email: Mail, Phone: Phone, Mobile: Smartphone, Website: Globe }

function display(method: ContactMethod) {
  return method.kind === 'Phone' || method.kind === 'Mobile' ? formatPhone(method.value) : method.value
}

function href(method: ContactMethod) {
  if (method.kind === 'Email') return `mailto:${method.value}`
  if (method.kind === 'Website') return /^https?:\/\//i.test(method.value) ? method.value : `https://${method.value}`
  return phoneHref(method.value)
}

// Click-to-call / mail / site (μεγάλοι στόχοι στο κινητό). Η ετικέτα ("Λογιστήριο") δίπλα.
export function ContactMethodLinks({ methods, className }: { methods: ContactMethod[]; className?: string }) {
  const { t } = useTranslation('crm')
  if (methods.length === 0) return null
  return (
    <ul className={className ?? 'flex flex-col gap-1'}>
      {methods.map((method, index) => {
        const Icon = icons[method.kind]
        return (
          <li key={`${method.kind}-${index}`} className="flex min-w-0 items-center gap-2 text-sm">
            <Icon className="size-3.5 shrink-0 text-muted-foreground" aria-hidden />
            <a
              href={href(method)}
              target={method.kind === 'Website' ? '_blank' : undefined}
              rel={method.kind === 'Website' ? 'noopener noreferrer' : undefined}
              className="truncate py-1 hover:underline md:py-0"
              aria-label={`${t(`contactKinds.${method.kind}`)}: ${display(method)}`}
            >
              {display(method)}
            </a>
            {method.label && <span className="truncate text-xs text-muted-foreground">{method.label}</span>}
          </li>
        )
      })}
    </ul>
  )
}
