# Hankatt Operations: Architecture & Domain Design (Gate 2)

**Status:** approved on 2026-11-02: D1–D14 as written, Q1–Q7 and self-registration answered (§16). Nothing here is implemented yet; next is Gate 3 (screen briefs).
**Inputs:** `PRODUCT-REQUIREMENTS.md` (PRD), `BUSINESS-RULES.md` (BR), `IMPLEMENTATION-ROADMAP.md`, `BOILERPLATE-AUDIT-REPORT.md`.
**How to review:**
- §15 lists the decisions to approve (D1–D14) and the questions only you can answer (Q1–Q7).
- Everything else explains *why* those decisions are proposed.
- Acceptance scenarios from PRD §14 are referenced as **S1–S20**.

---

## 1. Shape of the system

**A modular monolith inside the existing Panoptes v1 project.** One ASP.NET Core app, one PostgreSQL database, one React client. No microservices, no second database, no tenant layer.

Why:
- It's a single internal organization with a small team.
- The boilerplate already gives us identity, audit, settings, media, jobs and E2E tests in one process.
- Strong consistency between finance, renewals and work is far easier in one database transaction.

### 1.1 Modules

| Module | Owns | Depends on |
|---|---|---|
| **Crm** | Clients (billing entities), people, relationships, addresses, tags | — |
| **Projects** | Projects, statuses, members, milestones, related-project links | Crm |
| **Work** | Tasks, subtasks, checklists, assignees, boards, time entries, timers | Projects, Crm |
| **Support** | Tickets, ticket messages, conversion to tasks | Work, Crm, Projects |
| **Finance** | Charges, payments, allocations, credits, adjustments, external invoice refs, expenses, opening balances | Crm (+ reads Projects/Work/Subscriptions for links) |
| **Subscriptions** | Service catalog, bundles, subscriptions, periods, components, fulfillment | Crm, Finance |
| **Onboarding** | Drafts and the commit of the historical/new-work wizard | all of the above, through their services |
| **Collaboration** | Notes, comments, mentions, activity timeline, notifications, action center | all (by `EntityType`/`EntityId`) |
| **Shared** (exists) | Identity, permissions, audit, settings, media and attachments, jobs, email, business clock | — |

Quotes and change requests, the automation wizard, campaigns and advanced reporting are **Gate 5**. The event and job design below leaves room for them without rework.

### 1.2 Code layout (backend)

```
Panoptes_v1/
  Modules/
    Crm/        Entities/  Configuration/  Services/  Api/  CrmModule.cs
    Projects/   ...
    Work/  Support/  Finance/  Subscriptions/  Onboarding/  Collaboration/
  Api/ Services/ Data/ ...      (existing shared boilerplate, unchanged location)
```

- **Entities** are plain classes.
- **Configuration** holds one `IEntityTypeConfiguration<T>` per entity. `ApplicationDbContext` calls `ApplyConfigurationsFromAssembly`, and the existing inline config stays as-is.
- **Services** hold all business rules and transactions. Controllers stay thin: they map requests to service calls and return DTOs.
- **`XxxModule.cs`** contains `AddXxxModule(this IServiceCollection)`, called from `Program.cs`. It registers services, job handlers and settings sections.
- **One `ApplicationDbContext`, one migration stream.** Every slice adds additive migrations.

**Module rules** are enforced by an architecture test (§12):
1. A module may read another module's tables only through that module's **service or query interface**. Example: Finance calls `IProjectLookup.GetSummariesAsync(ids)`, not `db.Projects`.
2. No module writes another module's tables. Cross-module effects go through a service call within the same transaction, or a domain event (§9).
3. Navigation properties are allowed only *within* a module. Cross-module references are plain IDs, such as `Guid ClientId`.

### 1.3 Client layout

```
Panoptes_v1.Client/src/
  modules/<module>/   pages/  components/  api.ts  (query keys + hooks)
  i18n/locales/{en,el}/<module>.json
  components/ ...     (existing shared UI; Slice 1 adds the form kit, DataTable v2, StatusBadge, nav registry)
```

**Routes (decided, Q3):**
- Business modules live at top level inside `ConsoleLayout`: `/dashboard`, `/clients`, `/projects`, `/tasks`, `/tickets`, `/finance`, `/services`, `/files`, `/notes`.
- `/admin/*` stays for administration: users, roles, settings, audit, jobs.
- `/` goes to `/dashboard` when signed in, to `/login` otherwise.
- The public pages (home, privacy) and self-service password reset are removed.

### 1.4 API conventions

- Routes are `/api/<module>/<resource>`, for example `/api/crm/clients` and `/api/finance/payments`.
- **Lists** use the shared list helper planned in Slice 1:
  - offset paging;
  - whitelisted `sort`;
  - typed filters;
  - every query already passed through record access (§3).
- **DTOs carry `version`** (the `RowVersion`). Updates send it back, and a stale version returns 409 with "Someone else changed this…". This extends the existing `xmin` protection to the browser, which matters for Kanban moves, charge edits and ticket assignment.
- **DTOs carry capability flags** (`canEdit`, `canDelete`, `canRecordPayment`…). The client shows or hides actions from these flags and never computes permissions itself.
- **Business errors** are localized `ProblemDetails`, with a stable `code` extension (for example `finance.overAllocation`) for the UI and tests.
- **Bulk operations** are explicit endpoints with all-or-nothing semantics. They have no per-row partial success, except imports, which report per-row results.

---

## 2. Shared conventions for all business entities

| Concern | Convention |
|---|---|
| **Keys** | `Guid` (as in media). |
| **Human references** | Readable codes such as `CL-0042`, `PRJ-2026-007`, `TCK-1203`, `CHG-2026-0153` (format: Q2). Generated from a `Sequences` table (`Name`, `NextValue`) updated with a row lock inside the creating transaction, so codes have no gaps from races and no duplicates. Unique index per entity. |
| **Timestamps** | `CreatedAt`, `CreatedById`, `UpdatedAt`, `UpdatedById` (`IHasTimestamps`), set by `ApplicationDbContext` on save. These record *when it was typed in*, never business time. |
| **Business dates** | `DateOnly` for whole days (due date, completion date, service period, invoice date), in the business time zone (`BusinessClock`). `DateTime` (UTC, `timestamptz`) only for instants (timer start, payment received at…). |
| **Provenance** | `Origin` enum (`Live`, `Historical`, `Import`), plus `SourceRef` (e.g. `onboarding:{draftId}`, `import:{batchId}:{row}`), on every business root. **`Historical`/`Import` records never trigger side effects** (§9.3). |
| **Archive vs delete** | **Archive** (`ArchivedAt`) is a business state. The record stays searchable and reportable, and nothing is cancelled (BR A, CRM-06, PRJ-08). **Delete** (`DeletedAt`, recycle bin) exists only where the PRD asks for it (files, notes, drafts) and for records created by mistake with no financial links. A global query filter hides `DeletedAt` rows; archived rows are filtered by the UI, not globally. |
| **Concurrency** | `RowVersion` (`xmin`) on every mutable root, sent to the client as `version` (§1.4). |
| **Money** | Owned type `Money(decimal Amount, string Currency)`: `numeric(18,2)` plus `char(3)`. Rounded half-away-from-zero, per line. Currency is required on every money row (FIN-07). Aggregates group by currency; no conversion in v1. |
| **Enums** | Stored as strings (readable SQL and audit, safe reordering). |
| **Text search** | `pg_trgm` extension with GIN trigram indexes on names and references, for search and duplicate warnings. |
| **Audit** | Field-level audit already works. Business services also write **activity events** (§9.2) for the human-readable timeline. |

---

## 3. Authorization model

### 3.1 Two layers

1. **Global permissions (what kind of thing you may do).** Role claims, exactly as today. New permission classes are added in `Constants/Permissions.cs` and mirrored in `src/lib/permissions.ts`; a test asserts the two lists match.
2. **Record access (which records you may see).** One `IRecordAccess` service per module, exposing `IQueryable` filters that every list, detail, search, export, widget, notification and file check uses. It is never a check done in memory after loading.

### 3.2 Proposed permissions

| Module | Permissions |
|---|---|
| Clients | `Clients.View`, `Clients.Create`, `Clients.Edit`, `Clients.Archive` |
| Projects | `Projects.View`, `Projects.Create`, `Projects.Edit`, `Projects.Archive`, `Projects.ManageMembers` |
| Tasks | `Tasks.View`, `Tasks.Create`, `Tasks.Edit`, `Tasks.Delete`, `Tasks.ViewAllTime` (others' time logs) |
| Tickets | `Tickets.View`, `Tickets.Create`, `Tickets.Edit`, `Tickets.SendExternal` (customer-facing messages) |
| Finance | `Finance.View`, `Finance.Record` (charges, payments, allocations), `Finance.Adjust` (adjustments, voids, write-offs), `Finance.Expenses`, `Finance.Settings` (cutoff) |
| Subscriptions | `Subscriptions.View`, `Subscriptions.Manage`, `Subscriptions.OverrideDates` (early/late renewal exceptions) |
| Onboarding | `Onboarding.Use` (wizard); the commit also requires the create permissions of what it creates |
| Notes | `Notes.View`, `Notes.Edit` |

Finance is deliberately separate (SEC-01). A project manager without `Finance.View` sees *that* a task is linked to a charge, but not the amounts.

### 3.3 Project visibility (PRJ-06, BR F)

- `Project.Visibility` is `Internal` or `Restricted`.
- A `ProjectMember` row holds `ProjectId`, `UserId` and `Role` (`Lead`, `Member`, `Viewer`).
- **Internal:** visible to anyone with `Projects.View`.
- **Restricted:** visible only to its members and Admin. No permission overrides this; that's the point of "restricted".
- `ProjectAccess` replaces today's `AdminOnlyProjectAccess`:

  ```
  VisibleProjects(user) = Admin ? Everything
                        : Only(projects where Visibility = Internal AND user has Projects.View
                               OR exists member row for user)
  ```

**Inheritance:**
- Tasks, subtasks, checklists, time entries, comments, notes, tickets and file attachments with a `ProjectId` are visible only if the project is visible.
- Records without a project (client tasks, internal tasks, client-level tickets) follow the module permission alone.
- File attachments already work this way: `MediaAttachment.ProjectId` and "the strictest link wins".

**Finance and restricted projects (Q1):** the proposal is that users with `Finance.View` see every charge and payment, because a client's balance must always be complete and correct (CRM-02). Project and task links on those rows are **masked** ("Restricted project") for non-members. Without this, a finance user would see a client balance that doesn't add up.

### 3.4 Where it is enforced

| Surface | Mechanism |
|---|---|
| Lists, details, boards, calendars | Module query starts from `IRecordAccess.Visible<T>(user)` |
| Global search, command palette | Each module's search provider applies its own filter (§10) |
| Dashboard widgets, reports, exports | Widgets and report datasets are server queries using the same filters (§11) |
| Files | `MediaAttachments.VisibleTo` (exists) |
| Notifications, mentions, emails | Access re-checked **when delivered**; a mention of someone without access is refused before saving (COL-01) |
| Client UI | Uses capability flags only; hiding a button is never the security boundary |

**Permission-matrix tests.** For each module, an E2E test logs in as `outsider`, `member` and `admin` against a restricted project. It asserts that the list, detail, search, widget, export and file surfaces return nothing or 404 for the outsider (S14, S19).

---

## 4. Domain model by module

Only the fields that matter for behavior are listed. Every root also has the shared fields from §2.

### 4.1 CRM

- **Client**: the *legal billing entity* (CRM-01/02).
  - Fields: `Ref`, `Kind` (`Organization`/`Individual`), `LegalName`, `DisplayName`, `TaxId` (ΑΦΜ), `TaxOffice` (ΔΟΥ), `Status` (`Active`/`Inactive`/`Archived`), `DefaultCurrency` (EUR), `Tags`, `Addresses` (owned list: billing, office…), `ContactMethods` (owned: emails, phones, website).
  - **Every balance belongs to exactly one Client.** Two companies owned by the same person are two Clients with two balances (BR A).
- **Person**: a human contact. `FirstName`, `LastName`, `ContactMethods`, `Notes`.
- **ClientContact**: the relationship. `ClientId`, `PersonId`, `Role` (free text plus suggestions), `IsPrimary`, `IsBillingContact`, `StartedOn`/`EndedOn`. One person can belong to many clients, and debts are never combined (CRM-01).
- **Duplicate detection:**
  - same normalized `TaxId` gives a **hard** warning (requires confirmation);
  - trigram similarity on name ≥ 0.6, or the same email/phone, gives a soft warning.
  - It runs on create, in the onboarding wizard and in imports.

### 4.2 Projects

- **Project**:
  - `Ref`, `ClientId?` (null = internal project, BR A), `Name`, `Type` (configurable list), `StatusId`, `Visibility`;
  - real dates `StartedOn`, `DueOn`, `CompletedOn` (business dates, may be in the past);
  - `Origin`, `ArchivedAt`, `TemplateId?`.
- **ProjectStatus** (configured in Settings, PRJ-02): `Name` (en/el), `Category` (`Planned`, `Active`, `OnHold`, `Completed`, `Cancelled`), `Order`, `IsDefault`. Reports and automations use the **category**, never the name.
- **ProjectMember**: see §3.3.
- **Milestone**: `ProjectId`, `Name`, `DueOn`, `CompletedOn`, `Order`. A grouping of tasks (`Task.MilestoneId`), **not** a progress unit of its own (§4.3).
- **ProjectLink**: `ProjectId`, `RelatedProjectId`, `Kind` (`Related`, `FollowUp`, `Replaces`). A flat graph, no hierarchy (PRJ-04).
- **Completion** sets `CompletedOn` and the status category. It does **not** touch charges or subscriptions (PRJ-08). Post-completion steps (handover checklist, service activation) are explicit actions offered to the user, not automatic.

### 4.3 Work

- **Task**:
  - `Ref`, `ProjectId?`, `ClientId?` (client task without project, or derived from the project), `ParentTaskId?`, `MilestoneId?`, `Title`, `Description` (sanitized rich text), `StatusId`, `Priority`;
  - `StartOn`, `DueOn`, `CompletedOn` (business date, editable for historical work), `EstimateMinutes?`, `BoardRank`, `Origin`.
  - **One level of subtasks** (WRK-02): a task with a `ParentTaskId` cannot have children. Enforced in the service and by a database trigger-free rule (a check in the same transaction plus a test).
- **TaskStatus**: one shared workflow at first (WRK-02), configured like project statuses. Categories are `Todo`, `InProgress`, `Blocked`, `Done`, `Cancelled`. Allowed transitions are a server-side table (`TaskStatusTransition`), validated on every change, including drag and drop (WRK-04).
- **TaskAssignee**: `TaskId`, `UserId`, `IsPrimary`. A unique partial index on `(TaskId) WHERE IsPrimary` allows only one primary (BR A).
- **ChecklistItem**: `TaskId`, `Text`, `DoneAt`, `Order`.
- **TaskDependency** (optional in v1): `TaskId`, `DependsOnTaskId`, with a cycle check.
- **Board ordering**: `BoardRank` is a fractional string rank (LexoRank style). A move updates one row, sent with `version`, so concurrent moves return 409 instead of silently reordering.
- **TimeEntry**: `UserId`, `TaskId?`, `ProjectId?`, `StartedAt`, `EndedAt`, `Minutes`, `Billable`, `Note`, `Source` (`Manual`/`Timer`).
- **ActiveTimer**: primary key `UserId`, which enforces **at most one running timer per user** in the database (WRK-05). Pause and resume close and open time entries. Stop converts the timer to a final entry. All of this happens on the server, so closing the browser loses nothing.
- **Time is not cost**: time entries never create expenses or charges by themselves (BR D).

**Progress (PRJ-05, BR H). Proposal:**

```
task.completion = Done or Cancelled? 1 (Cancelled tasks are excluded from the denominator)
                : has subtasks? done subtasks / non-cancelled subtasks
                : 0
task.weight     = EstimateMinutes ?? 1         (top-level tasks only)
project.progress = Σ(weight × completion) / Σ(weight), over non-cancelled top-level tasks
milestone.progress = same formula, over its top-level tasks
```

- Subtasks count *inside* their parent and never again on their own, and milestones are only views over tasks, so nothing is counted twice.
- A historical project with no tasks shows its status category (Completed = 100%) instead of a misleading 0%.

### 4.4 Support

- **Ticket**:
  - `Ref`, `ClientId?`, `RequesterPersonId?`, `ProjectId?`, `SubscriptionId?`, `Subject`, `Priority`, `AssigneeId?`;
  - `Status` (`New`, `Open`, `Pending`, `Resolved`, `Closed`);
  - `Resolution` (`Fixed`, `Answered`, `Converted`, `Duplicate`, `WontFix`), `ConvertedTaskId?`, `Channel` (`Manual` now; `Email` later, SUP-01).
- **TicketMessage**: `TicketId`, `Kind` (`InternalNote`, `Outbound`, `Inbound`), `Body`, `AuthorId`, `SentAt?`, `Delivery` (`NotSent`, `Queued`, `Sent`, `Failed`), plus attachments.
  - Internal notes and outbound messages are **two different actions with two different buttons and endpoints**. An internal note can never be sent: there is no "change kind" operation (SUP-03).
  - Outbound messages go through the job queue (idempotency key `ticket-message:{id}`).
- **Convert to task** (S15) is one transaction:
  1. create the task, linked to the ticket's client and project, with ticket attachments linked (not copied);
  2. set the ticket to `Closed` with `Resolution = Converted` and `ConvertedTaskId`;
  3. write activity on both.
  The task stays open.

### 4.5 Collaboration

- **Note**: `EntityType`, `EntityId`, `ProjectId?` (inheritance), `CategoryId`, `Title`, `Body` (sanitized HTML), `DeletedAt` (recycle bin). Each save writes a **NoteVersion** (DOC-03).
- **Comment**: `EntityType`, `EntityId`, `ProjectId?`, `ParentCommentId?` (threads), `Body`, `Mentions` (user IDs), `EditedAt`, `DeletedAt`.
- **ActivityEvent**: `EntityType`, `EntityId`, `ProjectId?`, `Verb` (e.g. `task.statusChanged`), `ActorId`/`Source`, `Data` (jsonb with the i18n params), `OccurredAt`. Written by services in the same transaction. This is the Client 360° and task "Activity" tab. Read access goes through the same project filter.
- **Notification** and **NotificationPreference**: see §9.4.

---

## 5. Finance ledger

This is the part where mistakes cost real money, so the rules are spelled out and each one is mapped to a scenario.

### 5.1 Concepts (FIN-01)

| Concept | Entity | Is it money owed? | Is it cash? |
|---|---|---|---|
| Agreement / agreed price | `Agreement` (optional), price snapshots on subscriptions | no | no |
| Receivable | **`Charge`** (+ `ChargeLine`) | **yes** | no |
| Expected installment | `PaymentScheduleItem` on a charge | no (forecast only) | no |
| Cash received | **`Payment`** | no | **yes**, on `ReceivedOn` |
| Applying cash to debt | **`Allocation`** (payment or credit → charge) | reduces it | no |
| Credit note / goodwill | `Credit` (allocatable like a payment) | reduces it | no |
| Refund | `Refund` (linked to a payment) | — | yes (outgoing) |
| Correction | **`Adjustment`** (increase, decrease, write-off, void) | changes it | no |
| Official invoice | `ExternalInvoice` (AADE number, date, MARK, PDF) | no (metadata) | no |
| Historical declaration | fields on an opening charge (§5.3) | no | **no** |
| Expense | `Expense` | no | yes (outgoing, on its date) |

### 5.2 Charges

- **Charge**:
  - `Ref`, `ClientId` (exactly one billing entity), `IssuedOn`, `DueOn`, `Currency`;
  - `Origin` (`Live` or `Opening`), `State` (`Draft`, `Final`, `Void`), `SourceKey?` (unique, for generated charges, e.g. `subscription-period:{id}`), `Notes`.
- **ChargeLine**: `Description`, `Quantity`, `UnitPrice`, `DiscountPercent`, `VatRate?`, `LineNet`, `LineVat`, `LineTotal`; optional links `ProjectId?`, `TaskId?`, `SubscriptionPeriodId?`, `ChangeRequestId?`.
- **ChargeCoverage**: `ChargeId`, `EntityType` (`Task`/`Project`), `EntityId`. It says "this charge covers that work" **with no amount**, so several historic tasks can point to one existing bill without duplicating money (S5, BR D).
- **Draft** charges can be edited freely. **Final** charges are immutable: any change is an `Adjustment`, and the original stays visible (S17, FIN-04).
- **Balance of a charge:**

  ```
  total(lines) + Σ adjustments − Σ allocations (payments + credits)
  ```

  It is computed, not stored. A `ChargeBalance` SQL view (or materialized query) serves lists and reports.
- **Derived status:** `Open`, `PartiallyPaid`, `Paid`, `Overdue` (`DueOn < BusinessClock.Today` and balance > 0), or `Void`. Never typed in by hand.

### 5.3 Opening (historical) receivables (ONB-04/05/06, BR C)

An **opening charge** is a `Charge` with `Origin = Opening`. **Its amount is what was still owed at the tracking start**, nothing more. It also carries optional, nullable *declaration* fields:

- `DeclaredAgreedAmount?`: what the work was originally agreed for, if known;
- `DeclaredPaidBeforeCutoff?`: what was received before tracking started, if known;
- `DeclaredOn?`: the original bill date, if known.

`null` always means **unknown**, never zero (ONB-05). The declared amounts are reported as **historical declarations**, never as cash or as billed revenue (FIN-08).

| Scenario | How it is recorded | Why it can't double count |
|---|---|---|
| **S2** completed Aug 2026 task, €400, unpaid | Task `Completed`, `CompletedOn = 2026-08-…`, `Origin = Historical`. One opening charge of €400 with a `ChargeLine.TaskId` (or coverage) → task. | The client, project and task views all *read* the same charge balance; nothing sums task prices. |
| **S3** €1,000 work, €700 received before cutoff, €300 due | Opening charge €300; `DeclaredAgreedAmount = 1000`, `DeclaredPaidBeforeCutoff = 700`. | €700 is a declaration field, not a `Payment`, so cash flow can't see it. |
| **S4** only the outstanding balance is known | Opening charge €X, declaration fields `null` (shown as "Unknown"). | — |
| **S5** two historic tasks under one existing bill | One charge plus two `ChargeCoverage` rows. | Amount lives once on the charge. |
| **S6** €400 received after cutoff for an old debt | `Payment` (`ReceivedOn` = the actual date) plus `Allocation` → the opening charge. | Cash counted once, on its real date; the task's completion date is untouched. |
| **S1** archived, fully paid 2024 project | Project and tasks `Historical` and completed. No charge at all, or (if wanted) an opening charge of €0 with declarations. | Nothing produces revenue, invoices or messages. |
| **S20** no old emails or cash movements | Historical/opening records are excluded from event side effects (§9.3) and from cash reports by definition. | Enforced in code and tested. |

### 5.4 Tracking start (cutoff) (ONB-06/09, BR C)

- **Setting:** `FinanceSettings.TrackingStartDate` (`DateOnly`, edited by `Finance.Settings`). Initial value: **2026-12-01** (Q5).
- **Rules:**
  - Live payments must have `ReceivedOn ≥ TrackingStartDate`.
  - Opening charges describe the state *at* the cutoff.
  - Live charges are dated on or after it.
- **Changing it after live activity exists (proposal):**
  1. `GET /api/finance/cutoff/preview?date=` lists every record that would end up on the wrong side: live payments or charges before the new date, opening charges after it.
  2. The change is **refused** while any such record exists. The preview tells the user exactly which records to reclassify, and each reclassification is its own audited adjustment.
  3. When nothing conflicts, the change is saved with a reason and an audit entry.

  No silent reclassification, ever.

### 5.5 Payments, allocations, credits (FIN-02/03, S16)

- **Payment**: `ClientId`, `ReceivedOn`, `Amount`, `Currency`, `Method` (bank, card, cash…), `Reference`, `Notes`, `State` (`Recorded`, `Reversed`). A reversal is a linked reversing entry, never a delete.
- **Allocation**: `PaymentId?` or `CreditId?`, `ChargeId`, `Amount`, `AllocatedOn`.
- **Invariants**, checked in one transaction that locks the payment and the charges (`SELECT … FOR UPDATE`):
  - Σ allocations of a payment ≤ payment amount;
  - Σ allocations to a charge ≤ charge total + adjustments (no over-allocation; the UI warns before submitting, and the server refuses with `finance.overAllocation`);
  - the currencies of the payment and the charge match.
- **Unapplied money:**
  - the unallocated part of a payment is the client's **unapplied payment**, shown separately: "€150 received, not yet applied". This is how deposits and overpayments work without fake charges.
  - The client's net position is `Σ charge balances − unapplied payments − unused credits`, always displayed as its parts.
- **Payment entry UX** (Screen planning §6): pick a client, enter the amount, and the open charges are suggested oldest first. The user can redistribute amounts across charges, and any remainder stays unapplied.

### 5.6 External invoices (FIN-05)

- **ExternalInvoice**: `Provider` (default "AADE timologio"), `Number`, `IssuedOn`, `Mark?` (ΜΑΡΚ), `NetAmount?`, `VatAmount?`, `TotalAmount?`, `PdfMediaId?` (attached private file, `RequiredPermission = Finance.View`), `Notes`.
- Linked to charges many-to-many (`ExternalInvoiceCharge`).
- The UI labels it "Invoice reference (issued in AADE)". The app never says *it* issued an invoice.
- A read-only myDATA sync is a later, separately verified option (PRD §H).

### 5.7 Expenses (FIN-06)

- **Expense**: `SpentOn`, `Amount`, `Currency`, `Category`, `Supplier`, `ProjectId?`, `SubscriptionId?` (e.g. the registrar cost of a domain), `ReceiptMediaId?`, `RecurringTemplateId?`.
- Reports show "direct costs" and "expenses", never "profit" (FIN-06).

### 5.8 Report definitions (FIN-08)

These are fixed definitions, so every widget and report means the same thing.

| Metric | Definition |
|---|---|
| **Billed** | Σ final **live** charge totals (+ adjustments) by `IssuedOn` |
| **Collected** | Σ payments by `ReceivedOn` (− refunds by refund date) |
| **Outstanding** | Σ charge balances (live + opening) at a date |
| **Opening balance (historical)** | Σ opening charge amounts (what was owed at cutoff) |
| **Historical declared** | Σ declaration fields, labelled "declared, not tracked" |
| **Forecast** | Upcoming subscription periods × snapshot price + open payment-schedule items |
| **Expenses** | Σ expenses by `SpentOn` |

All metrics are per currency.

### 5.9 VAT (decided, Q4)

- Each line stores net, VAT rate and gross, so receivables match what the AADE invoice will show even though fiscal invoicing is external.
- A new line starts with `FinanceSettings.DefaultVatRate`: 24%, editable in Settings → Finance by `Finance.Settings`. The rate can also be changed per line (e.g. 0% for exempt work).
- Changing the default never touches existing lines: each line keeps the rate it was created with.

---

## 6. Subscriptions and renewals (SUB, BR E)

### 6.1 Model

- **ServiceCatalogItem**: `Name` (en/el), `Kind` (`Hosting`, `Domain`, `Maintenance`, `Email`, `Other`), `DefaultPrice`, `DefaultCycleMonths`, `DefaultProvider`, `IsActive`. These are **defaults only**. Changing them never rewrites existing agreements (SUB-01).
- **CatalogBundle** and **CatalogBundleItem**: package templates.
- **Subscription**: one client agreement.
  - `Ref`, `ClientId`, `ProjectId?` (optional, SUB-08), `Name`, `Status` (`Active`, `Suspended`, `Cancelled`, `Ended`).
  - `CycleMonths`, `Price` (snapshot), `ChargePolicy` (`Manual`, `AutoDraft`), `GraceDays`, `ReminderOffsetsDays` (e.g. `[30, 7, 0]`), `LateRenewalPolicy` (`PreserveCadence`, `NewCycleFromEffectiveDate`).
  - `StartedOn` (may be historical), `RemindersActive` (false for imported subscriptions until reviewed, ONB-07/08), `Origin`.
- **SubscriptionComponent**: the parts with their **own real-world expiry** (SUB-02). Examples: a domain inside a hosting bundle.
  - Fields: `Kind`, `Name` (e.g. `hankatt.gr`), `Provider`, `ProviderRef`, `ExternalExpiresOn` (registrar or provider date), `OwnReminderOffsets?`.
- **SubscriptionPeriod**: the renewal unit.
  - Fields: `SubscriptionId`, `PeriodStart`, `PeriodEnd` (`DateOnly`, inclusive), `Price` (snapshot), `PaymentDueOn`.
  - **`UNIQUE(SubscriptionId, PeriodStart)`**, so a scheduler re-run can't create a second period (SUB-08, S18).
  - Three **independent** dimensions (SUB-04, BR E):

| Dimension | Values | Changed by |
|---|---|---|
| **Agreement** | `Pending`, `Approved`, `Declined` | **Admin only** records the client's approval or refusal (Q7); no auto-approval in v1 |
| **Payment** | derived from the linked charge's balance: `NotCharged`, `Unpaid`, `Partial`, `Paid` | Finance |
| **Fulfillment** (per component) | `Pending`, `Done`, `NotNeeded` + `FulfilledOn`, `ProviderRef` | User confirms "renewed at the registrar" |

A domain can be agreed and paid while the registrar renewal is still pending (S10). The UI always shows all three dimensions.

### 6.2 Rules

- **Period dates** (BR E, SUB-05/06):

  ```
  next.PeriodStart = previous.PeriodEnd + 1 day        (default: early renewal keeps remaining days, S8)
  ```

  - **Late renewal:** with `PreserveCadence` the start date stays as above. With `NewCycleFromEffectiveDate` the start date is a date the user chooses (S9).
  - **Override:** any other start date needs `Subscriptions.OverrideDates` and a reason, which is audited.
- **Charge creation is a separate action.** "Create charge for this period" (or the `AutoDraft` policy) creates a draft charge with `SourceKey = subscription-period:{id}`. The unique key means a retry returns the existing charge. Creating the charge does **not** extend coverage and does **not** mark fulfillment (SUB-04).
- **What "covered until" means:** the last period with `Agreement = Approved`. Provider expiry is shown separately and is never inferred.
- **Bundles:** one subscription, one charge per period. Each component keeps its own `ExternalExpiresOn` and reminders (S11).
- **Price changes** (SUB-07): a `SubscriptionPriceChange` (`EffectiveFrom`, `NewPrice`, `Reason`) applies to periods starting on or after that date. A preview lists the affected subscriptions and the next amounts. Past periods keep their snapshot.
- **Suspension** (SUB-03): a status plus a reminder workflow only. It **never** switches anything off in production in v1.

### 6.3 Scheduler (idempotent)

One daily job, `subscriptions.daily`, runs per subscription with idempotency key `subscriptions.daily:{date}` and fans out. Every key below is deterministic, so a re-run or a crash mid-run does nothing twice (S18).

| Step | Idempotency key | Re-check at run time |
|---|---|---|
| Create the next period N days before `PeriodEnd` | `UNIQUE(SubscriptionId, PeriodStart)` | Subscription still `Active` |
| Reminder email/notification at each offset | `reminder:{periodId}:{offset}:{channel}` | Still not approved/paid/fulfilled? Subscription active? `RemindersActive`? Offset date not in the past at activation? |
| Component expiry reminder | `component-reminder:{componentId}:{expiresOn}:{offset}` | Expiry unchanged, not yet fulfilled |
| Auto-draft charge (policy) | `Charge.SourceKey` | Period approved, no charge yet |

Imported or historical subscriptions get **no reminders for past dates, ever** (S7, ONB-07). When the user turns on `RemindersActive` after review, a preview shows exactly which future reminders will be scheduled.

---

## 7. Historical onboarding wizard (ONB, Screen 3)

- **Drafts:** `OnboardingDraft` (`OwnerId`, `Path` (`New`, `InProgress`, `Historical`), `Step`, `Data` jsonb, `UpdatedAt`, `RowVersion`, `CommittedAt?`, `ResultRefs` jsonb). Drafts are saved on the server on every step change, can be resumed from any device, and appear in a "Drafts" list.
- **Validation:** each step validates on the server when it is saved, with the same rules as the real create endpoints, so errors appear on the step and not only at the end.
- **Preview:** `POST /api/onboarding/{id}/preview` is a **dry run** inside a transaction that is rolled back. It returns:
  - the records to create, grouped (client, project, tasks, opening charges, subscriptions, files);
  - **duplicate warnings** (§4.1, plus same client + project name, plus a similar opening charge: same client, amount and date);
  - **side effects**, which always include "No messages, charges or automations for past dates", the list of future reminders that will be scheduled only if activated, and any new receivables with their totals.
- **Commit:** `POST /api/onboarding/{id}/commit` runs **one transaction** through the module services, with `Origin = Historical` and `SourceRef = onboarding:{draftId}`.
  - It is idempotent on the draft: a second commit returns the stored `ResultRefs`.
  - It is all-or-nothing, so a failure leaves nothing half-created.
- **Corrections after commit:** done with normal audited operations (edit the project, void or adjust the opening charge). There is no "undo the whole onboarding" button in v1; it would have to reverse payments made since.
- **Unknown values** stay unknown: "Unknown" is an explicit choice in the money fields, distinct from 0.
- The **CSV import wizard** (IMP-01) reuses the same preview, duplicate detection, commit and provenance services with `Origin = Import`. It is a later slice.

---

## 8. Files and notes

- **Already built:** `MediaAttachment` (one file, many records, strictest link wins, no public visibility, no trash while linked).
- Each module's "Files" tab:
  - `GET/POST/DELETE /api/<module>/<record>/files`;
  - the module checks *its own* record access first, then creates the link with that record's `ProjectId` (and `RequiredPermission` for finance documents);
  - uploads from the tab attach in the same request (an `attachTo` field on the upload start, planned in the audit).
- **Folder restrictions** (DOC-02, later): `MediaFolderRestriction` (`FolderId`, `RequiredPermission?`, member list?). These are **AND**-ed with the attachment rules, so a folder can only narrow access, never widen it.
- **Version pruning** (`MaxVersions`) skips files linked to finance records (invoices, receipts), so a fiscal PDF's history is never lost.

---

## 9. Events, side effects and notifications

### 9.1 Domain events through the outbox

- Services raise domain events such as `task.completed`, `payment.recorded`, `period.approved` and `ticket.converted`.
- Each event is written as a job (`event:{name}`, idempotency key `event:{eventId}`) **in the same transaction** as the change (the job queue's outbox behaviour).
- Event handlers then run in the background with retries.
- The Gate 5 automation engine subscribes to the same events, so nothing has to be redesigned later.

### 9.2 Activity timeline

Activity events (§4.5) are written synchronously in the service transaction, not through jobs, so the timeline is never behind.

### 9.3 Historical suppression (ONB-07, BR C, S1/S7/S20)

- Every event carries the `Origin` of the record that caused it.
- Side-effecting handlers (emails, notifications to clients, automations, charge generation) **ignore `Historical`/`Import` events**.
- Only the activity timeline records them ("Imported via onboarding").
- A test enqueues a full historical onboarding and asserts that the job table holds **zero** side-effect jobs.

### 9.4 Notifications (G, COL-01)

- **Notification**: `UserId`, `Kind`, `EntityType`, `EntityId`, `TitleKey` + `Params`, `CreatedAt`, `ReadAt`.
- **NotificationPreference**: `UserId`, `Kind`, `InApp`, `Email`.
- The job that delivers a notification **re-checks record access** for the recipient. No access means the notification is dropped and logged.
- **Mentions:** the editor offers only users who can see the record, and the server refuses mentions of anyone else.

### 9.5 Action Center (UI-03)

- **Not a feed.** It's a set of server queries for "things that need a decision", each one access-filtered:
  - overdue tasks I own;
  - unassigned tickets;
  - overdue charges;
  - periods awaiting approval;
  - pending fulfillment;
  - expiring components.
- `ActionSnooze` (`UserId`, `ItemKey`, `Until`) hides an item until a date.

---

## 10. Search, saved views, command palette

- **Global search:** `GET /api/search?q=` fans out to registered module search providers (`ISearchProvider`).
  - Each provider uses trigram and reference matching *on its own access-filtered query*, then returns typed hits (`client`, `project`, `task`…) with up to 5 per group.
  - The command palette (Ctrl+K) uses the same endpoint and adds navigation commands.
- **Saved views** (WRK-04, UI-02): `SavedView` (`OwnerId`, `Module`, `Name`, `Filters` jsonb, `IsShared`). A shared view saves only the *filter definition*; results are always computed for the viewer.

---

## 11. Dashboard and reports

- **Widgets are server definitions** (`IDashboardWidget`: `Key`, `RequiredPermission`, `QueryAsync(user, params)`).
  - The client only lays them out.
  - A widget whose permission the user lacks is not offered, and if it's in a saved layout it renders "not available".
- **Layouts:** `DashboardLayout` (`OwnerId?`, `RoleId?`, `Layout` jsonb). Role defaults apply first; a user can save a personal layout and reset to the default (UI-01).
- **Reports** (RPT-01, mostly Gate 5):
  - fixed, named datasets built on the §5.8 definitions, with filters;
  - no arbitrary SQL;
  - exports run the same query as the screen.

---

## 12. Testing strategy

| Layer | Tool | Covers |
|---|---|---|
| **Domain integration tests** (new project `Panoptes_v1.Tests`) | NUnit against a real throwaway PostgreSQL database (`Panoptesv1_Test`, same name guard as E2E) | Ledger invariants, allocation concurrency, period idempotency, progress formula, cutoff guard, onboarding commit. **One test per acceptance scenario (S1–S20)** where it's backend logic. Fast, no browser. |
| **E2E** (existing) | Playwright + NUnit | Every screen and flow, the permission matrix (§3.4), usability scenarios from UX-DESIGN-STANDARD. |
| **Architecture tests** | Reflection-based NUnit test in `Panoptes_v1.Tests` | No module references another module's entities; every list endpoint of a business module goes through `IRecordAccess`; client and server permission lists match. |

Definition of done per slice (roadmap Gate 4): migration, backend, UI, permission checks, both test layers, and the UX review.

---

## 13. Data migration and rollback

- **Additive first:** new tables and nullable columns. Destructive changes use expand → migrate data → contract over two releases.
- **Reviewed SQL:** for every migration, `dotnet ef migrations script` is generated and reviewed before it reaches production (as we did for the `xmin` columns).
- **Production rollout:**
  1. back up the database and media storage;
  2. run `db:migrate`;
  3. deploy;
  4. smoke-test.

  **Rollback** means restoring the backup and redeploying the previous build. `Down` migrations are kept, but we don't rely on them for production.
- **Import or onboarding mistakes** are fixed with audited corrections, not by editing the database.

---

## 14. Operations

- **Backups (proposal, NFR):**
  - nightly `pg_dump` (custom format), kept 30 days, plus a weekly copy off the machine;
  - media: a nightly sync of `App_Data/media`, or R2 bucket versioning;
  - `App_Data/keys` (Data Protection keys) backed up **with** the database, or the encrypted settings secrets can't be read after a restore.
  - A **restore test every month** into a scratch database, with a short checklist in `docs/OPERATIONS.md`.
  - Proposed targets: RPO 24 h, RTO 4 h (confirm in D13).
- **Retention:**
  - audit logs: kept indefinitely in v1;
  - successful jobs: purged after 30 days, failed jobs kept until handled;
  - media trash: 30 days (existing);
  - notifications: read ones purged after 90 days.
- **Accessibility target:** WCAG 2.2 AA.
- **Observability:**
  - Serilog (existing) plus trace ids in the audit log (done);
  - the job failure list (`/api/jobs`) gets an admin screen in Slice 1;
  - a health endpoint for database and storage.

---

## 15. Decisions to approve

| # | Decision | Section |
|---|---|---|
| D1 | Modular monolith in the existing project; module folders; one DbContext; module boundary rules + architecture tests | §1 |
| D2 | Shared conventions: human references, business `DateOnly` vs UTC instants, `Origin`/`SourceRef` provenance, archive ≠ delete, `version` on DTOs, `Money` with currency | §2 |
| D3 | Permission list per module, finance permissions separate | §3.2 |
| D4 | Project visibility `Internal`/`Restricted` with members; restricted = members + Admin only; inheritance to tasks, notes, comments, tickets and files | §3.3 |
| D5 | Client = legal billing entity; Person + ClientContact relationships; duplicate detection rules | §4.1 |
| D6 | Configurable statuses mapped to fixed categories (projects and tasks); one shared task workflow with server-validated transitions | §4.2–4.3 |
| D7 | Progress formula (estimate-weighted top-level tasks, subtasks inside parents, milestones as views) | §4.3 |
| D8 | Tickets: separate internal-note and outbound actions; conversion closes the ticket with `Converted` and keeps the task open | §4.4 |
| D9 | Ledger: charges (draft → final, immutable) + adjustments, payments + allocations with locking, credits, unapplied money, computed balances and statuses | §5.1–5.5 |
| D10 | Opening receivables = amount owed at cutoff, with nullable declaration fields; `ChargeCoverage` for "one bill covers several tasks" | §5.3 |
| D11 | Cutoff changes refused while conflicting records exist; preview tells you what to reclassify | §5.4 |
| D12 | Subscriptions: snapshot prices, unique periods, three independent dimensions (agreement / payment / fulfillment), component expiries, date rules, idempotent scheduler, no past reminders for imported subscriptions | §6 |
| D13 | Backups and restore tests as proposed; RPO 24 h / RTO 4 h; WCAG 2.2 AA; retention as listed | §14 |
| D14 | Second test project for domain integration tests, one test per acceptance scenario | §12 |

## 16. Answers (2026-11-02)

| # | Question | Answer |
|---|---|---|
| **Q1** | Should finance users without project membership see charges from restricted projects? | **Yes.** Amounts and client are visible, so balances are complete; project and task names are masked for non-members (§3.3). |
| **Q2** | Format of human references? | **As proposed:** `CL-0042`, `PRJ-2026-007`, `T-1234` (tasks), `TCK-1203`, `CHG-2026-0153`, `PAY-2026-0089`, `SUB-0031`. Yearly reset for projects and finance. |
| **Q3** | Routes and public pages? | **Business modules at top level, `/admin` for administration, signed-in `/` → dashboard. The public pages are dropped** (home, privacy), and `/` goes to sign-in when signed out. **Self-service "Forgot password" is disabled**: the link, the `/forgot-password` and `/reset-password` pages and the API endpoint are removed. A forgotten password is reset by an administrator (`Users → Reset password`, which already exists). The lockout page says "contact your administrator". |
| **Q4** | VAT on internal charges? | **Net + rate + gross per line.** The default rate is **24%, set in Settings → Finance** (`FinanceSettings.DefaultVatRate`), so it can change without a release. Existing charges keep the rate they were created with. |
| **Q5** | Financial tracking start date? | **2026-12-01** (`FinanceSettings.TrackingStartDate`). Everything before it is entered as opening balances or historical declarations. |
| **Q6** | Initial roles? | **As proposed:** `Admin`, `Manager` (everything except settings and adjustments), `Staff` (work, tickets, notes, own time), `Finance` (finance + clients view), `Support` (tickets + clients view). Seeded by a setup command, editable in Roles. |
| **Q7** | Who approves renewals? | **Only the Admin.** Recording a period's agreement (approve/decline) is an Admin-only action (`[Authorize(Roles = Admin)]`), not a grantable permission. Others can still see periods, create charges and confirm fulfillment if their permissions allow. No auto-approval in v1. |

### 16.1 Self-registration (decided)

**Removed.** Staff accounts are created only by an administrator (`Users → Create user`, which already exists). `/register`, its API endpoint and the "Create an account" links go, together with the forgot-password removal (Slice 1).

---

## 17. What happens after approval

1. **Gate 3:** screen briefs for Dashboard, Client 360° and the onboarding wizard (`docs/ux/`), built on these entities and states.
2. **Slice 1 (rest):**
   - form kit, DataTable v2, status badges, nav registry, route permission guard, command palette, jobs admin screen;
   - `Panoptes_v1.Tests` project, `Sequences`, `IHasTimestamps`, the list helper;
   - **access changes from Q3:**
     - remove the public pages and self-service password reset;
     - `/` redirects to the dashboard or the sign-in page;
     - seed the Q6 roles;
     - remove self-registration (§16.1).
3. **Slice 2, CRM:** clients, people, relationships, duplicate detection, the Client 360° shell, global search.
4. **Slice 3, Projects and work:** `ProjectAccess` (internal/restricted, members), projects with configurable statuses, types and milestones, tasks with one subtask level, checklists and assignees, server-checked transitions, project and global boards, Client 360° Projects/Work tabs, project and task search. Screen brief and scope: `docs/ux/projects-work.md` (decisions S3-1…S3-7).
