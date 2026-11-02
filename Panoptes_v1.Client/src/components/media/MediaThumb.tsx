import type { MediaItem } from '@/lib/media-api'
import { cn } from '@/lib/utils'
import { fileStyle } from './media-utils'

// Η μικρογραφία ενός αρχείου: εικόνα (με το μέσο χρώμα όσο φορτώνει) ή εικονίδιο τύπου
export function MediaThumb({ item, className, iconClassName }: { item: MediaItem; className?: string; iconClassName?: string }) {
  const { version } = item
  const src = version.urls.thumb ?? (version.extension === 'svg' ? version.urls.original : null)

  if (src) {
    return (
      <div className={cn('overflow-hidden bg-muted', className)} style={{ backgroundColor: version.dominantColor ?? undefined }}>
        <img
          src={src}
          alt={item.altText ?? ''}
          loading="lazy"
          decoding="async"
          draggable={false}
          className={cn('size-full', version.extension === 'svg' ? 'object-contain p-3' : 'object-cover')}
        />
      </div>
    )
  }

  const style = fileStyle(version.extension)
  const Icon = style.icon
  return (
    <div className={cn('flex items-center justify-center', style.tint, className)}>
      <Icon className={cn('size-8', iconClassName)} />
    </div>
  )
}
