import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { LogOut, Search } from 'lucide-react'
import { navPages } from '@/components/layout/nav'
import { Button } from '@/components/ui/button'
import { Command, CommandDialog, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Kbd, KbdGroup } from '@/components/ui/kbd'
import { hasPermission, useLogout, useSession } from '@/lib/auth'
import { accountSections } from '@/pages/account/sections'
import { settingsSections } from '@/pages/admin/settings/sections'

const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.userAgent)

// Ctrl/⌘+K (όπως Linear, Vercel, GitHub): μετάβαση σε όποια σελίδα επιτρέπεται στον χρήστη και γρήγορες ενέργειες.
// Η αναζήτηση εγγραφών (πελάτες, projects...) θα μπει εδώ μέσω του /api/search (ARCHITECTURE §10).
export function CommandPalette() {
  const { t } = useTranslation()
  const { t: tSettings } = useTranslation('settings')
  const { t: tAccount } = useTranslation('account')
  const navigate = useNavigate()
  const { user } = useSession()
  const logout = useLogout()
  const [open, setOpen] = useState(false)

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey) && !event.altKey && !event.shiftKey) {
        // Αλλιώς ο browser πάει στη δική του αναζήτηση
        event.preventDefault()
        setOpen((value) => !value)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [])

  function go(to: string) {
    setOpen(false)
    navigate(to)
  }

  async function signOut() {
    setOpen(false)
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

      <CommandDialog open={open} onOpenChange={setOpen} title={t('palette.title')} description={t('palette.description')}>
        <Command loop>
          <CommandInput placeholder={t('palette.placeholder')} />
          <CommandList className="max-h-[min(60vh,24rem)]">
            <CommandEmpty>{t('palette.empty')}</CommandEmpty>

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
