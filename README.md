# Panoptes

An admin starter: ASP.NET Core 8 API with Identity and PostgreSQL, and a React + Tailwind + shadcn/ui front end.

- **Accounts:** sign-up with email confirmation, password reset, two-factor authentication (authenticator app + recovery codes), account lockout, profile photo, data export/deletion.
- **Admin console:** dashboard, users, roles with a permission hierarchy (nobody can grant more than they have), and an audit log of every change.
- **Media library:** safe uploads (images resized, metadata removed), folders, versions, trash, a reusable picker; stored locally or on Cloudflare R2.
- **Settings and languages:** general settings (default language, date and time format), branding, SMTP; English and Greek, per user.

<!-- template -->
## Start a new project from Panoptes
1. On GitHub: **Use this template → Create a new repository** (turn on *Template repository* in this repo's settings once). Or clone this repo into a new folder and point `origin` at the new repository.
2. Run the init script in the new folder:
   ```powershell
   git clone https://github.com/<owner>/<new-repo>.git
   cd <new-repo>
   ./init.ps1
   ```
   If Windows blocks the script: `powershell -ExecutionPolicy Bypass -File .\init.ps1`. On macOS/Linux: `pwsh ./init.ps1`.
3. `git push`.

The script asks for a project name (e.g. `Acme.Portal`) and an app name (e.g. `Acme Portal`), then:
- renames the solution, projects, folders, namespaces, database names and app name;
- gives the project its own user-secrets id and ports;
- stores the database connection (and optionally SMTP) in user secrets, creates the database and the first admin;
- deletes itself and commits.

It refuses to run in a copy whose `origin` is still the Panoptes repository. `./init.ps1 -Name Acme.Portal -SkipSetup` only renames. `Get-Help ./init.ps1 -Detailed` lists every option.

**Keeping it renameable:** `Panoptes` becomes the C# name and `panoptes` the kebab-case name (storage keys, npm package, file names). The database name (`Database=Panoptes`, `Panoptes_E2E`) and the app name people see (`<title>`, `DefaultAppName`, `appName`, `SenderName`, the authenticator issuer) are found by the rules at the top of `init.ps1`: write new occurrences in one of those forms, or reference `BrandingSettings.DefaultAppName` / `defaultBranding.appName`.

<!-- /template -->

## Requirements
- .NET 8 SDK, Node.js 22 or later (developed on 24), PostgreSQL
- To create new migrations: `dotnet tool install --global dotnet-ef`

## Run
Settings with passwords live in [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), not in `appsettings.json`.
```powershell
cd Panoptes
dotnet user-secrets set ConnectionStrings:DefaultConnection "Host=localhost;Database=Panoptes;Username=postgres;Password=..."
dotnet run -- db:migrate                        # creates or updates the database
dotnet run -- admin:create you@example.com      # the first admin (asks for a password)
dotnet run --launch-profile https
```
Open https://localhost:7222. The React app is built automatically the first time. After changing it, run `npm run build` in `Panoptes.Client`, or `npm run dev` for hot reload at http://localhost:5173.

**Emails:** set up SMTP in the app (Settings → Email), or set `SmtpSettings:Server`, `Port`, `Security`, `SenderEmail`, `Username` and `Password` with `dotnet user-secrets set`. Without an SMTP server, Development writes emails to `Panoptes/App_Data/emails` instead.

## Test
```powershell
dotnet test Panoptes.E2E
```
End-to-end tests run the real app in a browser against a separate `Panoptes_E2E` database. See [Panoptes.E2E/README.md](Panoptes.E2E/README.md).

## Project layout
| Folder | What's inside |
|---|---|
| `Panoptes` | API controllers, Identity, EF Core + migrations, audit logging, email, media |
| `Panoptes.Client` | React app (pages, layouts, shadcn components, API hooks) |
| `Panoptes.E2E` | Playwright end-to-end tests |
