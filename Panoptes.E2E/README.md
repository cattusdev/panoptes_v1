# Panoptes.E2E

End-to-end tests: real app, real PostgreSQL, real browser (Playwright + NUnit).

## Run

```powershell
dotnet build
# one-time (or after a Playwright version bump): download Chromium
powershell -ExecutionPolicy Bypass -File Panoptes.E2E/bin/Debug/net8.0/playwright.ps1 install chromium
dotnet test Panoptes.E2E
```

Or use the Test Explorer in Visual Studio.

## What happens on each run

0. The React client is rebuilt (`npm run build` in `Panoptes.Client`) so tests always run the current UI. Set `E2E_SKIP_CLIENT_BUILD=1` to skip this when only backend code changed.
1. The database `Panoptes_E2E` is **dropped and recreated** from the migrations (same server and credentials as `appsettings.json`). Your normal `Panoptes` database is never touched; the fixture refuses to reset any database whose name does not contain `e2e` or `test`. Set `E2E_CONNECTION_STRING` to use a different one.
2. Known roles and users are seeded (see `TestData.cs`; every user's password is `E2e-Passw0rd!`).
3. Emails are not sent: the app writes them to `%TEMP%\Panoptes.E2E\mail` (`Mailbox.cs`), and tests follow the real links (confirm email, reset password, change email). `Totp.Code(key)` generates authenticator codes for the 2FA tests.
4. The app is started with `dotnet run` on a free port (Development, logs in `%TEMP%\Panoptes.E2E`), and stopped at the end.

## Debugging

- Watch the browser: `$env:HEADED=1; dotnet test Panoptes.E2E`
- Step slowly: `$env:PWDEBUG=1; dotnet test Panoptes.E2E`

## Adding tests

Inherit from `E2ETest`, use `LoginAsync(TestData.<User>)`, and add a dedicated seed user in `TestData.cs` when a test changes that user's state.
