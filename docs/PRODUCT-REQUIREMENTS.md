# Hankatt Operations — Product Requirements Specification

**Version:** 1.1 (functional baseline)  
**Date:** 2026-10-31  
**State:** Agreed business scope, pending codebase audit and final technical design.

## 1. Mission and constraints

Internal operations platform for Hankatt, integrating CRM, projects, work, support, quotes, internal financial tracking, recurring services, renewals, automation, files, collaboration and reporting. Must accommodate *new, ongoing, completed and archived* work, including prior work that is fully, partially or not yet paid.

- Single internal organization; staff roles and project ACLs. No multi-tenancy or initial client portal.
- Manual tracking of official invoices from **AADE timologio**; optional verified external API integration later. No proprietary fiscal invoicing engine.
- Self-hostable/local development. Technical preference is ASP.NET Core + React/TypeScript + PostgreSQL, modular monolith; exact implementation/version to be validated after boilerplate inspection.
- Reuse boilerplate mail, media, auth, UI and any other suitable components. Read-only audit first.
- Complete product scope below is **not** a demand to implement all features in release one.

## 2. CRM (CRM)

- **CRM-01** Organizations and individual clients; one contact/person may be attached to multiple businesses through explicit relationships.
- **CRM-02** Each legally distinct billing entity maintains its own outstanding balance and invoicing references; relationships do not combine debts.
- **CRM-03** Multiple contacts, addresses, communication details, tags, notes and status (active/inactive/archived).
- **CRM-04** Client record can exist without projects, tickets, or subscriptions.
- **CRM-05** Customer 360° with Overview, Projects, Tasks/Tickets, Services, Finance, Files, Notes, Activity.
- **CRM-06** Archive preserves authorized history and does not silently cancel obligations or services.

## 3. Projects (PRJ)

- **PRJ-01** Client-linked and internal projects; milestones and deliverables but **no subprojects**.
- **PRJ-02** Configurable project statuses in Settings, mapped to stable system categories for reports and automation.
- **PRJ-03** Members, deadlines, templates, optional handover checklist and configured post-completion service activation.
- **PRJ-04** Related-project links permitted without hierarchy.
- **PRJ-05** Progress automatically derived from tasks/milestones without double counting subtasks.
- **PRJ-06** Visibility `internal` (eligible staff) or `restricted` (authorized project members/admins); task/file/comment/report access inherits project restrictions.
- **PRJ-07** List, Kanban, Timeline, Calendar views; completed/archived projects remain searchable with authorization.
- **PRJ-08** Completion does **not** mean paid, nor automatically cancel associated subscriptions.

## 4. Tasks, time and support (WRK/SUP)

- **WRK-01** Project tasks, client tasks without project, and internal tasks without client/project.
- **WRK-02** Tasks → one level of subtasks → checklists; workflow common initially, extensible to custom workflows.
- **WRK-03** Multiple assignees with one primary owner, priority, tags, due dates, estimates, dependencies as appropriate.
- **WRK-04** Global and project Kanban, grouping by project/assignee/priority, personal and saved filtered views; status transitions validated server-side.
- **WRK-05** Manual work logs and server-tracked timer (start/pause/resume/stop); at most one active timer per user; billable flag.
- **WRK-06** Scheduled/event-based recurring tasks (renewal, payment, completion) with deduplicated execution.
- **SUP-01** Separate support-ticket module; manual intake first, future email ingestion supported by architecture.
- **SUP-02** Tickets contain requester, client/project/service, priority, assignee, status, internal notes, customer-facing messages and files.
- **SUP-03** Ticket converts into a task **and closes with `converted` resolution**; preserve ticket and task link. Task remains open until performed; internal notes must never be sent accidentally.

## 5. Historical onboarding (ONB/HWR/HFI)

**First-release requirement, not an optional migration tool.** Guided wizard supports New Work, Existing/In-progress Work and Historical/Completed Work; choose existing/new client, enter project details/dates, optional previous work, historical finance, running subscriptions, files, review and confirm. Draft can be saved/resumed.

- **ONB-01** Register historical completed projects without reconstructing fictitious tasks, milestones, invoices or payments.
- **ONB-02** Register previous tasks (including completed ones) with actual date, description, attachments, optional assignees and amount (known/zero/unknown).
- **ONB-03** Each historic task may be historically settled, partially settled, unpaid or financially unknown; *task completion remains separate from payment status*.
- **ONB-04** If money is still owed, represent it as a client-specific opening receivable and link to task/project. If already included in an existing receivable, link rather than duplicate.
- **ONB-05** Record original agreed value and historical paid amount where known, or only outstanding balance if those amounts are unknown. Never infer an unknown value is zero.
- **ONB-06** Configurable financial tracking start/cutoff in Settings. Historic declared amounts and receipts before cutoff are distinct from live cashflow; incoming payment after cutoff on old debt counts at actual receipt date.
- **ONB-07** Historic entry suppresses retroactive mail, task/project completion rules, invoicing, and renewal jobs. Only explicitly approved future reminders are scheduled.
- **ONB-08** Existing subscriptions can be entered with historical start and distinct next renewal, billing, external expiry and service periods; projects without subscriptions remain valid.
- **ONB-09** Preview, duplicate detection, provenance/source and attachments, rollback/correction through controlled audited operations. Changing cutoff after financial activity requires explicit reconciliation safeguards.

## 6. Quotes and change requests (QTE)

- **QTE-01** Line-item quotes: quantity, price, discount, tax, validity and status (draft/sent/accepted/rejected/expired).
- **QTE-02** Accepted quote may generate project and commercial records idempotently; preserve agreed snapshot; projects may also be created without quote.
- **QTE-03** Formal change requests for material scope changes (description, cost, approval, schedule impact), and simple task-linked extra charges for smaller agreed work.

## 7. Internal finance (FIN)

- **FIN-01** Distinguish agreement, charge/receivable, line item, installment schedule, payment, allocation, credit, refund, adjustment and external fiscal invoice reference.
- **FIN-02** Charges attach to legal billing client; may reference project, task, service or change request. A payment can settle multiple charges, and a charge can be partially settled.
- **FIN-03** Deposits, staged payment arrangements, unapplied payments, overpayments and credits supported with consistent allocation and concurrency control.
- **FIN-04** Finalized transactions are corrected using auditable reversals/adjustments, never silent overwrites.
- **FIN-05** Register external AADE timologio number, date, MARK/reference where applicable, provider and PDF; do not claim fiscal issuance by this platform.
- **FIN-06** Manual direct project/service costs and general business expenses; optionally recurrent operating expenses. Cost metrics must not pretend to be accounting net profit.
- **FIN-07** EUR as initial operating currency, currency stored per financial record; no mixing currencies in aggregate reporting without an explicit conversion basis.
- **FIN-08** Distinguish outstanding balance, billed, collected, historical declared, expense and forecast. No double counting of installments/opening balances.

## 8. Catalog, packages and subscriptions (SUB)

- **SUB-01** Service catalog with defaults; each client agreement/subscription snapshots its own price and terms.
- **SUB-02** Standalone recurring services and bundles; consolidated client charge possible while components keep distinct actual third-party expiry dates.
- **SUB-03** Recurrence, manual/automatic charge policy, grace period, reminders, renewal/approval conditions, overdue/suspension procedures configurable per agreement.
- **SUB-04** Creating a charge does not complete a renewal; agreement, payment, and actual provider fulfillment tracked separately.
- **SUB-05** Early renewal ordinarily extends after the **previous period end**, with authorized exception.
- **SUB-06** Late renewal per subscription: continue original cycle or start new cycle with explicitly chosen effective date.
- **SUB-07** Price changes (individual/bulk) require preview and effective date; past periods retain original snapshots.
- **SUB-08** Historical and active services do not require an active project; unique renewal identity prevents duplicate billing or periods.

## 9. Automations & campaigns (AUT/CMP)

- **AUT-01** Wizard-configured flows with trigger → conditions → delays → authorized actions. Project/task/ticket/payment/renewal/manual triggers.
- **AUT-02** Templated email variables, previews/test sends, in-app notifications, scheduled actions, execution history, versions, retry/cancel/failure handling.
- **AUT-03** Recheck eligibility at send time; prevent duplicate effects via idempotency. Never execute past steps on historical import.
- **AUT-04** No arbitrary user-executed code or SQL in workflow steps.
- **CMP-01** Segmented announcements with audience preview, snapshots/clear scheduling, recipient deduplication, scheduled delivery and tracking.
- **CMP-02** Marketing consent/suppression and service communications handled distinctly; apply applicable requirements before sending.

## 10. Documents, collaboration, audit (DOC/COL/SEC)

- **DOC-01** Nested folders, secure private storage, previews, one physical file with multiple record associations, version history, recycle bin and authorized restore.
- **DOC-02** Association-context access evaluation, additional file restrictions and folder restrictions; neither a second link nor a permissive folder can bypass restricted project access.
- **DOC-03** Notes organized by category/folder, rich text, note versions and templates.
- **COL-01** Threaded comments, mentions, attachments; notifications must respect record ACLs.
- **SEC-01** Global roles/permissions plus per-project resource restrictions; financial permissions separate. Backend authorization on lists, details, search, exports, widgets and downloads.
- **SEC-02** Human-readable activity timeline, detailed auditable changes and separate admin security log; no logging of secrets.

## 11. UI, reporting, PWA and imports (UI/RPT/IMP)

- **UI-01** Role-default dashboard and personal default selection; draggable/resizable widgets, saved layouts, and permission-safe data queries.
- **UI-02** Sidebar, global search, command palette, client 360°, board/list/timeline/calendar views and cross-module saved views.
- **UI-03** Action Center for actionable issues (assignment, snooze, filters), distinct from notification history.
- **UI-04** Unified calendar for tasks, project milestones, reminders, renewals; allowed rescheduling with consistent effects.
- **UI-05** Mobile-first responsive/installable PWA; offline data editing not promised in initial release. Use existing boilerplate UI design system.
- **RPT-01** Custom filters, authorized datasets/metrics, comparisons, saved reports and exports; separate cashflow, forecasts and historic information; no arbitrary SQL.
- **IMP-01** Import wizard: upload, column mapping, validation, duplicate checks, preview, controlled execution, error report, audit and permission checks. Historical imports suppress side effects.

## 12. Nonfunctional requirements (NFR)

- Single organization; local/self-hosted environment and repeatable deployment.
- Secure authentication; project-level authorization; protected file delivery and uploads.
- Database constraints, concurrency and transactional integrity for finance, renewals and imports.
- Reliable jobs, outbox where warranted, idempotency and observability.
- Backup and **tested restoration** for database and file storage.
- Accessible keyboard/mobile interactions; Greek/English-ready; explicit timezone semantics (Europe/Athens operating context by default, with stored UTC instants where applicable).
- Unit, integration, permissions, workflow and historical onboarding tests.
- Do not hard-code performance or retention targets without an explicit decision.

## 13. Initial milestone and future phases

**Initial usable release:** reused identity/roles/layout, CRM, project/tasks, **historical onboarding including historical tasks and opening receivables**, current subscription/renewal registry, basic support tickets, basic finance, essential files/notes, operational dashboard and audit.

**Later:** full quotes/change requests, richer automations and campaigns, advanced reporting/layout personalization, comprehensive calendar/timeline, advanced import and external invoice integration.

## 14. Acceptance scenarios

1. Import an archived, paid 2024 website project without generating current revenue, invoice or message.
2. Import a completed August 2026 task for €400 still unpaid; outstanding appears under client, project and task exactly once.
3. Import €1,000 historical work with €700 received before cutoff and €300 due; €700 does not count in current cashflow.
4. Record just a known historical outstanding balance where original agreed price is unknown.
5. Link two historic tasks to one already-existing receivable without duplicate amounts.
6. Receive €400 after cutoff for an opening receivable; report on actual receipt date without changing past task completion.
7. Register a historic project with an active hosting subscription and next future renewal, without retroactive reminders.
8. Early annual renewal preserves remaining service days; override possible with authorization.
9. Late renewal obeys subscription-specific continuity/new-cycle policy.
10. Client agrees/pays for domain renewal, but actual registrar fulfillment remains pending until confirmed.
11. Bundle renews together financially while domain has separate expiry/reminder.
12. Shared file is accessible only through allowed links and never leaks restricted data.
13. Soft-deleted file restored with version history by authorized staff.
14. Unauthorized project data absent from searches, reports, exports and attachments.
15. Ticket converts to a task and closes with conversion reason while task stays open.
16. Partial charge settlement and payment allocations produce accurate balance.
17. Finalized charge corrected via logged adjustment, original retained.
18. Duplicate scheduler trigger does not create duplicate charges, tasks or messages.
19. Personalized dashboard does not reveal unauthorized financial/project data.
20. Historical import never triggers old emails or artificial current-period cash movements.

## 15. Readiness

Functional scope agreed. Physical schema, deployment specifics, choice of reusable modules and detailed API are **not finalized** until read-only boilerplate audit. No implementation is authorized by this document.
