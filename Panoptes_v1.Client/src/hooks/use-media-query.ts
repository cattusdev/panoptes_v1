import { useSyncExternalStore } from 'react'

// true όσο ισχύει το media query (π.χ. '(min-width: 1280px)'), για layouts που αλλάζουν θέση και όχι μόνο μέγεθος
export function useMediaQuery(query: string) {
  return useSyncExternalStore(
    (onChange) => {
      const list = window.matchMedia(query)
      list.addEventListener('change', onChange)
      return () => list.removeEventListener('change', onChange)
    },
    () => window.matchMedia(query).matches,
    () => false,
  )
}
