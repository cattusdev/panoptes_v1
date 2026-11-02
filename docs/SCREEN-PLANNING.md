# Hankatt Operations — Screen Planning (Draft)

**Design proposals only.** Final screen patterns depend on boilerplate UI audit and user approval.

## Navigation proposal

- Overview: My Dashboard, Action Center, My Work.
- Relationships: Clients and Contacts.
- Delivery: Projects, Tasks/Boards, Support Tickets, Calendar.
- Business: Quotes/Change Requests, Charges/Payments, Services/Renewals, Expenses.
- Workspace: Files, Notes, Campaigns/Automation, Reports.
- Administration: Users/Roles, Statuses, Workflows, Templates, Settings, Audit/Security.

Global search and command palette are available across authorized records. Avoid overcrowding sidebar: group/collapse modules and show only role-authorized sections.

## Priority screen 1 — Dashboard

**Goal:** Within seconds, recognize overdue work, money owed, upcoming renewals and urgent actions.

- Role-default layout with user-saved alternatives.
- Modular KPI widgets: active projects, overdue tasks/tickets, outstanding balances, upcoming subscriptions, collected vs expenses.
- Action Center highlights items requiring decisions (not noisy event feed).
- Each widget links to an appropriately filtered underlying view and enforces permissions.
- User can reposition/resize widgets, persist dashboard, revert to role defaults.
- Mobile: stacked widgets, reordered priorities, avoid wide dense charts.

## Priority screen 2 — Client 360°

**Goal:** Understand client status and act without navigation churn.

- Top: identity, billing entity, status, contacts, primary actions.
- Overview: projects, receivables, next service expiry, open tickets and recent activity.
- Tabs: Projects, Work, Services, Finance, Files, Notes, Activity.
- Examples of quick actions: new project, new ticket, record receipt, add existing service, attach file.
- Clarify historic declaration versus live receipts, and completed tasks versus unpaid charges.
- Shared contacts do not merge legal entity balances.

## Priority screen 3 — Project creation/onboarding wizard

**Goal:** Create new project OR accurately register old client work with minimal unnecessary steps.

1. Path: New / Existing in progress / Historical completed.
2. Client: select existing, create new, or internal project.
3. Project: title, type, real dates, state, visibility.
4. Previous work: optional historic tasks (completion date, agreed price when known, amount paid pre-cutoff, balance due); or new tasks/milestones if new work.
5. Finance: existing receivables, agreement, old settled state or opening balance; deduplication and classification of unknowns.
6. Services: none or active services/bundles; separate next renewal and external expiry; future notification preview.
7. Files/notes (optional).
8. Review: records to create, origin/provenance, estimated side effects (historical route must say **no retroactive messages/charges**), submit.

Save draft/resume. Clear validation. Prevent duplicate existing client/project/receivable creation. Do not force old milestone/task reconstruction.

## Priority screen 4 — Kanban/task details

- Global board with filters and grouping; same source data powers project board and My Work.
- Task detail with parent/subtask, checklist, assignees (one lead), dates, activity, comments, linked charge/financial summary when authorized.
- Drag/drop checks workflow and resource permissions; position changes saved consistently.
- Mobile fallback to grouped list or swipe-friendly lightweight columns.

## Priority screen 5 — Recurring services & renewals

- List of subscriptions with next billing, service period, external expiry, overdue/payment and actual fulfillment statuses.
- Bundle details show component expiries; clear renewal action and confirmation policy.
- Preview notifications and any resulting new charge before confirming manual action.

## Priority screen 6 — Finance

- Client receivable list grouped by live charges vs opening balances; allocations and credits.
- Payment entry supports partial/multiple-charge distribution and warns of over-allocation.
- Historical settled declarations are not presented as new cash receipts.
- External AADE invoice reference / PDF association, not internal issuance.

## Mandatory design states on every screen

Permission-limited, empty, loading, partial failure, validation errors, success, long data, small viewport, keyboard-only use. Research references before design. No implementation during audit stage.
