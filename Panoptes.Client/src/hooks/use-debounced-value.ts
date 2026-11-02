import { useEffect, useState } from 'react'

// Για search πεδία: η τιμή "σταθεροποιείται" αφού ο χρήστης σταματήσει να γράφει
export function useDebouncedValue<T>(value: T, delayMs = 300) {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return debounced
}
