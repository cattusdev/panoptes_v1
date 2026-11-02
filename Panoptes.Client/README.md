# Panoptes.Client

React 19 + TypeScript + Vite, Tailwind CSS v4 and shadcn/ui (Radix, "nova" style). Data fetching with TanStack Query, routing with React Router.

```powershell
npm install
npm run dev     # http://localhost:5173, proxies /api to https://localhost:7222 (BACKEND_URL to change)
npm run build   # type-check + build into ../Panoptes/wwwroot/spa
npm run lint
```

## Where things are
- `src/router.tsx` all routes (console routes are lazy-loaded)
- `src/pages/` `public/`, `auth/`, `account/`, `admin/`
- `src/components/ui/` shadcn components (`npx shadcn@latest add <name>`); we own and may extend them (e.g. badge/alert status variants)
- `src/components/admin/`, `src/components/form/`, `src/components/layout/` shared building blocks
- `src/lib/api.ts` fetch wrapper (JSON, CSRF header, ProblemDetails helpers); `src/lib/*-api.ts` typed hooks per area
- `src/index.css` theme tokens: light (`:root`) for public/auth pages, `.dark` for the console
