import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { LogOut, User } from 'lucide-react'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useLogout, useSession } from '@/lib/auth'

export function UserMenu() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { user } = useSession()
  const logout = useLogout()
  if (!user) return null

  async function signOut() {
    await logout.mutateAsync()
    navigate('/login', { replace: true })
  }

  return (
    <DropdownMenu>
      {/* Μόνο το avatar (όπως GitHub/Vercel/Linear): το email φαίνεται μέσα στο μενού */}
      <DropdownMenuTrigger
        data-testid="user-menu"
        aria-label={t('userMenu.open')}
        className="rounded-full outline-none ring-offset-2 ring-offset-background transition-shadow hover:ring-2 hover:ring-border focus-visible:ring-2 focus-visible:ring-ring"
      >
        <Avatar>
          {user.avatarUrl && <AvatarImage src={user.avatarUrl} alt="" data-testid="user-menu-avatar" />}
          <AvatarFallback className="bg-primary text-xs font-semibold text-primary-foreground">
            {user.email.charAt(0).toUpperCase()}
          </AvatarFallback>
        </Avatar>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuLabel className="font-normal">
          <div className="text-xs text-muted-foreground">{t('userMenu.signedInAs')}</div>
          <div className="truncate text-sm font-medium">{user.email}</div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link to="/account">
            <User /> {t('nav.myAccount')}
          </Link>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem variant="destructive" onSelect={signOut} disabled={logout.isPending}>
          <LogOut /> {t('userMenu.signOut')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
