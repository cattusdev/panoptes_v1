// Ανέβασμα αρχείου με το πρωτόκολλο του server (/api/media/uploads):
// POST (ξεκίνημα) -> PATCH ανά chunk με Upload-Offset -> το τελευταίο επιστρέφει το αρχείο.
// Σε διακοπή/σφάλμα δικτύου ξαναδοκιμάζει και συνεχίζει από εκεί που έμεινε (HEAD).
import i18n from '@/i18n'
import { api, ApiError, parseBody, xsrfHeaders } from '@/lib/api'
import type { MediaItem, MediaVisibility } from '@/lib/media-api'

export type UploadTarget = {
  folderId?: string | null
  visibility?: MediaVisibility
  replaceItemId?: string | null
  purpose?: 'Library' | 'Avatar' // Avatar: φωτογραφία προφίλ (για κάθε χρήστη, εκτός βιβλιοθήκης)
}

export type UploadResult = { uploadId: string; item: MediaItem }

type ChunkResponse = { status: number; offset: number | null; data: unknown }

const retryDelays = [1000, 3000, 6000]

export async function uploadFile(
  file: File,
  target: UploadTarget,
  { signal, onProgress, onStart }: { signal?: AbortSignal; onProgress?: (loaded: number, total: number) => void; onStart?: (uploadId: string) => void } = {},
): Promise<UploadResult> {
  const { id, chunkSize } = await api<{ id: string; chunkSize: number }>('/api/media/uploads', {
    method: 'POST',
    body: { fileName: file.name, size: file.size, ...target },
    signal,
  })
  onStart?.(id)

  let offset = 0
  let attempt = 0
  for (;;) {
    const chunk = file.slice(offset, Math.min(offset + chunkSize, file.size))
    let response: ChunkResponse
    try {
      response = await sendChunk(id, offset, chunk, signal, (sent) => onProgress?.(offset + sent, file.size))
    } catch (error) {
      if (signal?.aborted || attempt >= retryDelays.length) throw error
      await wait(retryDelays[attempt++], signal)
      offset = await serverOffset(id, signal) // Συνέχεια από ό,τι έφτασε στον server
      continue
    }

    if (response.status === 200) {
      onProgress?.(file.size, file.size)
      return { uploadId: id, item: response.data as MediaItem }
    }
    if (response.status === 204 || response.status === 409) {
      offset = response.offset ?? offset + chunk.size
      attempt = 0
      continue
    }
    // Server απασχολημένος: ξανά σε λίγο
    if (response.status === 503 && attempt < retryDelays.length) {
      await wait(retryDelays[attempt++], signal)
      continue
    }
    throw new ApiError(response.status, response.data)
  }
}

export function revertUpload(uploadId: string) {
  return api(`/api/media/uploads/${uploadId}`, { method: 'DELETE' })
}

// XHR (όχι fetch) για να έχουμε πρόοδο ανά byte
async function sendChunk(
  id: string,
  offset: number,
  chunk: Blob,
  signal: AbortSignal | undefined,
  onProgress: (sent: number) => void,
): Promise<ChunkResponse> {
  const headers = await xsrfHeaders()
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PATCH', `/api/media/uploads/${id}`)
    xhr.withCredentials = true
    xhr.setRequestHeader('Content-Type', 'application/offset+octet-stream')
    xhr.setRequestHeader('Upload-Offset', String(offset))
    xhr.setRequestHeader('Accept', 'application/json')
    for (const [name, value] of Object.entries(headers)) xhr.setRequestHeader(name, value)

    xhr.upload.onprogress = (e) => onProgress(e.loaded)
    xhr.onload = () => {
      const next = xhr.getResponseHeader('Upload-Offset')
      resolve({ status: xhr.status, offset: next === null ? null : Number(next), data: parseBody(xhr.responseText) })
    }
    xhr.onerror = () => reject(new Error(i18n.t('errors.network', { ns: 'media' })))
    xhr.onabort = () => reject(cancelled())

    if (signal) {
      if (signal.aborted) return reject(cancelled())
      signal.addEventListener('abort', () => xhr.abort(), { once: true })
    }
    xhr.send(chunk)
  })
}

async function serverOffset(id: string, signal?: AbortSignal) {
  const response = await fetch(`/api/media/uploads/${id}`, { method: 'HEAD', credentials: 'same-origin', signal })
  if (!response.ok) throw new ApiError(response.status, null)
  return Number(response.headers.get('Upload-Offset') ?? 0)
}

function wait(ms: number, signal?: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    const timer = setTimeout(resolve, ms)
    signal?.addEventListener('abort', () => {
      clearTimeout(timer)
      reject(cancelled())
    })
  })
}

function cancelled() {
  return new DOMException(i18n.t('errors.cancelled', { ns: 'media' }), 'AbortError')
}
