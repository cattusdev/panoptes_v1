import type { VariantProps } from 'class-variance-authority'
import type { badgeVariants } from '@/components/ui/badge'

// Οι σημασίες των status (docs/ux/README.md → Status badges). Κάθε module αντιστοιχίζει τα δικά του status
// σε μία από αυτές, ώστε το ίδιο χρώμα να σημαίνει παντού το ίδιο. Το κείμενο υπάρχει πάντα (όχι μόνο χρώμα).
export type StatusTone =
  | 'danger' // Θέλει ενέργεια / εκπρόθεσμο: Overdue, Failed, Declined
  | 'warning' // Αναμονή / σε κίνδυνο: Due soon, Pending approval, Partially paid
  | 'info' // Σε εξέλιξη / ανοιχτό: Open, In progress, Charged
  | 'success' // Ολοκληρώθηκε / εντάξει: Paid, Approved, Completed
  | 'neutral' // Ουδέτερο / ανενεργό: Draft, Archived, Not needed, Unknown
  | 'historical' // Ιστορικό / εισαγωγή (με ρολόι): Historical, Imported

type BadgeVariant = NonNullable<VariantProps<typeof badgeVariants>['variant']>

export const toneVariant = {
  danger: 'destructive',
  warning: 'warning',
  info: 'info',
  success: 'success',
  neutral: 'muted',
  historical: 'purple',
} as const satisfies Record<StatusTone, BadgeVariant>

// Το ίδιο χρώμα ως τελεία (λίστες δραστηριότητας)
export const toneDot: Record<StatusTone, string> = {
  danger: 'bg-destructive',
  warning: 'bg-warning',
  info: 'bg-info',
  success: 'bg-success',
  neutral: 'bg-muted-foreground',
  historical: 'bg-purple',
}

// Audit log: δημιουργία / αλλαγή / διαγραφή
export function auditTypeTone(type: string): StatusTone {
  if (type === 'Create') return 'success'
  if (type === 'Update') return 'info'
  if (type === 'Delete') return 'danger'
  return 'neutral'
}

// Χρήστης: κλειδωμένος, ανεπιβεβαίωτος, ενεργός
export function userStatus(user: { isLockedOut: boolean; emailConfirmed: boolean }) {
  if (user.isLockedOut) return { key: 'locked', tone: 'danger' } as const
  if (!user.emailConfirmed) return { key: 'unconfirmed', tone: 'warning' } as const
  return { key: 'active', tone: 'success' } as const
}

// Δουλειές παρασκηνίου: αποτυχία θέλει ενέργεια, η αναμονή είναι αναμενόμενη (όχι προειδοποίηση)
export function jobStatusTone(status: string): StatusTone {
  if (status === 'Failed') return 'danger'
  if (status === 'Running') return 'info'
  if (status === 'Succeeded') return 'success'
  return 'neutral'
}
