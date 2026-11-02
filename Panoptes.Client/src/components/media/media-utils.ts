import { useState, type ComponentType, type DragEvent } from 'react'
import { File, FileArchive, FileImage, FileSpreadsheet, FileText, Presentation } from 'lucide-react'
import i18n from '@/i18n'
import type { MediaFolder } from '@/lib/media-api'

// Τύπος δεδομένων για drag & drop αρχείων της βιβλιοθήκης πάνω σε φακέλους
export const MEDIA_DRAG_TYPE = 'application/x-panoptes-media'

export function childrenOf(folders: MediaFolder[], parentId: string | null) {
  return folders.filter((f) => f.parentId === parentId).sort((a, b) => a.name.localeCompare(b.name))
}

// Drop στόχος: δέχεται μόνο αρχεία της βιβλιοθήκης (όχι αρχεία από τον υπολογιστή)
export function useMediaDropTarget(onDrop?: (ids: string[]) => void) {
  const [over, setOver] = useState(false)
  if (!onDrop) return { over: false, props: {} }
  return {
    over,
    props: {
      onDragOver: (e: DragEvent) => {
        if (!e.dataTransfer.types.includes(MEDIA_DRAG_TYPE)) return
        e.preventDefault()
        e.dataTransfer.dropEffect = 'move'
        setOver(true)
      },
      onDragLeave: () => setOver(false),
      onDrop: (e: DragEvent) => {
        setOver(false)
        const data = e.dataTransfer.getData(MEDIA_DRAG_TYPE)
        if (!data) return
        e.preventDefault()
        e.stopPropagation()
        onDrop(JSON.parse(data) as string[])
      },
    },
  }
}

type FileStyle = { icon: ComponentType<{ className?: string }>; tint: string; label: string }
type GenericType = 'document' | 'text' | 'spreadsheet' | 'presentation'

// Εικονίδιο + χρώμα ανά τύπο (όπως Drive/Dropbox: PDF κόκκινο, Word μπλε, Excel πράσινο...).
// Ονόματα προϊόντων/μορφών μένουν ως έχουν· τα γενικά (Document, Text...) μεταφράζονται τη στιγμή της χρήσης.
const styles: Record<string, Omit<FileStyle, 'label'> & { label: string | { type: GenericType } }> = {
  pdf: { icon: FileText, tint: 'bg-destructive/12 text-destructive', label: 'PDF' },
  docx: { icon: FileText, tint: 'bg-info/12 text-info', label: 'Word' },
  odt: { icon: FileText, tint: 'bg-info/12 text-info', label: { type: 'document' } },
  txt: { icon: FileText, tint: 'bg-muted text-muted-foreground', label: { type: 'text' } },
  xlsx: { icon: FileSpreadsheet, tint: 'bg-success/12 text-success', label: 'Excel' },
  ods: { icon: FileSpreadsheet, tint: 'bg-success/12 text-success', label: { type: 'spreadsheet' } },
  csv: { icon: FileSpreadsheet, tint: 'bg-success/12 text-success', label: 'CSV' },
  pptx: { icon: Presentation, tint: 'bg-warning/12 text-warning', label: 'PowerPoint' },
  odp: { icon: Presentation, tint: 'bg-warning/12 text-warning', label: { type: 'presentation' } },
  zip: { icon: FileArchive, tint: 'bg-purple/14 text-purple', label: 'ZIP' },
  svg: { icon: FileImage, tint: 'bg-purple/14 text-purple', label: 'SVG' },
}

export function fileStyle(extension: string): FileStyle {
  const style = styles[extension]
  if (!style) return { icon: File, tint: 'bg-muted text-muted-foreground', label: extension.toUpperCase() }
  const label = typeof style.label === 'string' ? style.label : i18n.t(`fileTypes.${style.label.type}`, { ns: 'media' })
  return { ...style, label }
}
