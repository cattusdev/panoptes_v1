import type { ComponentType } from 'react'
import { createBrowserRouter } from 'react-router'
import { ConfirmEmailChangePage, ConfirmEmailPage, ResendConfirmationPage } from '@/pages/auth/EmailPages'
import { LoginPage } from '@/pages/auth/LoginPage'
import { HomeRedirect } from '@/pages/auth/HomeRedirect'
import { AccessDeniedPage, LockoutPage, NotFoundPage, RouteErrorPage } from '@/pages/auth/StatusPages'
import { TwoFactorPage } from '@/pages/auth/TwoFactorPage'
import { BrandingRoot } from '@/components/brand/BrandingRoot'
import { Permissions } from '@/lib/permissions'

// Το console (admin + account) φορτώνεται μόνο όταν χρειαστεί: το login μένει ελαφρύ
function lazyPage<M>(load: () => Promise<M>, pick: (module: M) => ComponentType) {
  return async () => ({ Component: pick(await load()) })
}

const consoleLayout = () => import('@/components/layout/ConsoleLayout')

// Οι σελίδες που θέλουν permission: χωρίς αυτό, "δεν έχετε πρόσβαση" μέσα στο console (ο server ελέγχει ούτως ή άλλως)
function requires(permission: string) {
  return async () => {
    const { RequirePermission } = await import('@/components/layout/RequirePermission')
    return { Component: () => <RequirePermission permission={permission} /> }
  }
}

export const router = createBrowserRouter([
  {
    // Error boundary για όλο το app
    errorElement: <RouteErrorPage />,
    element: <BrandingRoot />,
    children: [
      // Χωρίς δημόσιες σελίδες: το "/" πάει στο dashboard ή στη σύνδεση
      { path: '/', element: <HomeRedirect /> },

      // Σελίδες λογαριασμού (φωτεινό AuthLayout)
      { path: '/login', element: <LoginPage /> },
      { path: '/login/2fa', element: <TwoFactorPage /> },
      { path: '/confirm-email', element: <ConfirmEmailPage /> },
      { path: '/confirm-email-change', element: <ConfirmEmailChangePage /> },
      { path: '/resend-confirmation', element: <ResendConfirmationPage /> },
      { path: '/lockout', element: <LockoutPage /> },
      { path: '/access-denied', element: <AccessDeniedPage /> },

      // Console (σκούρο): χρειάζεται login
      {
        lazy: lazyPage(consoleLayout, (m) => m.ConsoleLayout),
        children: [
          {
            path: '/account',
            lazy: lazyPage(() => import('@/pages/account/AccountLayout'), (m) => m.AccountLayout),
            children: [
              { index: true, lazy: lazyPage(() => import('@/pages/account/ProfilePage'), (m) => m.ProfilePage) },
              { path: 'security', lazy: lazyPage(() => import('@/pages/account/SecurityPage'), (m) => m.SecurityPage) },
              { path: 'data', lazy: lazyPage(() => import('@/pages/account/DataPage'), (m) => m.DataPage) },
            ],
          },
          {
            path: '/admin',
            lazy: lazyPage(consoleLayout, (m) => m.RequireAdminArea),
            children: [
              { index: true, lazy: lazyPage(() => import('@/pages/admin/DashboardPage'), (m) => m.DashboardPage) },
              {
                lazy: requires(Permissions.Users.View),
                children: [
                  { path: 'users', lazy: lazyPage(() => import('@/pages/admin/users/UsersPage'), (m) => m.UsersPage) },
                  { path: 'users/:id', lazy: lazyPage(() => import('@/pages/admin/users/UserDetailPage'), (m) => m.UserDetailPage) },
                ],
              },
              {
                lazy: requires(Permissions.Roles.View),
                children: [
                  { path: 'roles', lazy: lazyPage(() => import('@/pages/admin/roles/RolesPage'), (m) => m.RolesPage) },
                  { path: 'roles/:id', lazy: lazyPage(() => import('@/pages/admin/roles/RoleDetailPage'), (m) => m.RoleDetailPage) },
                ],
              },
              {
                path: 'audit-logs',
                lazy: requires(Permissions.AuditLogs.View),
                children: [{ index: true, lazy: lazyPage(() => import('@/pages/admin/AuditLogsPage'), (m) => m.AuditLogsPage) }],
              },
              {
                path: 'settings',
                lazy: requires(Permissions.Settings.View),
                children: [
                  {
                    lazy: lazyPage(() => import('@/pages/admin/settings/SettingsLayout'), (m) => m.SettingsLayout),
                    children: [
                      { index: true, lazy: lazyPage(() => import('@/pages/admin/settings/GeneralSettingsPage'), (m) => m.GeneralSettingsPage) },
                      { path: 'branding', lazy: lazyPage(() => import('@/pages/admin/settings/BrandingPage'), (m) => m.BrandingPage) },
                      { path: 'email', lazy: lazyPage(() => import('@/pages/admin/settings/EmailSettingsPage'), (m) => m.EmailSettingsPage) },
                      { path: 'finance', lazy: lazyPage(() => import('@/pages/admin/settings/FinanceSettingsPage'), (m) => m.FinanceSettingsPage) },
                    ],
                  },
                ],
              },
              {
                path: 'media',
                lazy: requires(Permissions.Media.View),
                children: [{ index: true, lazy: lazyPage(() => import('@/pages/admin/media/MediaPage'), (m) => m.MediaPage) }],
              },
              // Μόνο με τα test endpoints (E2E): δοκιμή των κοινών components
              { path: 'dev/kit', lazy: lazyPage(() => import('@/pages/admin/dev/KitPage'), (m) => m.KitPage) },
              { path: '*', element: <NotFoundPage /> },
            ],
          },
        ],
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
