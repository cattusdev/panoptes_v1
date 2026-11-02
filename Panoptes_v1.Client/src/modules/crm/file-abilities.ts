import { hasPermission, useSession } from '@/lib/auth'
import { Permissions } from '@/lib/permissions'
import type { Client } from './api'

// Ποιος μπορεί τι στην καρτέλα Files: βλέπει με Media.View (ίδιος κανόνας με το κατέβασμα), συνδέει / αποσυνδέει
// με Clients.Edit, ανεβάζει με Media.Upload, διαλέγει από τη βιβλιοθήκη με Media.View
export function useClientFileAbilities(client: Client) {
  const { user } = useSession()
  const canView = hasPermission(user, Permissions.Media.View)
  return {
    canView,
    canUpload: canView && client.canEdit && hasPermission(user, Permissions.Media.Upload),
    canAttach: canView && client.canEdit,
    canUnlink: client.canEdit,
  }
}
