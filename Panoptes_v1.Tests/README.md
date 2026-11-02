# Panoptes_v1.Tests

Domain and architecture tests (docs/ARCHITECTURE.md §12): the real services against a real PostgreSQL database, with no HTTP and no browser. The whole suite runs in a few seconds.

## Run

```powershell
dotnet test Panoptes_v1.Tests
```

While the app is running (its Debug build is locked), use `dotnet test Panoptes_v1.Tests -c Release`.

## What happens on each run

1. The database `Panoptesv1_Test` is **dropped and recreated** from the migrations. It uses the same server and credentials as the app: `appsettings.json`, then user secrets. Your normal database is never touched. The fixture refuses to reset any database whose name does not contain `e2e` or `test`. Set `TEST_CONNECTION_STRING` to use a different one.
2. Each test builds its own service provider (`DomainTest`) with the real `ApplicationDbContext`, `SettingsService`, `BusinessClock`, `ReferenceGenerator`, `JobQueue`/`JobProcessor` (+ `TestJob`) and Identity. The clock is a `TestClock` the test sets.

## Folders

| Folder | Covers |
|---|---|
| `Foundations/` | References (gapless, rollback, business year), business dates (time zone, DST), audit log, timestamps, concurrency, the list helper |
| `Jobs/` | Job queue and runner: idempotency, outbox, retries and backoff, lease expiry, parallel runners |
| `Architecture/` | Reflection and source rules: client/server permissions match, `RowVersion` = `xmin`, every endpoint declares its access, paged endpoints take a `ListRequest`, `ILike` passes the escape character, module boundaries |

## Adding tests

- Inherit from `DomainTest`. Use `WithScopeAsync`/`WithDbAsync` for each unit of work (a fresh `DbContext`), `SignIn(userId)` to act as a user (it returns the request's trace id, handy for finding this test's audit rows), and `AuditContext.Use(...)` to act as a job or CLI.
- The database is shared by every test in the run: use unique names and keys (`Unique("prefix")`), and restore any setting you change.
- Business modules add **one test per acceptance scenario (S1–S20)** that is backend logic, next to their code's folder name (e.g. `Finance/`).
- `ModuleBoundaryTests` is skipped until the first `Panoptes_v1.Modules.*` namespace exists; then it checks that modules don't reference each other's entities.
