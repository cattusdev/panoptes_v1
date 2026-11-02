import { useState } from 'react'
import type { ColumnVisibilityState } from '@tanstack/react-table'

// Ποιες στήλες φαίνονται: ανά πίνακα και ανά browser (localStorage), όπως στο GitHub/Linear
export function useColumnVisibility(tableId: string) {
  const storageKey = `panoptes-v1.columns.${tableId}`
  const [visibility, setVisibility] = useState<ColumnVisibilityState>(() => {
    try {
      return JSON.parse(localStorage.getItem(storageKey) ?? '{}') as ColumnVisibilityState
    } catch {
      return {}
    }
  })

  function toggle(key: string, visible: boolean) {
    setVisibility((current) => {
      const next = { ...current, [key]: visible }
      try {
        localStorage.setItem(storageKey, JSON.stringify(next))
      } catch {
        // private mode κ.λπ.: ισχύει μόνο για αυτή τη σελίδα
      }
      return next
    })
  }

  return { visibility, toggle }
}
