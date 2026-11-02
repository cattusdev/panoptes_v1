# Screen brief: Client 360°

**Route:** `/clients/:id` with tab routes (`/clients/:id/projects`, `…/work`, `…/services`, `…/finance`, `…/files`, `…/notes`, `…/activity`). **Status:** approved 2026-11-02.
**Refs:** PRD CRM-01…06, FIN-02/05/08, SUB-04, DOC-01/02; SCREEN-PLANNING §2; ARCHITECTURE §3.3 (Q1), §4.1, §5, §6.

## 1. Who and why

| Persona | Top 3 goals |
|---|---|
| **Owner / Manager** | 1. Understand where we stand with this client: money, work, services. 2. Act without leaving the page (new project, ticket, service, file). 3. See what happened recently. |
| **Finance** | 1. What is owed, what is overdue, what is unapplied. 2. Record a payment and apply it correctly. 3. Find the AADE invoice reference for a charge. |
| **Support / Staff** | 1. Open tickets and work for the client. 2. Contacts (who to call). 3. Services and their expiries. |

**Success:**
- Questions like "does Beta Ltd owe us anything new, or is it the old balance?" and "is their domain renewed?" are answered from the header and Overview without opening another page.
- Shared contacts never mix two companies' balances (CRM-02).

## 2. Actions

- **Primary:** `New ▾` (split menu): Project, Task, Ticket, Service, Note, Upload file. Only items the user may create are shown.
- **Finance primary:** `Record payment` (visible with `Finance.Record`). It sits next to `New ▾` as an outline button, so the page keeps one filled primary button.
- **Secondary:** `Edit details`, `⋮ → Archive / Unarchive`, `⋮ → Copy reference`.
- **Contextual:** row actions in each tab (open, record payment for a charge, approve a renewal for Admin, mark a component renewed).

## 3. Content, by priority

### Header (always visible)

- **Display name** (h1), `Ref` (`CL-0042`), status badge (Active / Inactive / Archived), kind icon (Organization / Individual).
- Legal name and ΑΦΜ/ΔΟΥ on a second line (copyable).
- **Primary contact:** name, role, phone and email (click-to-call/mail).

### Highlights strip (4 items; Attio-style "highlights", fixed set)

| Highlight | Content | Click → |
|---|---|---|
| **Balance** | Outstanding €X, "€Y overdue"; "incl. €Z opening balance" when present | Finance tab, `Open` filter |
| **Unapplied** | Unapplied payments + unused credits (shown only if > 0) | Finance tab, Payments |
| **Work** | Active projects n · open tasks n · open tickets n | Work tab |
| **Next renewal** | Service name, period end date, and the three dimension badges | Services tab |

Users without `Finance.View` see the Work and Next renewal highlights only; the Balance and Unapplied highlights are absent, not blurred.

### Details panel (right side, desktop)

- **Contacts:** each person with role, primary/billing marks and contact methods. "Also contact for: *Other Client* →" when the person is linked to other clients. This links *people* only and **never combines balances**.
- **Addresses:** billing first.
- **Tags**, **Default currency**, **Created / Origin** ("Registered via onboarding · Historical" chip when `Origin = Historical`).

### Tabs

| Tab | Content |
|---|---|
| **Overview** | "Needs attention for this client" (overdue charges, renewals awaiting approval, pending fulfillment, overdue tasks, unassigned tickets); recent activity (last 8 events); active projects with progress bars; services with next expiry |
| **Projects** | Table: ref, name, status (category badge), progress, dates, visibility (🔒 Restricted chip). Archived/completed projects stay listed under a "Show completed & archived" toggle (PRJ-07) |
| **Work** | Two sections: **Tasks** (client tasks and project tasks the user can see) and **Tickets** (open first). Each section has quick filters (Open / Mine / All) |
| **Services** | Subscriptions with components: service, cycle, price snapshot, current period, **Agreement · Payment · Fulfillment** badges, provider expiry per component, next reminder. Row actions: Approve (Admin), Create charge, Mark renewed at provider |
| **Finance** | See §3.1 |
| **Files** | Files linked to this client *and* to its projects/tasks the user can see. Columns: name, linked to, uploaded, by. Actions: Upload, Attach from library (`MediaPicker`), Unlink (removes the link only, says so) |
| **Notes** | Note list by category, newest first; open in a side sheet with version history |
| **Activity** | Full timeline with type filters (work, finance, services, files, notes) |

### 3.1 Finance tab (the most important tab for scenario 1)

```
Balance €1,100.00 (€400.00 overdue) · Unapplied €150.00          [Record payment]
[Open] [All]                                      Currency: EUR
── Live charges ──────────────────────────────────────────────────
Ref            Issued   Due      Description        Total   Paid    Balance  Status
CHG-2026-0161  2 Dec    16 Dec   Hosting 2027       €300    €0      €300     Open
CHG-2026-0158  1 Dec    15 Dec   Website changes    €400    €0      €400     Overdue
── Opening balances (owed on 1 Dec 2026) ─────────────────────────
CHG-2026-0153  —(Aug)   1 Dec    Logo redesign      €400    €0      €400     Overdue  [Opening balance]
── Payments ──────────────────────────────────────────────────────
PAY-2026-0089  10 Dec   Bank     €550   applied €400 → CHG-…0149  unapplied €150   [Apply]
▸ Historical declarations (not cash, for reference only)
  Website 2024 · agreed €1,000 · €700 paid before tracking · source: onboarding
```

- **Live charges and opening balances are separate groups**, each with its own subtotal; a grand total sits under both. Old debt can never look like new revenue (scenario 1).
- **Every row opens a side sheet** (`Sheet`, like the audit log) with lines, adjustments, allocations, the AADE reference(s) with PDF, coverage ("covers tasks: T-1201, T-1202"), and an activity timeline. Final charges show `Adjust…`, not `Edit` (FIN-04).
- **Restricted-project rows** show "🔒 Restricted project" in the description link position (Q1).
- **Multiple currencies:** a subtotal per currency, and no grand total across currencies (FIN-07).

### 3.2 Record payment (dialog)

```
Record payment · Beta Ltd
Amount *        [ 550,00 ] €     Received on * [ 10/12/2026 ▾ ]
Method *        [ Bank transfer ▾ ]   Reference [ ΑΡ. 4471 ]
Apply to (oldest first, editable):
  ☑ CHG-2026-0153  Opening balance  due 1 Dec    balance €400   apply [400,00]
  ☑ CHG-2026-0158  Website changes  due 15 Dec   balance €400   apply [150,00]
  ☐ CHG-2026-0161  Hosting 2027     due 16 Dec   balance €300   apply [  0,00]
Applied €550,00 · Unapplied €0,00                         [Cancel] [Record €550,00]
```

- **Pre-fill:** amounts are applied oldest due date first. The user can edit any amount or untick a charge, and the remainder stays **unapplied**, which is allowed and labelled as such.
- **Over-allocation:** if a row exceeds its charge balance, or the total exceeds the amount, the row shows an inline error and the submit button is disabled. The server enforces the same rule.
- **Tracking start:** a received date before 1 Dec 2026 is refused with "Payments before tracking started can't be recorded; add it as a historical declaration instead."
- **Submit:** toast "Payment PAY-2026-0089 recorded"; balances refresh in place.

## 4. Layout

### Desktop (≥ 1280 px: main column + 320 px details panel)

```
┌ ← Clients                                                                  ┐
│ Beta Ltd  CL-0042 [Active] 🏢                     [Record payment] [New ▾] [⋮]│
│ ΒΗΤΑ Μονοπρόσωπη ΙΚΕ · ΑΦΜ 099999999 · ΔΟΥ Αθηνών · Maria P. (CEO) 📞 ✉   │
├──────────────┬──────────────┬──────────────┬──────────────┬───────────────┤
│ Balance      │ Unapplied    │ Work         │ Next renewal │ Contacts      │
│ €1,100       │ €150         │ 2 proj · 5 t │ Hosting Pro  │ Maria P. CEO ★│
│ €400 overdue │              │ 1 ticket     │ ends 31 Dec  │  📞 ✉ also for│
│ incl. €400   │              │              │ Approved·Un- │  Gamma OE →   │
│ opening      │              │              │ paid·Pending │ Nikos K. IT   │
├──────────────┴──────────────┴──────────────┴──────────────┤ Addresses     │
│ Overview  Projects  Work  Services  Finance  Files  Notes │ Billing: …    │
│ Activity                                                  │ Tags: web,seo │
│ ───────────────────────────────────────────────────────── │ Origin:       │
│ (tab content)                                             │ Historical ⏱  │
└───────────────────────────────────────────────────────────┴───────────────┘
```

- Between 1024 and 1279 px the details panel moves *above* the tabs as a collapsible "About" card.

### Mobile (< 768 px)

```
┌ ← Beta Ltd        [⋮] ┐
│ CL-0042 [Active]       │
│ Balance €1,100         │
│ €400 overdue ›         │  ← single most important number first
├────────────────────────┤
│ ▸ About & contacts     │  ← accordion (call/mail buttons are large)
├────────────────────────┤
│ Overview Projects Work…│  ← horizontally scrollable tab chips
│ (tab content as cards) │  ← tables become cards: title, amount, status
├────────────────────────┤
│ [ + New ]  [ € Payment]│  ← sticky bottom action bar
└────────────────────────┘
```

- Tables become card lists. A charge card shows ref, description, balance, status and the `Opening balance` chip.
- The Record payment dialog becomes a full-height sheet. The allocation rows stack, and each row has a large amount field.

## 5. States

| State | Behaviour |
|---|---|
| Loading | Header skeleton plus the active tab's skeleton; highlights load independently |
| Not found / no access | One "This client doesn't exist or you don't have access" page (same message for both, no leak), with a link back to Clients |
| Archived | Banner: "Archived on 3 Dec 2026. Services and balances are unaffected." with an `Unarchive` button. Actions that create new work show a confirm ("This client is archived. Create anyway?") |
| Empty client (new) | Overview shows a short checklist: Add a contact · Create a project · Add a service · Register past work (onboarding). Each tab has its own empty state with one action |
| Finance without permission | Finance tab and money highlights absent; Overview has no money items |
| Partial failure | Per tab and per highlight: "Couldn't load · Retry · Ref …" |
| Conflict | Editing details with a stale `version` → 409 toast plus Reload, with the user's input kept |
| Duplicate on edit | Changing ΑΦΜ to one that another client has → blocking warning naming that client, with "Open it" and "Save anyway" (requires a reason, audited) |
| Long data | Names up to 200 characters truncate in the header (full text in a tooltip and the details panel); many contacts → "Show all (12)" |

## 6. Main flows and step counts

1. **Record a €550 payment against two charges:** `Record payment` → type 550 → (pre-filled allocation) → `Record`. 3 actions.
2. **New ticket for this client:** `New ▾ → Ticket` → a side sheet with the client pre-filled → subject, priority → `Create`. The new ticket appears in Work and Activity.
3. **Attach a contract** (scenario 6): Files → `Upload` (or `Attach from library`) → choose a file → optionally link to a project. A file linked to a restricted project is visible only to its members (strictest link wins, already enforced).
4. **Is the domain renewed?** (scenario 3): the Next renewal highlight → Services tab → the component row shows `Approved · Paid · Pending at registrar`, with the registrar expiry date and a `Mark renewed` action.

## 7. Components

| Need | Existing | Gap |
|---|---|---|
| Header, back link, actions | `PageHeader` (`back`), `DropdownMenu`, `Button` | Split button "New ▾" (`DropdownMenu` + `Button` group) |
| Tabs with routes | `RouteTabs` + nested routes (as `AccountLayout`) | Scrollable tab chips on mobile |
| Highlights | `IconTile` | `KpiTile` (shared with the dashboard) |
| Tables | `DataTable`, `TablePagination`, `TableEmpty`, `Badge`, `DateTime` | Grouped sections with subtotals; card mode for mobile; `MoneyText`; shared `StatusBadge` |
| Charge detail | `Sheet` (as in the audit log) | — |
| Record payment | `Dialog`, `Field*` | `MoneyInput`, `DatePicker`, editable allocation rows (react-hook-form field array) |
| Files tab | `MediaUploader`, `MediaPicker`, `MediaDetailsSheet`, `MediaThumb`, attachment backend (done) | `RecordFiles` component and per-module files endpoints |
| Activity | — | `ActivityTimeline` component (Slice 1) |
| Archive banner | `Alert` | `warning`/`info` Alert variants |

## 8. Acceptance criteria

1. Live charges and opening balances are shown in separate, labelled groups with separate subtotals. Historical declarations appear only in the collapsed "not cash" block (S2, S3, S4).
2. A client whose primary contact also belongs to another client shows only its own balance. The other client is reachable only through the contact link (CRM-02).
3. Record payment pre-fills allocations oldest first, prevents over-allocation in the UI *and* on the server, and keeps the remainder as unapplied (S16).
4. A finance user who isn't a member sees a restricted project's charges with "Restricted project" and no project link; a non-finance user sees no money anywhere on the page (Q1, S14, S19).
5. Archiving changes no subscription, charge or project (CRM-06): an E2E test asserts unchanged statuses after archive.
6. Each service row shows three separate badges; only Admin sees Approve/Decline (Q7, S10).
7. Files tab: unlinking says "removes the link only", and the file stays in the library (DOC-01).
8. At 390 px: balance first, sticky actions, cards instead of tables, no horizontal scroll.

## 9. Usability scenarios to run

- **Scenario 1:** "Does Beta Ltd owe us money from new work, or only the old balance?" Expect the answer from the Balance highlight plus the Finance groups in under 30 s.
- **Scenario 3:** "Has the client agreed and paid for the hosting renewal, and is the domain renewed at the registrar?"
- **Scenario 6:** "Attach the signed contract so only the project team can see it."
- **Scenario 7:** record a payment on a phone.
