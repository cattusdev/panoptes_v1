# Hankatt Operations — Business Rules & Decision Register

**Agreed through five requirements rounds plus two clarification rounds.** These rules outrank illustrative sample schemas. Unspecified technical choices must be proposed, not silently assumed.

## A. Customer & project ownership

- Distinct legal billing entities have distinct balances even where the same person owns or manages them.
- A client may exist without a project; an internal project may exist without a client.
- Archived/completed status does not imply subscription cancellation, payment settlement or record deletion.
- No subproject hierarchy. Task structure: task → one subtask level → checklists.
- Multiple task assignees with one primary assignee.
- Project status names configurable, mapped to fixed semantic categories.
- Initially common task workflow; customizable workflows later.
- Progress is computed, with no double counting of tasks/subtasks/milestones. Exact weighting algorithm remains to be formalized.

## B. Support and work

- Tickets and tasks are distinct business entities.
- On conversion, close ticket with `converted` resolution and retain reference to created task. The task is *not* completed by that act.
- Ticket internal note and externally delivered message have unambiguous visibility and separate send actions.
- Track time both manually and with a timer; only one active timer per staff member.
- Scheduled/event-generated tasks must be idempotent and auditable.

## C. Historical onboarding and initial balances

- Wizard paths: new project, in-progress existing project, historical/completed project; client can be chosen or created.
- Historic tasks may be entered directly Completed, with original completion date, cost and financial state independently.
- Historical task: completed, priced €400, unpaid €400 -> client receivable **€400** with origin link. Do not create a fake present-day €400 charge *and* another €400 opening receivable.
- Historical financial input has two forms: summary (amount/status) and detailed reconstruction (known agreement, historical receipts and balance). Unknown original amounts stay unknown.
- Historical paid amounts before cutoff do not become current cash movements. Cash received later against old debt is recorded on actual receipt date.
- Tracking commencement/cutoff is set via Settings and changes after live activity require guarded reconciliation/review.
- Historical imports never replay past automation steps or create artificial invoices, payments, project completion events or tasks.
- Upcoming reminders for active imported subscriptions can be activated intentionally after review.
- Preserve historical source/provenance and actual business dates separately from `created_at`.
- Import wizard must detect duplicates, preview intended records/effects, support draft/resume, and provide useful error feedback.

## D. Money and fiscal references

- Contract/quote/charge/payment/allocation/invoice reference are separate notions.
- Payment schedules do not count as invoices or payments. Partial receipts and overpayments are supported.
- Each receivable belongs to one legal billing entity, may link to project/task/service; a charge may hold multiple line items.
- Finalized financial changes use immutable adjustment/reversal/credit records; do not rewrite history.
- Avoid double counting historic opening debt when associating multiple completed tasks with the same old bill.
- EUR now; currency code on every money record. Multi-currency reports need explicit rate/currency basis.
- External invoice issuance stays with AADE timologio; internal invoice references are metadata, not proof of platform fiscal issuance.
- Track actual receipts versus billed amounts, outstanding debt, forecast renewal revenue, expenses, historic declarations separately.
- Project time tracking and staff time estimates are not automatically assumed to be book-valued expenses.

## E. Recurring services and fulfillment

- Service catalog defaults do not overwrite existing client-specific price agreements.
- Package price and consolidated charge can coexist with individually expiring components (notably domain renewal).
- Keep agreement/renewal approval, payment status and third-party fulfillment separate.
- Charge generation alone does not extend the service period.
- Early renewal defaults to the day after the old coverage period ends, unless authorized override.
- Late renewal is configurable per subscription: preserve cadence or start a new period at specified effective date.
- Track service/coverage period, payment due, reminder date and external registrar/provider expiry independently.
- Suspension is configurable and should not automatically switch off production hosting in the first release.
- Any scheduler re-run must not create duplicate renewal periods/charges/notifications.

## F. Permissions and files

- Projects: `internal` visible to eligible internal staff; `restricted` visible only to authorized members/admins.
- Related tasks, notes, comments, reports, exports and attached files inherit restricted-project boundaries.
- One physical file may be attached to multiple entities. Each association has contextual access checks; global file restrictions also apply.
- A permissive file link or folder cannot elevate access to restricted-project information. Folder-specific grants must only narrow access within project bounds.
- Recycle bin, soft delete, version history and authorized restore. Unlinking shared file need not delete blob.
- ACLs enforced in API, searches, widgets, exports, previews, direct downloads, notifications.

## G. Workflow, messaging and UX

- In-app and email notification preferences per user; mentions check authorization before disclosure.
- Automation wizard: trigger, multiple steps, delays, conditions, authorized actions and manual trigger. Published versions and full execution history.
- Recheck conditions before delayed action; retries with deduplication, controlled cancellation and failure inspection.
- Marketing segments, deduplication, consent/suppression separate from service communications.
- Dashboard role defaults plus user-saved layouts with draggable/resizable widgets. Backend ACL still governs widget content.
- Global search and command palette; saved views, Kanban grouping, action center and calendar.
- Mobile-first installable PWA, but offline editing not assumed in v1.
- Existing boilerplate design system is the visual baseline. Mandatory UX design gate before building major screens.

## H. Remaining design determinations (not new user questions)

These require explicit proposals/review during technical design: precise progress weighting; initial-cutoff reconciliation mechanics; file-link/folder ACL evaluation algorithm; time/date representation for full-day subscriptions; campaign audience snapshot policy; staff-offboarding reassignment; retention, backup RPO/RTO, accessibility target; specific integration feasibility with AADE. Do not present them as user decisions already made.
