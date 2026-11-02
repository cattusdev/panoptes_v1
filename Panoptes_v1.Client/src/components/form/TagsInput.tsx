import { useId, useState, type Ref } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'

// Ελεύθερες ετικέτες (όπως στο Linear/GitHub labels): Enter ή κόμμα προσθέτει, Backspace σε κενό πεδίο σβήνει την τελευταία.
// suggestions: όσες υπάρχουν ήδη (προτείνονται από τον browser). Διπλές (χωρίς διάκριση πεζών/κεφαλαίων) δεν μπαίνουν.
export function TagsInput({
  id,
  value,
  onChange,
  onBlur,
  ref,
  suggestions = [],
  placeholder,
  maxLength = 40,
  className,
  'aria-invalid': ariaInvalid,
}: {
  id?: string
  value: string[]
  onChange: (value: string[]) => void
  onBlur?: () => void
  ref?: Ref<HTMLInputElement>
  suggestions?: string[]
  placeholder?: string
  maxLength?: number
  className?: string
  'aria-invalid'?: true
}) {
  const { t } = useTranslation()
  const listId = useId()
  const [draft, setDraft] = useState('')

  function add(text: string) {
    const tag = text.trim().replace(/,$/, '').trim()
    if (tag && !value.some((existing) => existing.toLowerCase() === tag.toLowerCase())) onChange([...value, tag])
    setDraft('')
  }

  return (
    <div className={cn('flex flex-col gap-2', className)}>
      <Input
        id={id}
        ref={ref}
        value={draft}
        list={listId}
        maxLength={maxLength}
        placeholder={placeholder ?? t('tags.placeholder')}
        aria-invalid={ariaInvalid}
        onChange={(e) => (e.target.value.endsWith(',') ? add(e.target.value) : setDraft(e.target.value))}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault()
            add(draft)
          } else if (e.key === 'Backspace' && !draft && value.length > 0) {
            onChange(value.slice(0, -1))
          }
        }}
        onBlur={() => {
          if (draft.trim()) add(draft)
          onBlur?.()
        }}
      />
      <datalist id={listId}>
        {suggestions
          .filter((s) => !value.some((v) => v.toLowerCase() === s.toLowerCase()))
          .map((s) => (
            <option key={s} value={s} />
          ))}
      </datalist>
      {value.length > 0 && (
        <ul className="flex flex-wrap gap-1.5" aria-label={t('tags.selected')}>
          {value.map((tag) => (
            <li key={tag}>
              <Badge variant="muted" className="gap-1 pr-1">
                {tag}
                <button
                  type="button"
                  className="rounded-sm opacity-70 hover:opacity-100 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                  aria-label={t('tags.remove', { tag })}
                  onClick={() => onChange(value.filter((v) => v !== tag))}
                >
                  <X className="size-3" />
                </button>
              </Badge>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
