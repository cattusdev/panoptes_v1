# Hankatt Operations: Boilerplate Capability Audit (Gate 1)

**Date:** 2026-11-02
**Scope:** read-only inspection of the Panoptes v1 repository: backend `Panoptes_v1/`, client `Panoptes_v1.Client/`, tests `Panoptes_v1.E2E/`, plus `CLAUDE.md` and `AGENTS.md`.
**Status:** for review. No code, dependency, migration or configuration was changed during the audit.

Paths are relative to the repository root. "Verdict" uses three values:

- **Reuse**: fit for purpose as-is.
- **Extend**: keep, but it needs changes before Hankatt modules can rely on it.
- **New**: no usable equivalent exists.

---

## 1. Stack confirmed

| Layer | Actual | Notes |
|---|---|---|
| Backend | ASP.NET Core 8 JSON API, EF Core with Npgsql (PostgreSQL), ASP.NET Identity, Serilog, MailKit, NetVips | `AddControllersWithViews` is used only for the global antiforgery filter; there are no Razor views (`Program.cs:54-57`). |
| Client | React 19, react-router 8 (`createBrowserRouter`, lazy console routes), TanStack Query 5, Tailwind 4 (CSS-first, `src/index.css`), shadcn/ui (`radix-nova`, 27 components), i18next (en/el, typed keys), FilePond, sonner | Built by Vite into `Panoptes_v1/wwwroot/spa`, served as the SPA fallback. |
| Tests | Playwright + NUnit E2E (about 130 tests) against the real app and a throwaway `Panoptesv1_E2E` database | No backend unit-test project and no client test runner. |
| Hosting | Self-hosted. Local files or Cloudflare R2/S3 for media; Data Protection keys in `App_Data/keys` | Fits PRD §1 (self-hostable, ASP.NET Core + React/TS + PostgreSQL). |

**Multi-tenancy:** none. The word "tenant" appears nowhere in the code. This matches the single-organization requirement (roadmap risk 6 is clear).

**Size:** about 5.5k lines of C# in the backend. The codebase is small and consistent, so changing it is cheap.

---

## 2. Capability and reuse matrix

### 2.1 Identity, permissions, authorization

| Capability | Evidence | Verdict | What Hankatt needs |
|---|---|---|---|
| Sign-in, 2FA, email confirmation, password reset, lockout, session re-validation every 60 s | `Program.cs:34`, `Api/AuthController.cs`, `Api/AccountController.cs` | **Reuse** | Nothing. |
| Users and roles admin, with escalation guard (`AdminGuard`: never manage or grant above your own permissions) | `Services/AdminGuard.cs`, `Api/UsersController.cs`, `Api/RolesController.cs` | **Reuse** | Nothing. |
| Global permissions as role claims (`Permission` type) | Nested constant classes in `Constants/Permissions.cs:45-81`, found by reflection; one policy per permission (`Program.cs:80-94`) | **Extend** | One class per new module (Clients, Projects, Tasks, Tickets, Finance, Subscriptions…). The file already expects this (`Permissions.cs:88`). Finance permissions must be kept separate (SEC-01). |
| Client permission mirror | `Panoptes_v1.Client/src/lib/permissions.ts`; labels in `src/i18n/locales/{en,el}/admin.json` (`modules`) | **Extend** | Kept in sync by hand. Acceptable, but add a test that compares the two lists. |
| One role per user | `UsersController.Update` removes all roles and adds one (`UsersController.cs:243-245`); DTOs use `roles.FirstOrDefault()` | **Reuse, with a note** | Enough for staff roles. Project-level access must come from **project membership**, not extra roles. |
| Per-record (resource) authorization | Only `Services/Media/MediaAccess.cs` (owner or `ManageAll`), checked in memory per item | **New** | `internal`/`restricted` projects with members, inherited by tasks, comments, files, reports, search, exports and widgets (PRD PRJ-06, SEC-01). Needs query-level filters (`IQueryable`), not checks after loading. |
| Route guards in the client | Only `RequireAdminArea` (`components/layout/ConsoleLayout.tsx`); individual pages rely on the API returning 403 | **Extend** | A `RequirePermission` route wrapper and a `useCan()` hook. These are a UX nicety only; the server must always enforce access. |
| Staff identity data | Stock `IdentityUser`; `UserProfile` has only `UserId`, `AvatarMediaId`, `Language` (`Models/ProfileAndBranding.cs:6-12`) | **Extend** | Display name (and possibly job title) for assignees, mentions and the activity timeline. |

### 2.2 Data, audit, consistency

| Capability | Evidence | Verdict | What Hankatt needs |
|---|---|---|---|
| Single `ApplicationDbContext`, fluent configuration inline in `OnModelCreating` | `Data/ApplicationDbContext.cs` | **Extend** | Move to `IEntityTypeConfiguration` classes per module (`ApplyConfigurationsFromAssembly`), or `OnModelCreating` becomes unmanageable. |
| Migrations and CLI | 6 migrations in `Migrations/`; `dotnet run -- db:migrate` (`Services/SetupCommands.cs`) | **Reuse** | Nothing. |
| Entity conventions | No base entity. Keys are a mix (Guid, int, string). `CreatedAt`/`UpdatedAt` are set by hand. Soft delete exists only as `MediaItem.DeletedAt`, filtered by hand everywhere. | **New** | Shared `IHasTimestamps`/`ISoftDelete` interfaces, a global soft-delete query filter, and business dates (`DateOnly`) and provenance kept separate from `CreatedAt` (Business rules C). |
| Concurrency control | None on app entities. No `xmin`/rowversion, no `DbUpdateConcurrencyException` handling. | **New** | Required for finance allocations, renewals and Kanban moves (FIN-03, NFR). |
| Audit log (field-level) | `SaveChangesAsync` override (`ApplicationDbContext.cs:103-226`) writes `AuditLogs` with user, table, type, primary key, old/new values and affected columns. `INotAudited` opt-out (`:126`). | **Extend** | See the gaps listed below the table. |
| Audit log UI | `/admin/audit-logs` list plus detail sheet (`pages/admin/AuditLogsPage.tsx`, `Api/AuditLogsController.cs`) | **Reuse** | Becomes the admin security/technical log (SEC-02). |
| Human-readable activity timeline | None | **New** | A per-record "who did what" feed for the Client 360° and task detail screens (SEC-02, Screen 2). |

Gaps in the audit log:

1. **Sync `SaveChanges()` is not overridden**, so any sync save skips the audit.
2. **Bulk `ExecuteUpdate`/`ExecuteDelete` bypass it** (today only on non-audited upload sessions).
3. **Excluded columns are matched by name across all tables.** The list at `ApplicationDbContext.cs:151` includes `PhoneNumber`, so a CRM contact's phone would never be audited.
4. **The user comes from the HttpContext only.** Background jobs and CLI work would show as anonymous.
5. **No trace id or IP is recorded.**
6. **The second save for generated keys runs outside a transaction.**
7. **`AuditLogs` has no indexes**, and its values are stored as `text`, not `jsonb`.

### 2.3 Background processing and idempotency

| Capability | Evidence | Verdict | What Hankatt needs |
|---|---|---|---|
| Hosted services | One: `MediaCleanupService` (`Program.cs:134`; `Services/Media/MediaCleanup.cs:224-244`): a `PeriodicTimer`, a scope per run, logged errors, and a static `SemaphoreSlim` so two runs never overlap (`:27`) | **Reuse the pattern** | Good template for a job runner's loop. |
| Durable jobs, scheduling, retries, outbox | None (no Hangfire/Quartz, no queue table) | **New** (decided: own Postgres job/outbox table) | Renewals, reminders, recurring tasks, automation delays and email sending must be durable, retryable, cancellable, deduplicated and inspectable (WRK-06, AUT-02/03, Business rules E). |
| Idempotency | Only the chunked upload protocol (offset-based, returns 409 on a mismatch; `Api/MediaUploadsController.cs`) | **New** | Unique idempotency keys on jobs, renewal periods and generated charges (acceptance scenario 18). |
| Multi-instance safety | Settings cached in process memory for 10 min (`Services/Settings/SettingsService.cs:21`); upload locks in memory (`Services/Media/MediaDelivery.cs:65`); static cleanup gate | **Accept for v1** | A single instance is fine for an internal app. The job runner must still use database locking (`SKIP LOCKED`) so a later second instance stays safe. |

### 2.4 Settings, localization, time

| Capability | Evidence | Verdict | What Hankatt needs |
|---|---|---|---|
| Settings framework: one JSON row per section; secrets encrypted with Data Protection; values audited, secrets never | `Services/Settings/SettingsService.cs`, `Services/Settings/Sections.cs`, `ISettingsSection` | **Reuse** | New sections need no migration: financial tracking cutoff (ONB-06), project status names (PRJ-02), notification defaults. A cutoff change after live activity needs an extra guard (ONB-09). |
| Settings UI (vertical section nav, registry) | `pages/admin/settings/sections.ts`, `components/admin/SettingsNav.tsx`, `SettingsCard` | **Reuse** | Nothing. |
| Greek/English: client namespaces typed against `en`; server `IStringLocalizer` plus `SharedResource.el.resx`; per-user, visitor and app-default language; emails in the recipient's language | `src/i18n/`, `Resources/`, `Services/Localization/AppLanguages.cs` | **Reuse** | One namespace per module (`crm`, `projects`, `finance`…). Meets the NFR "Greek/English-ready". |
| Date display following settings (format, 12/24h, time zone) | `components/admin/DateTime.tsx`, `lib/dates.ts` | **Reuse** | Nothing. |
| Business time zone on the server | `GeneralSettings.TimeZone` is display-only, validated only by a regex (`Api/SettingsController.cs:85`) | **New** | Renewal periods, due dates and "today" must be calculated in Europe/Athens on the server (NFR timezone semantics). Store UTC instants, and keep full-day dates as `DateOnly`. |

### 2.5 Email and notifications

| Capability | Evidence | Verdict | What Hankatt needs |
|---|---|---|---|
| Sending: MailKit with a configurable transport (pickup directory → Settings SMTP → user-secrets SMTP → dev folder), test send | `Services/EmailSender.cs` | **Reuse** | Nothing for the transport itself. |
| Account emails (inline-CSS HTML layout, localized) | `Services/AccountEmails.cs` | **Extend** | Links come from the current request (`AccountEmails.cs:66`), so they **cannot be built from a background job**. Needs a configured public base URL. Sending is synchronous, with no retry. |
| Email templates with variables, previews, test sends | None (C# raw strings only) | **New, later** | Gate 5 automation (AUT-02). Not needed for the MVP's first slices. |
| In-app notifications, per-user preferences, mentions | None | **New** | MVP needs at least assignment/due notifications later; mentions must check access before revealing anything (COL-01). |

### 2.6 Files and media

| Capability | Evidence | Verdict | What Hankatt needs |
|---|---|---|---|
| Ingest: content sniffing, limits, zip-bomb checks, image re-encoding and variants, metadata stripping | `Services/Media/MediaService.cs`, `FileSniffer.cs`, `MediaOptions.cs` | **Reuse** | Already accepts PDF, docx/xlsx/pptx, odt/ods/odp, txt, csv, zip. Enough for AADE invoice PDFs and contracts. |
| Storage: local or R2/S3 (public and private buckets); private files always stream through the app | `Services/Media/MediaStorage.cs`, `S3MediaStorage.cs` | **Reuse** | Nothing. Streaming through the app is what makes access checks possible. |
| Resumable chunked upload (`/api/media/uploads`) and `MediaUploader` (FilePond) | `Api/MediaUploadsController.cs`, `components/media/MediaUploader.tsx` | **Extend** | Accept an "attach to record" target so that uploading from a record's Files tab creates the link in the same transaction. |
| Versions, trash, restore | `MediaItem`/`MediaVersion` in `Models/Media/MediaEntities.cs`; `MediaService` | **Extend** | Already covers DOC-01's version history and recycle bin. Version pruning (`MaxVersions=10`) must not delete versions that a record refers to. |
| Nested folders | `MediaFolder`, up to 10 levels | **Reuse, with a note** | One global tree with no folder ACL. Folder grants may only **narrow** access (DOC-02). |
| Access control | `MediaAccess`: anyone with `Media.View` sees **every** library file, **every** user's trash, all folders and their counts; public files are served with no check at all | **Extend (critical)** | Access based on what the file is attached to: the most restrictive link wins, and attached files may never be made public. The check must cover list, search, trash, details, versions, folder counts and delivery (DOC-02, acceptance scenarios 12 and 14). |
| Linking files to records | `MediaUsage` (`Services/Media/MediaUsage.cs:22`) is a hard-coded check for branding logo and favicon only; avatars are tracked separately | **New** | A `MediaAttachment` table (one file, many records), with usage providers that guard trash, purge, empty trash, upload revert and folder delete. |
| Media UI: browser, picker, field, details sheet with versions, thumbnails | `components/media/*` | **Reuse in parts** | `MediaPicker`, `MediaUploader`, `MediaDetailsSheet` and `MediaThumb` work inside a record's Files tab. `MediaBrowser` is tied to the global library and should not be embedded there. |

### 2.7 Client UI system

| Capability | Evidence | Verdict | What Hankatt needs |
|---|---|---|---|
| Design tokens: light public theme; dark console with mauve primary; status colours; tinted badges | `src/index.css`, `components/ui/badge.tsx` (success, warning, info, purple, brand, muted, dot) | **Reuse** | A shared `statusVariant()` map, because each page currently defines its own (`AuditLogsPage.tsx:16`, `DashboardPage.tsx:27`). Add warning and info `Alert` variants. A few hard-coded hex colours (`ConsoleLayout.tsx:70`, `Common.tsx:26`) should become tokens. |
| Console shell: floating sidebar with mini/full modes, mobile overlay, topbar with user menu | `components/layout/ConsoleLayout.tsx`, `components/layout/AdminSidebar.tsx` | **Extend** | The nav is a static array with **closed** label and group unions. There are no collapsible groups, badge counts, breadcrumbs or global search in the topbar, and the footer has a hard-coded `v2.3` (`AdminSidebar.tsx:128`). Screen planning needs 6 groups. |
| Page patterns: `PageHeader` (with back), `RouteTabs`, detail page made of `SettingsCard`s, `DangerZone`, `ConfirmDialog`, side `Sheet`, unsaved-changes bar | `components/admin/*`; `pages/admin/users/*`, `roles/RoleDetailPage.tsx`, `AuditLogsPage.tsx` | **Reuse** | The Users list and detail is the template for every new module. `RouteTabs` with nested routes (as in `AccountLayout.tsx`) is the basis for the Client 360° tabs. |
| `DataTable` and `TablePagination` | `components/admin/DataTable.tsx`: display only, with skeleton and empty states | **Extend** | Sorting, row selection with bulk actions, column visibility and page size (decided: `@tanstack/react-table` behind the same look). Copy-pasted URL filter code should become a `useUrlFilters` hook. |
| Forms | Plain `useState` plus shadcn `Field`; server errors via `fieldErrors()` (`lib/api.ts`); only `PasswordInput`, `CodeInput`, `PasswordRequirements` | **Extend** | Decided: react-hook-form + zod. Missing pieces: money input, date picker, combobox, multi-select, field arrays, a wizard stepper and drafts. |
| API layer | `lib/api.ts` (CSRF, Accept-Language, ProblemDetails helpers, `Paged<T>`); per-area hook files | **Reuse** | Add a query-key factory convention. Optimistic updates (Kanban) are not used anywhere yet. |
| Dashboard | `pages/admin/DashboardPage.tsx` with fixed system metrics; `Api/DashboardController.cs` | **New (replace content)** | Business widgets, role defaults, saved layouts, permission-safe data (UI-01). There is no real `StatCard`: `StatCard.tsx` only exports `IconTile`. |
| Charts, Kanban drag and drop, command palette, calendar | None (chart tokens exist but are unused) | **New** (decided: shadcn chart/Recharts, @dnd-kit, shadcn command/cmdk, calendar/popover) | — |
| PWA | No manifest, no service worker | **New, later** | UI-05. Must respect the `/spa/` base path and the backend SPA fallback. |

### 2.8 API conventions and errors

| Capability | Evidence | Verdict | Notes |
|---|---|---|---|
| `[ApiController]` controllers, record DTOs, DataAnnotations plus localized messages, `ValidationProblem` | `Api/*.cs`, `Services/Localization/ValidationLocalization.cs` | **Reuse** | Complex finance validation will live in services, not attributes. |
| Paging: offset `PagedResult<T>` and `Paging.Normalize`; cursor paging in media | `Api/ApiContracts.cs`, `Api/MediaController.cs:285` | **Extend** | Filters and sort are hand-written per endpoint. A shared list helper (sort whitelist, ILike escaping) avoids repeating this in about 10 new list endpoints. |
| Error handling: `GlobalExceptionHandler` returns ProblemDetails with a trace id and never leaks internals | `Middlewares/GlobalExceptionHandler.cs` | **Reuse** | Add a mapping from concurrency conflicts to 409. |
| Antiforgery on every state-changing request | `Program.cs:56`, `lib/api.ts` | **Reuse** | — |
| OpenAPI / codegen | None | **Leave as is** | DTOs are written by hand on the client. Acceptable at this size. |

### 2.9 Testing

| Capability | Evidence | Verdict | Notes |
|---|---|---|---|
| E2E harness: builds the client, resets the database (refuses unless the name contains `e2e`/`test`), seeds around 28 scenario users, launches the real app, mailbox via pickup directory, test endpoints | `Panoptes_v1.E2E/AppFixture.cs`, `TestData.cs`, `E2ETest`, `MediaApiTests.cs` (`MediaTest` helpers) | **Reuse** | Perfect for permission, workflow and historical onboarding scenarios (NFR testing). |
| Unit/integration tests for domain invariants | None | **New (recommended)** | The finance and renewal invariants (no double counting, idempotent renewals) are best tested quickly against PostgreSQL without a browser. Proposed in Gate 2. |

---

## 3. Technical risks (ranked)

1. **Restricted-project data leaking through files.** Today `Media.View` reveals the whole library, all trash and folder counts, and public files bypass every check. Attaching business files without the planned access model would break acceptance scenarios 12 and 14. *Mitigation:* attachment-based access applied at query level, no public visibility for attached files, and E2E tests for each way a file can be reached.
2. **Duplicate side effects from retries.** There is no job store, no idempotency keys and no locking. *Mitigation:* the job/outbox table with unique keys, unique constraints on renewal periods and generated charges, and rechecking conditions before a delayed action runs.
3. **Lost updates on money and boards.** There are no concurrency tokens. *Mitigation:* `xmin` concurrency tokens, a 409 response the UI explains, and serialized allocation writes.
4. **Gaps in the audit trail.** CRM phone numbers would go unaudited, and system and job actions show as anonymous. *Mitigation:* per-table exclusions, an actor context, a transaction around the two-phase save, and indexes on the audit table.
5. **Background emails impossible as built.** Account emails derive links from the request. *Mitigation:* a configured public base URL and a send-email job.
6. **Time zone ambiguity in renewals.** The server has no business-zone logic. *Mitigation:* a business clock using Europe/Athens, with `DateOnly` for coverage periods.
7. **UI drift.** Missing primitives (forms, tables, status semantics) tempt every module to build its own. *Mitigation:* build them once in Slice 1, before any business screen.
8. **Single-instance assumptions** (memory cache, locks). Low risk for an internal deployment. Documented, and the job runner uses database locks.

### 3.1 Status after the safety fixes (2026-11-02)

After Gate 1 was approved, risks 1–6 were fixed in the boilerplate, each with E2E coverage. `MediaAttachmentTests`, `BackgroundJobTests` and `DataIntegrityTests` are new, and `MediaLibraryTests` has a new test.

| # | Risk | What changed | Where |
|---|---|---|---|
| 1 | Restricted-project file leaks | New `MediaAttachments` table (one file, many records) and one visibility filter, `VisibleTo`, used by every media query: list, search, trash, details, versions, folder counts, file delivery, replace and branding lookups. The strictest link wins: project boundary plus optional required permission. Project access fails closed (only Admin) until the Projects module exists. Attached files can't be made public, trashed, purged, undone, auto-purged, or deleted along with a folder. | `Services/Media/MediaAttachments.cs`, `Services/Access/ProjectAccess.cs`, `Services/Media/MediaUsage.cs`, `Api/Media*Controller.cs` |
| 2 | Duplicate side effects | Durable job queue with unique idempotency keys (`ON CONFLICT DO NOTHING`), outbox-style enqueue inside the open transaction, `SKIP LOCKED` claiming, leases, retries with backoff, failure inspection, and admin retry/cancel at `/api/jobs`. Email job included. | `Services/Jobs/*`, `Api/JobsController.cs` |
| 3 | Lost updates | `xmin` row versions on media items and folders, profiles, branding and settings. Conflicts return a localized 409, never a 500. | `Data/ApplicationDbContext.cs`, `Middlewares/GlobalExceptionHandler.cs` |
| 4 | Audit gaps | Sync and async saves are both audited, in one transaction. Exclusions are per entity type (a client's phone is audited, an account phone isn't), with `[AuditIgnore]`. Keys generated by the database (e.g. role-permission rows) are now recorded after the save; previously they were missing from the log. `Source` (jobs, CLI, cleanup) and `TraceId` columns, shown in the audit log UI. Indexes added. | `Data/ApplicationDbContext.cs`, `Data/AuditContext.cs`, `Models/AuditEntry.cs` |
| 5 | No background emails | `AppUrls` uses `App:PublicBaseUrl`, with the request as a fallback; a warning is logged in production if it's missing, which also prevents Host-header poisoning of reset links. Job-based `EnqueueEmailAsync`. | `Services/AppUrls.cs`, `Services/Jobs/SendEmailJob.cs` |
| 6 | Time zone ambiguity | `BusinessClock` (Settings time zone, else Europe/Athens; DST-aware start of day). The time zone setting is validated against real zones. | `Services/BusinessClock.cs`, `Api/SettingsController.cs` |

Also fixed while testing: in the media folder tree, a long folder name overflowed the side panel and covered the file grid (`components/media/FolderTree.tsx`).

Still open by design:
- **Risk 7** (UI building blocks) comes before the first business screen.
- **Risk 8** (single instance) is accepted; the job queue is already safe with multiple instances.
- **Real project membership** replaces `AdminOnlyProjectAccess` in the Projects slice.

---

## 4. Suggested adoption path

1. **Keep the boilerplate as the application shell.** Don't fork the identity, settings, media or E2E layers. Extend them in place, following the existing conventions in `AGENTS.md`: Greek comments, single quotes and no semicolons in the client, all text through `t()`, dates through `<DateTime>`.
2. **Slice 1 (foundations), before any business module:**
   - entity conventions, concurrency and audit hardening;
   - the activity timeline;
   - the job/outbox runner;
   - background-safe email;
   - staff display name;
   - the business clock;
   - the list query helper;
   - client form kit, DataTable v2, status semantics, nav registry with collapsible groups, `RequirePermission`, command palette.
3. **Then business slices,** each gated by its architecture section and UX brief: CRM → projects/tasks (with record access) → onboarding wizard → finance → subscriptions/renewals → tickets, record files and dashboard.
4. **The media access rework** (attachments, query-level access, usage guards) lands with the first module that attaches files, and it must be finished before any restricted project goes live.

---

## 5. Out of scope for this audit
- Performance, load or security penetration testing.
- Verifying AADE timologio integration feasibility (PRD §H; remains a later decision).
- Backup and restore tooling. None exists in the repo; it will be proposed in Gate 2.
