import { lazy, Suspense } from 'react'
import { Skeleton } from '@/components/ui/skeleton'
import type { MediaUploaderProps } from './MediaUploader'

// Το FilePond φορτώνεται μόνο όταν ανοίξει κάποιο upload (η βιβλιοθήκη/ο picker μένουν ελαφριά)
const MediaUploaderImpl = lazy(() => import('./MediaUploader').then((m) => ({ default: m.MediaUploader })))

export function MediaUploader(props: MediaUploaderProps) {
  return (
    <Suspense fallback={<Skeleton className="h-[76px] rounded-xl" />}>
      <MediaUploaderImpl {...props} />
    </Suspense>
  )
}
