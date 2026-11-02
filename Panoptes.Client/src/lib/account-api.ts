import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/api'
import { sessionQueryKey, type LoginResult, type SessionUser } from '@/lib/auth'

export type PasswordPolicy = {
  requiredLength: number
  requireUppercase: boolean
  requireLowercase: boolean
  requireDigit: boolean
  requireNonAlphanumeric: boolean
}

export type Account = {
  id: string
  email: string
  emailConfirmed: boolean
  phoneNumber: string | null
  twoFactorEnabled: boolean
  hasAuthenticator: boolean
  recoveryCodesLeft: number
  isMachineRemembered: boolean
}

const post = <T = void>(path: string, body?: unknown) => api<T>(path, { method: 'POST', body })

// ---------- Χωρίς login ----------
export function usePasswordPolicy() {
  return useQuery({
    queryKey: ['password-policy'],
    queryFn: () => api<PasswordPolicy>('/api/account/password-policy'),
    staleTime: Infinity,
  })
}

export const useRegister = () => useMutation({ mutationFn: (input: { email: string; password: string }) => post('/api/account/register', input) })

export const useResendConfirmation = () => useMutation({ mutationFn: (email: string) => post('/api/account/resend-confirmation', { email }) })

export const useConfirmEmail = () =>
  useMutation({ mutationFn: (input: { userId: string; code: string }) => post('/api/account/confirm-email', input) })

export const useConfirmEmailChange = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { userId: string; email: string; code: string }) => post('/api/account/confirm-email-change', input),
    onSuccess: () => Promise.all([queryClient.invalidateQueries({ queryKey: ['account'] }), queryClient.invalidateQueries({ queryKey: sessionQueryKey })]),
  })
}

export const useForgotPassword = () => useMutation({ mutationFn: (email: string) => post('/api/account/forgot-password', { email }) })

export const useResetPassword = () =>
  useMutation({ mutationFn: (input: { email: string; code: string; password: string }) => post('/api/account/reset-password', input) })

// ---------- 2FA βήμα στο login ----------
type LoginResponse = { result: LoginResult | 'expired'; user: SessionUser | null }

export function useTwoFactorPending() {
  return useQuery({
    queryKey: ['two-factor-pending'],
    queryFn: () => api<void>('/api/auth/two-factor'),
    retry: false,
    staleTime: 0,
  })
}

export function useTwoFactorLogin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { code: string; rememberMe: boolean; rememberMachine: boolean } | { recoveryCode: string }) =>
      'recoveryCode' in input ? post<LoginResponse>('/api/auth/login-recovery', input) : post<LoginResponse>('/api/auth/login-2fa', input),
    onSuccess: (data) => {
      if (data.result === 'success') queryClient.setQueryData(sessionQueryKey, { user: data.user })
    },
  })
}

// ---------- Ο λογαριασμός μου ----------
export function useAccount() {
  return useQuery({ queryKey: ['account'], queryFn: () => api<Account>('/api/account') })
}

function useAccountMutation<TInput, TResult = void>(mutationFn: (input: TInput) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['account'] }),
  })
}

export const useUpdateProfile = () => useAccountMutation((phoneNumber: string) => api<Account>('/api/account/profile', { method: 'PUT', body: { phoneNumber } }))
export const useChangeEmail = () => useAccountMutation((newEmail: string) => post('/api/account/change-email', { newEmail }))
export const useSendVerificationEmail = () => useAccountMutation<void>(() => post('/api/account/send-verification-email'))
export const useChangePassword = () =>
  useAccountMutation((input: { currentPassword: string; newPassword: string }) => post('/api/account/change-password', input))

export function useAuthenticatorSetup(enabled: boolean) {
  return useQuery({
    queryKey: ['authenticator-setup'],
    queryFn: () => api<{ sharedKey: string; authenticatorUri: string }>('/api/account/2fa/authenticator'),
    enabled,
    staleTime: 0,
    gcTime: 0,
  })
}

export const useEnableTwoFactor = () => useAccountMutation((code: string) => post<{ recoveryCodes: string[] }>('/api/account/2fa/enable', { code }))
export const useRegenerateRecoveryCodes = () => useAccountMutation<void, { recoveryCodes: string[] }>(() => post<{ recoveryCodes: string[] }>('/api/account/2fa/recovery-codes'))
export const useDisableTwoFactor = () => useAccountMutation<void, Account>(() => post<Account>('/api/account/2fa/disable'))
export const useResetAuthenticator = () => useAccountMutation<void, Account>(() => post<Account>('/api/account/2fa/reset-authenticator'))
export const useForgetBrowser = () => useAccountMutation<void, Account>(() => post<Account>('/api/account/2fa/forget-browser'))

export function useDeleteAccount() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (password: string) => post('/api/account/delete', { password }),
    onSuccess: () => {
      queryClient.clear()
      queryClient.setQueryData(sessionQueryKey, { user: null })
    },
  })
}

// ---------- Φωτογραφία προφίλ ----------
// Η εικόνα ανεβαίνει πρώτα (MediaUploader, purpose "Avatar") και μετά ορίζεται εδώ

function useInvalidateAvatar() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([sessionQueryKey, ['users'], ['user'], ['role']].map((queryKey) => queryClient.invalidateQueries({ queryKey })))
}

export function useSetAvatar() {
  const invalidate = useInvalidateAvatar()
  return useMutation({
    mutationFn: (mediaId: string) => api<{ avatarUrl: string | null }>('/api/account/avatar', { method: 'PUT', body: { mediaId } }),
    onSuccess: invalidate,
  })
}

export function useRemoveAvatar() {
  const invalidate = useInvalidateAvatar()
  return useMutation({
    mutationFn: () => api('/api/account/avatar', { method: 'DELETE' }),
    onSuccess: invalidate,
  })
}
