import i18n from '@/i18n'
import { usePasswordPolicy, type PasswordPolicy } from '@/lib/account-api'

// Οι κανόνες κωδικού του server ως λίστα ελέγχων (για το live checklist και τον έλεγχο πριν το submit)
export function passwordRules(policy: PasswordPolicy) {
  return [
    { label: i18n.t('password.minLength', { count: policy.requiredLength }), test: (p: string) => p.length >= policy.requiredLength, on: true },
    { label: i18n.t('password.uppercase'), test: (p: string) => /[A-Z]/.test(p), on: policy.requireUppercase },
    { label: i18n.t('password.lowercase'), test: (p: string) => /[a-z]/.test(p), on: policy.requireLowercase },
    { label: i18n.t('password.digit'), test: (p: string) => /\d/.test(p), on: policy.requireDigit },
    { label: i18n.t('password.symbol'), test: (p: string) => /[^a-zA-Z0-9]/.test(p), on: policy.requireNonAlphanumeric },
  ].filter((rule) => rule.on)
}

export function usePasswordValid(password: string) {
  const { data: policy } = usePasswordPolicy()
  return !policy || passwordRules(policy).every((rule) => rule.test(password))
}
