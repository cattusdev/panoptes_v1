# Hankatt Operations — Delivery Roadmap & Approval Gates

**This is a plan, not authorization to implement.**

## Gate 0 — Product baseline (prepared)

- PRD v1.1, business invariants, acceptance scenarios and UX design standard provided.
- Open technical design choices explicitly listed; do not invent boilerplate functionality.

## Gate 1 — Read-only boilerplate audit (FIRST AGENT ACTION)

- Inspect source + docs, inventory real capabilities, confirm stack and coupling.
- Deliver evidence-based capability/reuse matrix, technical risks and suggested adoption path.
- No changes, file writes, dependency installs, migrations, or scaffold.
- Review report with user before proceeding.

## Gate 2 — Architecture and domain design (after audit approval)

- Decide which existing abstractions to reuse/adapt/extract vs new modules.
- Define domain aggregates and ownership, entity relationships, idempotency, storage ACL model, background processing semantics.
- Formalize historical financial cutoff and opening balance reconciliation.
- Define data migration strategy, API contracts, authorization matrix, integration boundaries, architecture tests.
- Deliver architecture/domain/database plan for approval; no production implementation until explicitly approved.

## Gate 3 — UX research and design prototype (after audit)

- Audit existing UI tokens/components.
- Study relevant real UI reference patterns (where accessible).
- Design and review Dashboard, Client 360°, Historical Project Wizard first; include mobile and error/empty states.
- Document approved patterns and usability acceptance scenarios.

## Gate 4 — MVP implementation (only after approval)

Suggested sequenced slices:

1. Application shell/identity/permissions/audit reuse.
2. CRM with relationships, archival and Client 360° foundation.
3. Projects/tasks, historic completion, memberships and basic Kanban.
4. Onboarding wizard with historic tasks, opening receivables and duplicate prevention.
5. Internal charges, payments/allocations, financial summaries.
6. Service catalog, active subscriptions, renewal registry and due alerts.
7. Basic tickets, files/notes and daily dashboard.

Each slice: database migrations + backend + UI + permission checks + integration tests + realistic UX review, not CRUD only.

## Gate 5 — Expanded functionality

Quotes/change requests; enhanced automation wizard; campaign builder; advanced dashboard layouts and reporting; full calendar/timeline; improved import/export; external invoice read-only sync if verified.

## Risk priorities

- Historical balances counted as current receipts.
- Duplicate receivables from multiple previous tasks.
- File links leaking restricted-project attachments.
- Retry processing duplicate renewal charges or emails.
- UI design drifting from existing boilerplate components.
- Multi-tenant dependency accidentally pulled into an internal single-org app.

## Definition of ready for coding

Read-only audit reviewed, scope of first slice approved, critical invariants tested in design, UI design gate passed for key screens, migration and rollback strategy understood. No speculative implementation from initial prompt.
