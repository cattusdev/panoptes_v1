import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { Building2, Contact, LogOut, Search, type LucideIcon } from 'lucide-react'
import { navPages } from '@/components/layout/nav'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Command, CommandDialog, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Kbd, KbdGroup } from '@/components/ui/kbd'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { hasPermission, useLogout, useSession } from '@/lib/auth'
import { searchMinLength, useGlobalSearch, type SearchHit } from '@/lib/search-api'
import { accountSections } from '@/pages/account/sections'
import { settingsSections } from '@/pages/admin/settings/sections'

const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.userAgent)

// Πού ανοίγει κάθε τύπος αποτελέσματος του /api/search (νέο module = μία γραμμή εδώ + common:palette.groups.<type>)
const searchTargets: Record<string, { icon: LucideIcon; to: (hit: SearchHit) => string }> = {
  client: { icon: Building2, to: (hit) => `/clients/${hit.id}` },
  person: { icon: Contact, to: (hit) => `/contacts?person=${hit.id}` },
}

// Ctrl/⌘+K (όπως Linear, Vercel, GitHub): εγγραφές από το /api/search (ARCHITECTURE §10, μόνο όσες βλέπει ο χρήστης),
// μετάβαση σε όποια σελίδα επιτρέπεται και γρήγορες ενέργειες.
export function CommandPalette() {
  const { t } = useTranslation()
  const { t: tSettings } = useTranslation('settings')
  const { t: tAccount } = useTranslation('account')
  const navigate = useNavigate()
  const { user } = useSession()
  const logout = useLogout()
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const debouncedSearch = useDebouncedValue(search, 200)
  const results = useGlobalSearch(open ? debouncedSearch : '')
  const groups = search.trim().length >= searchMinLength ? (results.data?.groups ?? []).filter((g) => searchTargets[g.type]) : []

  // Κάθε άνοιγμα ξεκινά με κενή αναζήτηση
  function changeOpen(value: boolean) {
    setOpen(value)
    if (!value) setSearch('')
  }

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey) && !event.altKey && !event.shiftKey) {
        // Αλλιώς ο browser πάει στη δική του αναζήτηση
        event.preventDefault()
        setSearch('')
        setOpen((value) => !value)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [])

  function go(to: string) {
    changeOpen(false)
    navigate(to)
  }

  async function signOut() {
    changeOpen(false)
    await logout.mutateAsync()
    navigate('/login', { replace: true })
  }

  const pages = navPages(user)
  const settings = settingsSections.filter((section) => !section.permission || hasPermission(user, section.permission))
  const settingsLabel = t('nav.settings')
  const accountLabel = t('nav.myAccount')

  return (
    <>
      <Button
        variant="outline"
        onClick={() => setOpen(true)}
        aria-label={t('palette.open')}
        aria-keyshortcuts="Control+K Meta+K"
        className="size-9 justify-start gap-2 px-2.5 font-normal text-muted-foreground md:w-64"
      >
        <Search className="size-4 shrink-0" />
        <span className="hidden flex-1 truncate text-left md:inline">{t('palette.open')}</span>
        <KbdGroup className="hidden md:inline-flex">
          <Kbd>{isMac ? '⌘' : 'Ctrl'}</Kbd>
          <Kbd>K</Kbd>
        </KbdGroup>
      </Button>

      <CommandDialog open={open} onOpenChange={changeOpen} title={t('palette.title')} description={t('palette.description')}>
        <Command loop>
          <CommandInput placeholder={t('palette.placeholder')} value={search} onValueChange={setSearch} />
          <CommandList className="max-h-[min(60vh,24rem)]">
            <CommandEmpty>{results.isFetching ? t('palette.searching') : t('palette.empty')}</CommandEmpty>

            {groups.map((group) => {
              const target = searchTargets[group.type]
              const Icon = target.icon
              return (
                <CommandGroup key={group.type} heading={t(`palette.groups.${group.type as 'client' | 'person'}`)}>
                  {group.hits.map((hit) => (
                    <CommandItem
                      key={`hit-${hit.type}-${hit.id}`}
                      value={`hit-${hit.type}-${hit.id}`}
                      // Ο server έχει ήδη φιλτράρει: το κείμενο της αναζήτησης ταιριάζει πάντα, ώστε το cmdk να μην τα κρύψει
                      keywords={[search, hit.title, hit.subtitle ?? '', hit.reference ?? '']}
                      onSelect={() => go(target.to(hit))}
                    >
                      <Icon />
                      <span className="min-w-0 flex-1 truncate">
                        {hit.title}
                        {hit.subtitle && <span className="ml-2 text-xs text-muted-foreground">{hit.subtitle}</span>}
                      </span>
                      {hit.isArchived && <Badge variant="muted">{t('palette.archived')}</Badge>}
                      {hit.reference && <span className="font-mono text-xs text-muted-foreground">{hit.reference}</span>}
                    </CommandItem>
                  ))}
                </CommandGroup>
              )
            })}

            {pages.length > 0 && (
              <CommandGroup heading={t('palette.groups.pages')}>
                {pages.map((page) => {
                  const Icon = page.icon
                  const label = t(`nav.${page.label}`)
                  return (
                    <CommandItem key={page.to} value={label} onSelect={() => go(page.to)}>
                      <Icon />
                      {label}
                    </CommandItem>
                  )
                })}
              </CommandGroup>
            )}

            {settings.length > 0 && (
              <CommandGroup heading={settingsLabel}>
                {settings.map((section) => {
                  const Icon = section.icon
                  const label = tSettings(`sections.${section.id}.label`)
                  return (
                    <CommandItem key={section.to} value={`${settingsLabel} ${label}`} onSelect={() => go(section.to)}>
                      <Icon />
                      {label}
                    </CommandItem>
                  )
                })}
              </CommandGroup>
            )}

            <CommandGroup heading={accountLabel}>
              {accountSections.map((section) => {
                const Icon = section.icon
                const label = tAccount(`layout.tabs.${section.id}`)
                return (
                  <CommandItem key={section.to} value={`${accountLabel} ${label}`} onSelect={() => go(section.to)}>
                    <Icon />
                    {label}
                  </CommandItem>
                )
              })}
              <CommandItem value={t('userMenu.signOut')} onSelect={signOut} disabled={logout.isPending}>
                <LogOut />
                {t('userMenu.signOut')}
              </CommandItem>
            </CommandGroup>
          </CommandList>

          <div className="flex items-center gap-4 border-t px-3 py-2 text-xs text-muted-foreground" aria-hidden>
            <span className="flex items-center gap-1.5">
              <KbdGroup>
                <Kbd>↑</Kbd>
                <Kbd>↓</Kbd>
              </KbdGroup>
              {t('palette.hints.navigate')}
            </span>
            <span className="flex items-center gap-1.5">
              <Kbd>↵</Kbd>
              {t('palette.hints.select')}
            </span>
            <span className="flex items-center gap-1.5">
              <Kbd>Esc</Kbd>
              {t('palette.hints.close')}
            </span>
          </div>
        </Command>
      </CommandDialog>
    </>
  )
}
