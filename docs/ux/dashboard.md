# Screen brief: Dashboard and Action Center

**Route:** `/dashboard` (signed-in `/` redirects here). **Status:** approved 2026-11-02.
**Refs:** PRD UI-01, UI-03; SCREEN-PLANNING §1; ARCHITECTURE §5.8 (metric definitions), §9.5 (Action Center), §11 (widgets).

## 1. Who and why

| Persona | Top 3 goals |
|---|---|
| **Owner / Admin** (you) | 1. See in seconds what is overdue or at risk: work, money, renewals. 2. Decide on what only you can decide (renewal approvals). 3. Know the cash position: collected this month vs outstanding, without counting old paid work as new. |
| **Manager** | 1. Overdue and unassigned work. 2. Upcoming deadlines and renewals. 3. Jump to filtered lists. |
| **Staff / Support** | 1. My work today. 2. My tickets. 3. What's due this week. |
| **Finance** | 1. Overdue receivables. 2. Payments to apply. 3. Upcoming renewal revenue. |

**Success:** within 10 seconds of opening, you can name the most urgent item and reach it in one click (UX priority scenarios 1 and 3).

## 2. Actions

- **Primary:** none page-wide. Each item has its own action (Approve renewal, Record payment, Assign, Open).
- **Secondary:** `Customize` (edit layout), `⋮ → Reset to default layout`.
- **Per widget:** a title link → the full list with the same filter applied (e.g. Overdue charges → `/finance/charges?status=overdue`).

## 3. Content, by priority

1. **Action Center: "Needs your attention".** One list, grouped, most urgent first. Inspired by Linear's Focus grouping and Stripe's "actions needed".
   - **Renewals awaiting your approval** (Admin only, Q7): service, client, period, price, `Approve` / `Decline`.
   - **Overdue charges**: client, ref, days overdue, balance. `Record payment` / `Open`.
   - **Unassigned tickets**: subject, client, age. `Assign to me` / `Assign…`.
   - **Overdue tasks** (mine; all tasks for managers): title, project, due. `Open`.
   - **Pending fulfillment**: agreed and paid but not renewed at the provider (e.g. a domain). `Mark renewed`.
   - **Expiring components** (provider expiry within 30 days): domain, registrar date.
   - Each row has a `Snooze` menu (1 day, 1 week, until a date). Snoozed items show under a "Snoozed (n)" toggle.
2. **KPI tiles** (4, all per currency, each with a one-line definition in its tooltip, ARCHITECTURE §5.8):
   - **Outstanding:** €X, with "€Y overdue" below. The opening-balance part is shown as "incl. €Z opening balances".
   - **Collected this month:** payments by received date, vs last month (▲/▼ plus the word "more"/"less"). *Never* includes historical declarations.
   - **Renewals in 30 days:** count and expected amount, with "n awaiting approval".
   - **Overdue work:** overdue tasks + open tickets older than the SLA (count), with "n unassigned".
3. **Upcoming (next 14 days):** a dated agenda of task due dates, period ends, payment due dates and provider expiries. Each entry has a type icon and text, never colour alone.
4. **My work:** my tasks due today and overdue, plus a running-timer chip if a timer is active.
5. **Cash, last 6 months:** collected vs expenses, monthly bars with a table toggle for screen readers. Labelled "Collected (cash received)" and "Expenses", **never "profit"** (FIN-06).
6. **Oldest receivables:** the top 5 by days overdue, with an `Opening balance` chip where relevant.

## 4. Layout

### Desktop (≥ 1024 px)

```
┌ Dashboard ─────────────────────────────────── [Customize] [⋮] ┐
│ Monday 7 December 2026                                        │
├──────────────┬──────────────┬──────────────┬──────────────────┤
│ Outstanding  │ Collected    │ Renewals 30d │ Overdue work     │
│ €4,320.00    │ €2,150.00    │ 6 · €1,180   │ 9                │
│ €1,100 overdue│ ▲ €400 more │ 2 awaiting ✓ │ 3 unassigned     │
│ incl. €2,800 │ than Nov     │ approval     │                  │
│ opening bal. │              │              │                  │
├──────────────┴──────────────┴──────────────┼──────────────────┤
│ Needs your attention                    12 │ Upcoming 14 days │
│ ▸ Renewals awaiting your approval (2)      │ Tue 8  ◷ Task: … │
│   Hosting Pro · Acme SA · 1 Jan–31 Dec 27  │ Thu 10 ⟳ Period  │
│   €240.00        [Approve] [Decline] [⋯]   │        ends: …   │
│ ▸ Overdue charges (3)                      │ Fri 11 🌐 Domain  │
│   CHG-2026-0153 · Beta Ltd · 21 days       │        expires … │
│   €400.00 [Opening balance] [Record pay.]  ├──────────────────┤
│ ▸ Unassigned tickets (3) …                 │ My work          │
│ ▸ Overdue tasks (4) …                      │ ☐ Fix SSL · Acme │
│ Snoozed (1)                                │ ☐ Send report …  │
├────────────────────────────────────────────┴──────────────────┤
│ Cash, last 6 months [Chart|Table] │ Oldest receivables         │
│ ▇ collected ▒ expenses           │ Beta Ltd  €400  21d  Open.bal│
└──────────────────────────────────┴────────────────────────────┘
```

- **Grid:** 12 columns. Each widget is size **S** (3 cols), **M** (6), **L** (8) or **XL** (12). Tiles are S; Action Center is L; Upcoming and My work are S/M.
- **Customize mode** (Stripe-like explicit mode, not always-draggable):
  - widgets get a drag handle (dnd-kit), a size menu (S/M/L) and a remove ✕;
  - a side sheet lists the widgets you can add, already filtered by your permissions;
  - `Save` / `Cancel` / `Reset to role default`.
  - **Keyboard:** each widget has `Move up` / `Move down` buttons in customize mode, so reordering never requires a mouse.

### Mobile (< 768 px): its own order, not a squeezed grid

```
┌ Dashboard        [⋮] ┐
│ Needs your attention │  ← first: what to act on
│ ▸ Renewals (2)    ›  │
│ ▸ Overdue charges ›  │  ← groups collapsed, tap opens the list
│ ▸ Tickets (3)     ›  │
├──────────┬───────────┤
│Outstanding│ Collected│  ← tiles 2×2, numbers only
│ €4,320   │ €2,150    │
├──────────┼───────────┤
│Renewals  │ Overdue   │
│ 6        │ 9         │
├──────────┴───────────┤
│ Upcoming (7 days)    │
│ My work              │
└──────────────────────┘
```

- Charts are replaced by "€2,150 collected · €900 expenses this month" plus a link to the full report.
- Row actions are full-width buttons inside the expanded group (no hover-only controls).
- Customize is desktop-only in v1. On mobile the role default or your saved layout's *order* applies.

## 5. States

| State | Behaviour |
|---|---|
| Loading | Each widget has its own skeleton and its own query, so one slow widget never blocks the page |
| Empty (new install, no clients) | Action Center shows "Nothing needs your attention" with an illustration-free empty card and two actions: **Register existing client work** (onboarding wizard) and **New client** |
| Empty (all clear) | "All clear. Nothing overdue or awaiting approval." plus "Snoozed (n)" if any |
| Partial failure | The failed widget shows "Couldn't load Outstanding · Retry · Ref 0HN…"; other widgets keep working |
| Permission | Widgets the user can't use are not offered in Customize. If a saved layout contains one (permission removed later), it renders "Not available for your role" and can be removed |
| Restricted data | Counts and lists come from access-filtered queries; a finance user sees charges masked as "Restricted project" (Q1) |
| Stale | Data refreshes every 60 s and on window focus (the existing dashboard pattern); a small "Updated 1 min ago" note per widget |
| Long names | Client and service names truncate with a tooltip; amounts never truncate |

## 6. Main flows

1. **Approve a renewal** (Admin): Action Center → `Approve` → small confirm popover showing the period, price and "Creates no charge by itself" → item moves out of the list, toast "Approved. Create the charge?" with a `Create charge` action. Two clicks plus an optional third.
2. **Overdue balance** (scenario 1): the Outstanding tile shows overdue and opening-balance parts separately → click → the receivables list filtered `overdue`, with opening balances clearly chipped. Old paid work never appears, because it isn't a receivable.
3. **Customize:** `Customize` → drag/resize/add → `Save`. `Reset to role default` asks for confirmation (it discards your layout).

## 7. Components

| Need | Existing | Gap (built in Slice 1) |
|---|---|---|
| Page header, actions menu | `PageHeader`, `DropdownMenu` | — |
| KPI tile | `IconTile` only (`components/admin/StatCard.tsx`) | Real `KpiTile` (value, delta with word, definition tooltip, link) |
| Lists inside widgets | `DataTable`, `Badge`, `DateTime mode="relative"`, `TableEmpty` | Compact list variant; shared `StatusBadge` |
| Chart | — (chart tokens exist in `index.css`) | shadcn `chart` (Recharts, approved) with a data-table toggle |
| Widget grid, customize mode | — | `WidgetGrid` with dnd-kit + keyboard move buttons; layout persistence API |
| Snooze menu, date choice | `DropdownMenu` | `DatePicker` (calendar + popover) |
| Confirm popover | `ConfirmDialog` (modal) | `popover` for light confirmations |

## 8. Acceptance criteria

1. Every number has a tooltip definition matching ARCHITECTURE §5.8. Collected never includes historical declarations or opening balances (test with S3 data: €700 declared does not appear).
2. Outstanding shows the overdue and opening-balance parts; clicking it opens the correctly filtered list.
3. Renewal items show the three dimensions as separate badges; only Admin sees Approve/Decline.
4. A Staff user's dashboard contains no finance widget and no restricted project item (E2E, S19).
5. One failing widget shows its own error state while the others render (E2E, forced failure).
6. Layout: customize, save, reload (persisted), reset to role default. Reordering works with the keyboard alone.
7. At 390 px wide: Action Center first, no horizontal scroll, no hover-only controls, all row actions reachable.
8. With an 80-character client name and amounts over €100,000, nothing overflows its widget.

## 9. Usability scenarios to run (UX-DESIGN-STANDARD)

- **Scenario 1:** "Which client owes the most overdue money, and is any of it from before December?" (Expect: answered from the tiles and Oldest receivables in under 20 s.)
- **Scenario 3:** "Is acme.gr renewed at the registrar?" (Expect: found via Upcoming or Expiring components; the user correctly tells agreement, payment and fulfillment apart.)
- **Scenario 7:** the same two questions on a phone.
