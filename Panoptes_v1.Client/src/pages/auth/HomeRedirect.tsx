import { Navigate } from 'react-router'
import { Loader2 } from 'lucide-react'
import { homePath, useSession } from '@/lib/auth'

// Το "/": δεν υπάρχει δημόσια αρχική. Συνδεδεμένος -> dashboard (ή ο λογαριασμός του), αλλιώς -> σύνδεση
export function HomeRedirect() {
  const { user, isPending } = useSession()

  if (isPending) {
    return (
      <div className="flex min-h-svh items-center justify-center text-muted-foreground">
        <Loader2 className="size-5 animate-spin" />
      </div>
    )
  }

  return <Navigate to={user ? homePath(user) : '/login'} replace />
}
