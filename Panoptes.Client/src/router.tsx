import type { ComponentType } from 'react'
import { createBrowserRouter } from 'react-router'
import { ConfirmEmailChangePage, ConfirmEmailPage, ResendConfirmationPage } from '@/pages/auth/EmailPages'
import { LoginPage } from '@/pages/auth/LoginPage'
import { ForgotPasswordPage, ResetPasswordPage } from '@/pages/auth/PasswordPages'
import { RegisterPage } from '@/pages/auth/RegisterPage'
import { AccessDeniedPage, LockoutPage, NotFoundPage, RouteErrorPage } from '@/pages/auth/StatusPages'
import { TwoFactorPage } from '@/pages/auth/TwoFactorPage'
import { BrandingRoot } from '@/components/brand/BrandingRoot'
import { HomePage } from '@/pages/public/HomePage'
import { PrivacyPage } from '@/pages/public/PrivacyPage'

// Το console (admin + account) φορτώνεται μόνο όταν χρειαστεί: οι δημόσιες σελίδες και το login μένουν ελαφριές
function lazyPage<M>(load: () => Promise<M>, pick: (module: M) => ComponentType) {
  return async () => ({ Component: pick(await load()) })
}

const consoleLayout = () => import('@/components/layout/ConsoleLayout')

export const router = createBrowserRouter([
  {
    // Error boundary για όλο το app
    errorElement: <RouteErrorPage />,
    element: <BrandingRoot />,
    children: [
      // Δημόσιες σελίδες (φωτεινό θέμα)
      { path: '/', element: <HomePage /> },
      { path: '/privacy', element: <PrivacyPage /> },

      // Σελίδες λογαριασμού (φωτεινό AuthLayout)
      { path: '/login', element: <LoginPage /> },
      { path: '/login/2fa', element: <TwoFactorPage /> },
      { path: '/register', element: <RegisterPage /> },
      { path: '/confirm-email', element: <ConfirmEmailPage /> },
      { path: '/confirm-email-change', element: <ConfirmEmailChangePage /> },
      { path: '/resend-confirmation', element: <ResendConfirmationPage /> },
      { path: '/forgot-password', element: <ForgotPasswordPage /> },
      { path: '/reset-password', element: <ResetPasswordPage /> },
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
              { path: 'users', lazy: lazyPage(() => import('@/pages/admin/users/UsersPage'), (m) => m.UsersPage) },
              { path: 'users/:id', lazy: lazyPage(() => import('@/pages/admin/users/UserDetailPage'), (m) => m.UserDetailPage) },
              { path: 'roles', lazy: lazyPage(() => import('@/pages/admin/roles/RolesPage'), (m) => m.RolesPage) },
              { path: 'roles/:id', lazy: lazyPage(() => import('@/pages/admin/roles/RoleDetailPage'), (m) => m.RoleDetailPage) },
              { path: 'audit-logs', lazy: lazyPage(() => import('@/pages/admin/AuditLogsPage'), (m) => m.AuditLogsPage) },
              {
                path: 'settings',
                lazy: lazyPage(() => import('@/pages/admin/settings/SettingsLayout'), (m) => m.SettingsLayout),
                children: [
                  { index: true, lazy: lazyPage(() => import('@/pages/admin/settings/GeneralSettingsPage'), (m) => m.GeneralSettingsPage) },
                  { path: 'branding', lazy: lazyPage(() => import('@/pages/admin/settings/BrandingPage'), (m) => m.BrandingPage) },
                  { path: 'email', lazy: lazyPage(() => import('@/pages/admin/settings/EmailSettingsPage'), (m) => m.EmailSettingsPage) },
                ],
              },
              { path: 'media', lazy: lazyPage(() => import('@/pages/admin/media/MediaPage'), (m) => m.MediaPage) },
              { path: '*', element: <NotFoundPage /> },
            ],
          },
        ],
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
