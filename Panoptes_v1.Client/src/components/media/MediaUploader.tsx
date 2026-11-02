import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import * as FilePond from 'filepond'
import FilePondPluginFileValidateSize from 'filepond-plugin-file-validate-size'
import FilePondPluginFileValidateType from 'filepond-plugin-file-validate-type'
import FilePondPluginImageCrop from 'filepond-plugin-image-crop'
import FilePondPluginImageExifOrientation from 'filepond-plugin-image-exif-orientation'
import FilePondPluginImagePreview from 'filepond-plugin-image-preview'
import FilePondPluginImageResize from 'filepond-plugin-image-resize'
import FilePondPluginImageTransform from 'filepond-plugin-image-transform'
import FilePondPluginImageValidateSize from 'filepond-plugin-image-validate-size'
import 'filepond/dist/filepond.min.css'
import 'filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css'
import './filepond.css'
import { Skeleton } from '@/components/ui/skeleton'
import i18n, { currentLanguage } from '@/i18n'
import { ApiError, problemMessage } from '@/lib/api'
import { useMediaSettings, type MediaItem, type MediaKind, type MediaSettings } from '@/lib/media-api'
import { revertUpload, uploadFile, type UploadTarget } from '@/lib/media-upload'
import { cn } from '@/lib/utils'

FilePond.registerPlugin(
  FilePondPluginFileValidateType,
  FilePondPluginFileValidateSize,
  FilePondPluginImageExifOrientation,
  FilePondPluginImagePreview,
  FilePondPluginImageValidateSize,
  FilePondPluginImageCrop,
  FilePondPluginImageResize,
  FilePondPluginImageTransform,
)

// Ο browser δεν δίνει αξιόπιστο MIME (π.χ. .csv στα Windows): ο τύπος βγαίνει από την κατάληξη.
// Ο πραγματικός έλεγχος γίνεται στον server (περιεχόμενο αρχείου).
const mimeByExtension: Record<string, string> = {
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  png: 'image/png',
  gif: 'image/gif',
  webp: 'image/webp',
  svg: 'image/svg+xml',
  pdf: 'application/pdf',
  docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  pptx: 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
  odt: 'application/vnd.oasis.opendocument.text',
  ods: 'application/vnd.oasis.opendocument.spreadsheet',
  odp: 'application/vnd.oasis.opendocument.presentation',
  txt: 'text/plain',
  csv: 'text/csv',
  zip: 'application/zip',
}

const extensionOf = (name: string) => name.split('.').pop()?.toLowerCase() ?? ''

export type MediaUploaderProps = {
  target: UploadTarget
  multiple?: boolean
  accept?: MediaKind[] // Προεπιλογή: όλα τα είδη
  initialFiles?: File[]
  onUploaded?: (item: MediaItem) => void
  onReverted?: () => void
  // Πριν από την αναίρεση (undo) ενός upload, π.χ. αποσύνδεση από την εγγραφή όπου συνδέθηκε (αλλιώς ο server την αρνείται)
  beforeRevert?: (item: MediaItem) => Promise<unknown>
  onBusyChange?: (busy: boolean) => void
  // Τετράγωνο κόψιμο + σμίκρυνση στον browser πριν το upload (π.χ. φωτογραφία προφίλ). Ο server ξαναεπεξεργάζεται ούτως ή άλλως.
  squareCrop?: number
  // Μέσα σε modal: αρχεία που αφήνονται οπουδήποτε στη σελίδα πάνε σε αυτό (και ο browser δεν ανοίγει το αρχείο)
  dropAnywhere?: boolean
  labelIdle?: string
  className?: string
}

// Το δικό μας wrapper γύρω από το FilePond (core): drag & drop, πρόοδος, ακύρωση, retry, undo.
// Τη μεταφορά την κάνει το uploadFile (chunks με συνέχιση) ώστε να δουλεύουν και ελληνικά ονόματα αρχείων.
export function MediaUploader(props: MediaUploaderProps) {
  const settings = useMediaSettings()
  const { i18n: { language } } = useTranslation('media')
  const inputRef = useRef<HTMLInputElement>(null)
  const pondRef = useRef<FilePond.FilePond | null>(null)
  const latest = useRef(props)
  // uploadId -> το αρχείο που δημιουργήθηκε (για το beforeRevert)
  const uploaded = useRef(new Map<string, MediaItem>())

  useEffect(() => {
    latest.current = props
  })

  const acceptKey = props.accept?.join(',') ?? ''
  const multiple = props.multiple ?? true
  const { squareCrop, labelIdle, dropAnywhere } = props

  useEffect(() => {
    if (!settings.data || !inputRef.current) return
    const accept = acceptKey ? (acceptKey.split(',') as MediaKind[]) : null
    const pond = FilePond.create(inputRef.current, {
      ...pondOptions(settings.data, accept, multiple, !!squareCrop),
      ...pondLabels(settings.data, accept, multiple, !!squareCrop),
      ...(squareCrop ? cropOptions(squareCrop) : {}),
      ...(labelIdle ? { labelIdle } : {}),
      ...(dropAnywhere ? { dropOnPage: true, dropOnElement: false } : {}),
    })
    pondRef.current = pond

    const reportBusy = () => {
      const busy = pond.getFiles().some((f) => f.status === FilePond.FileStatus.PROCESSING || f.status === FilePond.FileStatus.PROCESSING_QUEUED)
      latest.current.onBusyChange?.(busy)
    }
    pond.setOptions({
      onaddfile: reportBusy,
      onprocessfilestart: reportBusy,
      onprocessfile: reportBusy,
      onprocessfileabort: reportBusy,
      onremovefile: reportBusy,
      server: {
        process: (_field, file, _metadata, load, error, progress, abort) => {
          const controller = new AbortController()
          let uploadId: string | undefined
          let done = false
          uploadFile(file as File, latest.current.target, {
            signal: controller.signal,
            onStart: (id) => (uploadId = id),
            onProgress: (loaded, total) => progress(true, loaded, total),
          })
            .then((result) => {
              done = true
              uploaded.current.set(result.uploadId, result.item)
              load(result.uploadId)
              latest.current.onUploaded?.(result.item)
            })
            .catch((e: unknown) => {
              if (controller.signal.aborted) return
              error(e instanceof ApiError ? problemMessage(e) : i18n.t('uploader.failed', { ns: 'media' }))
            })

          return {
            // Ακύρωση ενώ ανεβαίνει: ό,τι πρόλαβε να ανέβει σβήνεται. Το FilePond καλεί abort και όταν κλείνει
            // (destroy), ακόμα και για ολοκληρωμένα uploads: αυτά μένουν (αναίρεση μόνο με το "undo").
            abort: () => {
              if (done) return
              controller.abort()
              if (uploadId) revertUpload(uploadId).catch(() => {})
              abort()
            },
          }
        },
        revert: (uploadId, load, error) => {
          const item = uploaded.current.get(uploadId)
          const before = item && latest.current.beforeRevert ? latest.current.beforeRevert(item) : Promise.resolve()
          before
            .then(() => revertUpload(uploadId))
            .then(() => {
              load()
              latest.current.onReverted?.()
            })
            .catch((e: unknown) => error(problemMessage(e)))
        },
        fetch: null,
        restore: null,
        load: null,
      },
    })

    const initial = latest.current.initialFiles
    if (initial?.length) void pond.addFiles(initial)

    return () => {
      pond.destroy()
      pondRef.current = null
    }
  }, [settings.data, acceptKey, multiple, squareCrop, labelIdle, dropAnywhere])

  // Αλλαγή γλώσσας: μόνο τα κείμενα (χωρίς να ξαναστηθεί το FilePond και να χαθούν τα uploads σε εξέλιξη)
  useEffect(() => {
    if (!settings.data || !pondRef.current) return
    const accept = acceptKey ? (acceptKey.split(',') as MediaKind[]) : null
    pondRef.current.setOptions({ ...pondLabels(settings.data, accept, multiple, !!squareCrop), ...(labelIdle ? { labelIdle } : {}) })
  }, [language, settings.data, acceptKey, multiple, squareCrop, labelIdle])

  if (!settings.data) return <Skeleton className={cn('h-[76px] rounded-xl', props.className)} />

  return (
    <div className={cn('media-uploader', props.className)}>
      <input ref={inputRef} type="file" />
    </div>
  )
}

// Κόψιμο στο κέντρο σε τετράγωνο, σμίκρυνση και JPEG (μικρό αρχείο, ίδιο αποτέλεσμα σε κάθε browser)
function cropOptions(size: number) {
  return {
    allowImageCrop: true,
    imageCropAspectRatio: '1:1',
    allowImageResize: true,
    imageResizeTargetWidth: size,
    imageResizeTargetHeight: size,
    imageResizeMode: 'cover',
    imageResizeUpscale: false,
    allowImageTransform: true,
    imageTransformOutputMimeType: 'image/jpeg',
    imageTransformOutputQuality: 90,
    imagePreviewHeight: 180,
    stylePanelLayout: 'compact circle',
    stylePanelAspectRatio: '1:1',
    styleLoadIndicatorPosition: 'center bottom',
    styleProgressIndicatorPosition: 'right bottom',
    styleButtonRemoveItemPosition: 'left bottom',
    styleButtonProcessItemPosition: 'right bottom',
  } as FilePond.FilePondOptions
}

function allowedExtensions(settings: MediaSettings, accept: MediaKind[] | null, photo: boolean) {
  const kinds = settings.kinds.filter((k) => !accept || accept.includes(k.kind))
  const extensions = kinds.flatMap((k) => k.extensions).filter((e) => !photo || e !== 'svg')
  return { kinds, extensions }
}

function pondOptions(settings: MediaSettings, accept: MediaKind[] | null, multiple: boolean, photo: boolean): FilePond.FilePondOptions {
  const { kinds, extensions } = allowedExtensions(settings, accept, photo)
  const mimes = [...new Set(extensions.map((e) => mimeByExtension[e]).filter(Boolean))]
  const maxBytes = Math.max(...kinds.map((k) => k.maxBytes))

  return {
    allowMultiple: multiple,
    maxParallelUploads: 3,
    instantUpload: true,
    allowRevert: true,
    credits: false,

    // Τύπος: από την κατάληξη (ίδια λίστα με τον server)
    acceptedFileTypes: mimes,
    fileValidateTypeDetectType: (file: File, type: string) => Promise.resolve(mimeByExtension[extensionOf(file.name)] ?? type ?? ''),

    // Μέγεθος: το μεγαλύτερο όριο εδώ, το ακριβές ανά είδος το ελέγχει ο server πριν σταλεί οτιδήποτε
    maxFileSize: `${Math.floor(maxBytes / 1024)}KB`,

    imageValidateSizeMaxResolution: settings.maxPixels,

    imagePreviewHeight: 140,
    imagePreviewMaxFileSize: '10MB',

    // Μετασχηματισμοί μόνο όταν ζητηθούν (αλλιώς τα αρχεία πάνε ακριβώς όπως είναι, π.χ. κινούμενα GIF)
    allowImageCrop: false,
    allowImageResize: false,
    allowImageTransform: false,
  } as FilePond.FilePondOptions
}

// Όλα τα κείμενα του FilePond και των plugins στη γλώσσα της εφαρμογής (και όσα αλλιώς θα έμεναν στα αγγλικά defaults).
// Τα {filesize}, {minWidth} κ.λπ. (μονά άγκιστρα) τα συμπληρώνει το ίδιο το FilePond.
function pondLabels(settings: MediaSettings, accept: MediaKind[] | null, multiple: boolean, photo: boolean): FilePond.FilePondOptions {
  const t = i18n.getFixedT(null, 'media')
  const tc = i18n.getFixedT(null, 'common')
  const allowed = allowedExtensions(settings, accept, photo).extensions.filter((e) => e !== 'jpeg').map((e) => e.toUpperCase())
  const separators = new Intl.NumberFormat(currentLanguage()).formatToParts(1000.5)

  return {
    labelIdle: multiple ? t('uploader.idleFiles') : t('uploader.idleFile'),
    labelDecimalSeparator: separators.find((p) => p.type === 'decimal')?.value ?? '.',
    labelThousandsSeparator: separators.find((p) => p.type === 'group')?.value ?? ',',
    labelInvalidField: t('uploader.invalidField'),
    labelFileWaitingForSize: t('uploader.waitingForSize'),
    labelFileSizeNotAvailable: t('uploader.sizeNotAvailable'),
    labelFileCountSingular: t('uploader.fileCountSingular'),
    labelFileCountPlural: t('uploader.fileCountPlural'),
    labelFileLoading: t('uploader.loading'),
    labelFileAdded: t('uploader.added'),
    labelFileLoadError: t('uploader.loadError'),
    labelFileRemoved: t('uploader.removed'),
    labelFileRemoveError: t('uploader.removeError'),
    labelFileProcessing: t('uploader.processing'),
    labelFileProcessingComplete: t('uploader.processingComplete'),
    labelFileProcessingAborted: t('uploader.processingAborted'),
    labelFileProcessingError: (error: { body?: string } | null) => error?.body || t('uploader.processingError'),
    labelFileProcessingRevertError: t('uploader.revertError'),
    labelFileSizeBytes: t('uploader.bytes'),
    labelTapToCancel: t('uploader.tapToCancel'),
    labelTapToRetry: t('uploader.tapToRetry'),
    labelTapToUndo: t('uploader.tapToUndo'),
    labelButtonRemoveItem: tc('actions.remove'),
    labelButtonAbortItemLoad: t('uploader.abort'),
    labelButtonRetryItemLoad: tc('actions.retry'),
    labelButtonAbortItemProcessing: tc('actions.cancel'),
    labelButtonUndoItemProcessing: tc('actions.undo'),
    labelButtonRetryItemProcessing: tc('actions.retry'),
    labelButtonProcessItem: tc('actions.upload'),

    labelFileTypeNotAllowed: t('uploader.typeNotAllowed'),
    fileValidateTypeLabelExpectedTypes: t('uploader.expectedTypes', { types: `${allowed.slice(0, 6).join(', ')}${allowed.length > 6 ? '…' : ''}` }),

    labelMaxFileSizeExceeded: t('uploader.fileTooLarge'),
    labelMaxFileSize: t('uploader.maxFileSize'),
    labelMinFileSizeExceeded: t('uploader.fileTooSmall'),
    labelMinFileSize: t('uploader.minFileSize'),
    labelMaxTotalFileSizeExceeded: t('uploader.totalTooLarge'),
    labelMaxTotalFileSize: t('uploader.maxTotalFileSize'),

    imageValidateSizeLabelFormatError: t('uploader.imageFormatError'),
    imageValidateSizeLabelImageSizeTooSmall: t('uploader.imageTooSmall'),
    imageValidateSizeLabelImageSizeTooBig: t('uploader.imageTooBig'),
    imageValidateSizeLabelExpectedMinSize: t('uploader.minSize'),
    imageValidateSizeLabelExpectedMaxSize: t('uploader.maxSize'),
    imageValidateSizeLabelImageResolutionTooLow: t('uploader.resolutionTooLow'),
    imageValidateSizeLabelImageResolutionTooHigh: t('uploader.imageTooLarge'),
    imageValidateSizeLabelExpectedMinResolution: t('uploader.minResolution'),
    imageValidateSizeLabelExpectedMaxResolution: t('uploader.maxResolution', { megapixels: Math.round(settings.maxPixels / 1_000_000) }),
  } as FilePond.FilePondOptions
}
