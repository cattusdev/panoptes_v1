import { useEffect, useState } from 'react'

// Για κουμπιά "Resend": μετά από κάθε αποστολή περιμένεις λίγα δευτερόλεπτα
export function useCooldown(seconds = 30) {
  const [remaining, setRemaining] = useState(0)

  useEffect(() => {
    if (remaining <= 0) return
    const timer = setTimeout(() => setRemaining((r) => r - 1), 1000)
    return () => clearTimeout(timer)
  }, [remaining])

  return { remaining, active: remaining > 0, start: () => setRemaining(seconds) }
}
