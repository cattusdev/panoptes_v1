# Panoptes v1.0

An admin starter: ASP.NET Core 8 API with Identity and PostgreSQL, and a React + Tailwind + shadcn/ui front end.

- **Accounts:** created by an administrator (no public sign-up or self-service password reset), two-factor authentication (authenticator app + recovery codes), account lockout, profile photo, data export/deletion.
- **Admin console:** dashboard, users, roles with a permission hierarchy (nobody can grant more than they have), and an audit log of every change.
- **Media library:** safe uploads (images resized, metadata removed), folders, versions, trash, a reusable picker; stored locally or on Cloudflare R2.
- **Settings and languages:** general settings (default language, date and time format), branding, SMTP; English and Greek, per user.

## Requirements
- .NET 8 SDK, Node.js 22 or later (developed on 24), PostgreSQL
- To create new migrations: `dotnet tool install --global dotnet-ef`

## Run
Settings with passwords live in [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), not in `appsettings.json`.
```powershell
cd Panoptes_v1
dotnet user-secrets set ConnectionStrings:DefaultConnection "Host=localhost;Database=Panoptesv1;Username=postgres;Password=..."
dotnet run -- db:migrate                        # creates or updates the database
dotnet run -- admin:create you@example.com      # the first admin (asks for a password)
dotnet run -- roles:seed                        # the default roles: Manager, Staff, Finance, Support
dotnet run --launch-profile https
```
Open https://localhost:7770. The React app is built automatically the first time. After changing it, run `npm run build` in `Panoptes_v1.Client`, or `npm run dev` for hot reload at http://localhost:5173.

**Emails:** set up SMTP in the app (Settings → Email), or set `SmtpSettings:Server`, `Port`, `Security`, `SenderEmail`, `Username` and `Password` with `dotnet user-secrets set`. Without an SMTP server, Development writes emails to `Panoptes_v1/App_Data/emails` instead.

## Test
```powershell
dotnet test Panoptes_v1.E2E
```
End-to-end tests run the real app in a browser against a separate `Panoptesv1_E2E` database. See [Panoptes_v1.E2E/README.md](Panoptes_v1.E2E/README.md).

## Project layout
| Folder | What's inside |
|---|---|
| `Panoptes_v1` | API controllers, Identity, EF Core + migrations, audit logging, email, media |
| `Panoptes_v1.Client` | React app (pages, layouts, shadcn components, API hooks) |
| `Panoptes_v1.E2E` | Playwright end-to-end tests |
