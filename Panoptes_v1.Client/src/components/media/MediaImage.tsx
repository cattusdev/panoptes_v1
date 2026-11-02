import type { MediaItem } from '@/lib/media-api'
import { cn } from '@/lib/utils'
import { MediaThumb } from './MediaThumb'

// Εικόνα της βιβλιοθήκης για οποιαδήποτε σελίδα: ο browser διαλέγει μέγεθος (srcset), δεσμεύεται ο χώρος
// (width/height, χωρίς "πήδημα" της σελίδας), φορτώνει όταν φανεί, με το μέσο χρώμα μέχρι τότε.
export function MediaImage({
  item,
  sizes = '100vw',
  className,
  alt,
  eager,
}: {
  item: MediaItem
  sizes?: string // π.χ. "(min-width: 768px) 50vw, 100vw"
  className?: string
  alt?: string
  eager?: boolean
}) {
  const { version } = item
  if (item.kind !== 'Image') return <MediaThumb item={item} className={className} />

  const candidates = [
    [version.urls.medium, Math.min(version.width ?? 800, 800)],
    [version.urls.large, Math.min(version.width ?? 1600, 1600)],
    [version.urls.original, version.width],
  ] as const
  // Μόνο διαφορετικά πλάτη (μικρές εικόνες έχουν ίδιο medium/large/original)
  const seen = new Set<number>()
  const srcSet = candidates
    .filter((c): c is readonly [string, number] => !!c[0] && !!c[1])
    .filter(([, width]) => !seen.has(width) && !!seen.add(width))
    .map(([url, width]) => `${url} ${width}w`)
    .join(', ')

  return (
    <img
      src={version.urls.medium ?? version.urls.original}
      srcSet={srcSet || undefined}
      sizes={srcSet ? sizes : undefined}
      width={version.width ?? undefined}
      height={version.height ?? undefined}
      alt={alt ?? item.altText ?? ''}
      loading={eager ? 'eager' : 'lazy'}
      decoding="async"
      style={{ backgroundColor: version.dominantColor ?? undefined }}
      className={cn('h-auto max-w-full', className)}
    />
  )
}
