// Μικρός fetch wrapper για το backend API:
// - στέλνει/δέχεται JSON
// - βάζει το antiforgery token (cookie XSRF-TOKEN -> header X-XSRF-TOKEN) σε κάθε μη-GET request
// - πετάει ApiError με status + body όταν η απάντηση δεν είναι 2xx
// - στέλνει την τρέχουσα γλώσσα (Accept-Language): μηνύματα λάθους και emails έρχονται στη γλώσσα του χρήστη

import i18n, { currentLanguage } from '@/i18n'

export class ApiError extends Error {
  readonly status: number
  readonly data: unknown

  constructor(status: number, data: unknown) {
    super(`Request failed with status ${status}`)
    this.status = status
    this.data = data
  }
}

function readCookie(name: string): string | null {
  const match = document.cookie.split('; ').find((c) => c.startsWith(`${name}=`))
  return match ? decodeURIComponent(match.slice(name.length + 1)) : null
}

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  body?: unknown
  signal?: AbortSignal
}

// Το antiforgery token για μη-GET requests (και για όσα δεν περνούν από το api(), π.χ. τα uploads).
// Αν δεν έχουμε πάρει ακόμα token (π.χ. πολύ γρήγορο submit), το ζητάμε πρώτα.
export async function xsrfHeaders(): Promise<Record<string, string>> {
  if (!readCookie('XSRF-TOKEN')) await fetch('/api/auth/session', { credentials: 'same-origin' })
  const token = readCookie('XSRF-TOKEN')
  return token ? { 'X-XSRF-TOKEN': token } : {}
}

export async function api<T>(path: string, { method = 'GET', body, signal }: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', 'Accept-Language': currentLanguage() }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (method !== 'GET') Object.assign(headers, await xsrfHeaders())

  const response = await fetch(path, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    credentials: 'same-origin',
    signal,
  })

  const data = parseBody(await response.text())
  if (!response.ok) throw new ApiError(response.status, data)
  return data as T
}

export function parseBody(text: string): unknown {
  if (!text) return null
  try {
    return JSON.parse(text)
  } catch {
    return text
  }
}

type ProblemDetails = { title?: string; detail?: string; errors?: Record<string, string[]> }

// Ένα φιλικό μήνυμα από οποιοδήποτε λάθος (ProblemDetails.detail, validation errors, status)
export function problemMessage(error: unknown, fallback?: string): string {
  fallback ??= i18n.t('errors.generic')
  if (!(error instanceof ApiError)) return fallback
  const problem = (error.data ?? {}) as ProblemDetails
  if (problem.detail) return problem.detail
  const firstFieldError = problem.errors && Object.values(problem.errors)[0]?.[0]
  if (firstFieldError) return firstFieldError
  if (error.status === 403) return i18n.t('errors.forbidden')
  if (error.status === 404) return i18n.t('errors.notFound')
  return fallback
}

// Validation errors ανά πεδίο: { email: "...", password: "..." } (τα keys σε camelCase)
export function fieldErrors(error: unknown): Record<string, string> {
  if (!(error instanceof ApiError)) return {}
  const errors = ((error.data ?? {}) as ProblemDetails).errors ?? {}
  return Object.fromEntries(
    Object.entries(errors).map(([key, messages]) => [key.charAt(0).toLowerCase() + key.slice(1), messages[0] ?? '']),
  )
}

export type Paged<T> = { items: T[]; total: number; page: number; pageSize: number }

// Query string χωρίς τα κενά πεδία
export function toQuery(params: Record<string, string | number | undefined | null>) {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}
