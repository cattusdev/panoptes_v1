# Agent quick start

Read this first, then [CLAUDE.md](CLAUDE.md) for the architecture, UX rules and security rules.

## What this is
Panoptes: an ASP.NET Core 8 JSON API (Identity, PostgreSQL, EF Core) with a React + Tailwind + shadcn/ui client. It covers accounts, an admin console and a media library.

| Folder | What's inside |
|---|---|
| `Panoptes/` | Backend: `Api/` controllers, `Services/`, `Data/` (DbContext and audit log), `Migrations/` |
| `Panoptes.Client/` | React app, built into `Panoptes/wwwroot/spa` |
| `Panoptes.E2E/` | Playwright + NUnit tests against the real app |

<!-- template -->
## This copy can seed new projects
`init.ps1` at the root renames a fresh copy into a new project (see README). In this repo:
- Don't rename things by hand. `Panoptes` becomes the C# name and `panoptes` the kebab-case name. The database name and the app name people see are found by the regex rules at the top of `init.ps1` (`Database=Panoptes`, `Panoptes_E2E`, `<title>`, `DefaultAppName`, `appName`, `SenderName`, the authenticator issuer). Write new occurrences in one of those forms (or reference `BrandingSettings.DefaultAppName`), or add a rule.
- If you change `init.ps1`, keep it working in Windows PowerShell 5.1: no `??` or ternary, and ASCII only. On `DbConnectionStringBuilder`, call `get_/set_ConnectionString()`, because `.ConnectionString` is read as a dictionary key.
- Test it on a throwaway copy with a throwaway database, never on this folder (it refuses to run while `origin` is the Panoptes repository).

<!-- /template -->
## Commands
Run these from the repository root.

| Task | Command |
|---|---|
| Run the app | `cd Panoptes; dotnet run --launch-profile https` (opens https://localhost:7222) |
| Create or update the database | `cd Panoptes; dotnet run -- db:migrate` |
| Create the first admin | `cd Panoptes; dotnet run -- admin:create you@example.com` |
| Build the client after changing it | `cd Panoptes.Client; npm run build` |
| Hot reload the client | `cd Panoptes.Client; npm run dev` |
| Lint the client | `cd Panoptes.Client; npm run lint` |
| Run the tests | `dotnet test Panoptes.E2E` |
| Add a migration | `dotnet ef migrations add <Name> --project Panoptes` |

The tests need PostgreSQL and the connection string in user secrets. They also have two opt-in groups:
- `--filter "Category=Screenshots"` for screenshots;
- `--filter "Category=S3"` for S3 storage (needs Docker).

## Before you call work done
- Every feature or fix gets an E2E test in `Panoptes.E2E`.
- `dotnet test Panoptes.E2E` passes.
- `dotnet build` shows 0 warnings.
- The client builds and lints.

## Traps
- **A running app locks the build.** "File is locked by Panoptes" means the app is running: stop it before building or testing.
- **Secrets live only in user secrets:** `cd Panoptes; dotnet user-secrets list`. Never put passwords or keys in `appsettings.json` or in commits.
- **Never touch the dev database from tests or scripts.** The E2E database (`Panoptes_E2E`) is wiped on every run, and its name must contain `e2e` or `test`. Before running anything that writes, check which database the connection string points at.
- **Don't run Prettier.** The project has no Prettier config, so it rewrites the style of whole files. Follow the existing style instead: single quotes, no semicolons in TypeScript, Greek code comments.
- **Use existing shared pieces before building new ones:**
  - shadcn components in `src/components/ui` (`npx shadcn@latest add <name>`);
  - shared admin pieces in `src/components/admin`;
  - media pieces in `src/components/media`.

  The admin sidebar is our own component, not shadcn's.
- **Emails:** without an SMTP server, Development writes them to `Panoptes/App_Data/emails`. The tests use `Email:PickupDirectory`. Locally, point `SmtpSettings` (user secrets) or Settings → Email at Mailpit (`localhost:1025`, security `None`) and read them at http://localhost:8025.
- **Dates:** show them with `<DateTime>` / `useDateFormat()`, never `toLocaleString()`, so they follow Settings → General.
- **Text:** every UI string goes through `t()` with an entry in both `src/i18n/locales/en` and `el` (`tsc` fails if Greek is missing); server messages through `IStringLocalizer<SharedResource>` plus a Greek entry in `Resources/SharedResource.el.resx`. Keep the English wording stable: the E2E tests match on it.
- **Git:** commit or push only when the user asks.
