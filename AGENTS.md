# Agent quick start

Read this first, then [CLAUDE.md](CLAUDE.md) for the architecture, UX rules and security rules.

## What this is
Panoptes_v1: an ASP.NET Core 8 JSON API (Identity, PostgreSQL, EF Core) with a React + Tailwind + shadcn/ui client. It covers accounts, an admin console and a media library.

| Folder | What's inside |
|---|---|
| `Panoptes_v1/` | Backend: `Api/` controllers, `Services/`, `Data/` (DbContext and audit log), `Migrations/` |
| `Panoptes_v1.Client/` | React app, built into `Panoptes_v1/wwwroot/spa` |
| `Panoptes_v1.E2E/` | Playwright + NUnit tests against the real app |

## Commands
Run these from the repository root.

| Task | Command |
|---|---|
| Run the app | `cd Panoptes_v1; dotnet run --launch-profile https` (opens https://localhost:7770) |
| Create or update the database | `cd Panoptes_v1; dotnet run -- db:migrate` |
| Create the first admin | `cd Panoptes_v1; dotnet run -- admin:create you@example.com` |
| Create the default roles | `cd Panoptes_v1; dotnet run -- roles:seed` |
| Build the client after changing it | `cd Panoptes_v1.Client; npm run build` |
| Hot reload the client | `cd Panoptes_v1.Client; npm run dev` |
| Lint the client | `cd Panoptes_v1.Client; npm run lint` |
| Run the tests | `dotnet test Panoptes_v1.E2E` |
| Add a migration | `dotnet ef migrations add <Name> --project Panoptes_v1` |

The tests need PostgreSQL and the connection string in user secrets. They also have two opt-in groups:
- `--filter "Category=Screenshots"` for screenshots;
- `--filter "Category=S3"` for S3 storage (needs Docker).

## Before you call work done
- Every feature or fix gets an E2E test in `Panoptes_v1.E2E`.
- `dotnet test Panoptes_v1.E2E` passes.
- `dotnet build` shows 0 warnings.
- The client builds and lints.

## Traps
- **A running app locks the build.** "File is locked by Panoptes_v1" means the app is running: stop it before building or testing.
- **Secrets live only in user secrets:** `cd Panoptes_v1; dotnet user-secrets list`. Never put passwords or keys in `appsettings.json` or in commits.
- **Never touch the dev database from tests or scripts.** The E2E database (`Panoptesv1_E2E`) is wiped on every run, and its name must contain `e2e` or `test`. Before running anything that writes, check which database the connection string points at.
- **Don't run Prettier.** The project has no Prettier config, so it rewrites the style of whole files. Follow the existing style instead: single quotes, no semicolons in TypeScript, Greek code comments.
- **Use existing shared pieces before building new ones:**
  - shadcn components in `src/components/ui` (`npx shadcn@latest add <name>`);
  - shared admin pieces in `src/components/admin`;
  - media pieces in `src/components/media`.

  The admin sidebar is our own component, not shadcn's.
- **Emails:** without an SMTP server, Development writes them to `Panoptes_v1/App_Data/emails`. The tests use `Email:PickupDirectory`. Locally, point `SmtpSettings` (user secrets) or Settings → Email at Mailpit (`localhost:1025`, security `None`) and read them at http://localhost:8025.
- **Dates:** show them with `<DateTime>` / `useDateFormat()`, never `toLocaleString()`, so they follow Settings → General.
- **Text:** every UI string goes through `t()` with an entry in both `src/i18n/locales/en` and `el` (`tsc` fails if Greek is missing); server messages through `IStringLocalizer<SharedResource>` plus a Greek entry in `Resources/SharedResource.el.resx`. Keep the English wording stable: the E2E tests match on it.
- **Files on records:** never query `MediaItems` for users without `MediaAttachments.VisibleTo(...)`, and never let an attached file become public. Files in restricted projects must stay invisible everywhere.
- **Side effects (emails, charges, renewals, reminders):** go through `JobQueue` with a deterministic idempotency key, never fire-and-forget. Handlers re-check that the work is still needed.
- **Audit:** code without an HTTP request wraps its work in `AuditContext.Use("<area>:<what>")`. Don't use `ExecuteUpdate`/`ExecuteDelete` on audited tables. New mutable entities get a `RowVersion` (`IsRowVersion()`).
- **Running the tests while the app runs:** `dotnet test Panoptes_v1.E2E -c Release` builds to `bin/Release`, so it doesn't hit the locked Debug build.
- **Git:** commit or push only when the user asks.
