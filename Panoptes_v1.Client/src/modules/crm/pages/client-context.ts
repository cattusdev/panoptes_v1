import { useOutletContext } from 'react-router'
import type { Client, ClientContact } from '../api'

// Ό,τι δίνει το ClientLayout στις καρτέλες του (Outlet context)
export type ClientOutletContext = {
  client: Client
  contacts: ClientContact[] | undefined
  openAddContact: () => void
  openUpload: () => void
}

export function useClientContext() {
  return useOutletContext<ClientOutletContext>()
}
