import { useEffect } from 'react'

// Οι auth/public σελίδες είναι φωτεινές, το admin console σκούρο (class "dark" στο <html>)
export function useThemeMode(mode: 'light' | 'dark') {
  useEffect(() => {
    document.documentElement.classList.toggle('dark', mode === 'dark')
    document.documentElement.style.colorScheme = mode
  }, [mode])
}
