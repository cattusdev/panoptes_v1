import type { ComponentType } from 'react'
import { Database, ShieldCheck, UserRound } from 'lucide-react'

type AccountSection = { id: 'profile' | 'security' | 'data'; to: string; end?: boolean; icon: ComponentType<{ className?: string }> }

// Οι ενότητες του My account (tabs και command palette). Νέα ενότητα = μια γραμμή εδώ + ένα route στο router.tsx
// + label στο account:layout.tabs.<id> (en και el).
export const accountSections: AccountSection[] = [
  { id: 'profile', to: '/account', end: true, icon: UserRound },
  { id: 'security', to: '/account/security', icon: ShieldCheck },
  { id: 'data', to: '/account/data', icon: Database },
]
