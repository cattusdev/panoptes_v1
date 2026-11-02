# Screen brief: Project creation and historical onboarding wizard

**Routes:** `/onboarding` (drafts list + `Start`), `/onboarding/:draftId/:step`. **Status:** approved 2026-11-02.
**Refs:** PRD §5 (ONB-01…09), BUSINESS-RULES C; SCREEN-PLANNING §3; ARCHITECTURE §5.3–5.4 (opening balances, cutoff 1 Dec 2026), §6 (subscriptions), §7 (drafts, preview, commit), §9.3 (no side effects).

## 1. Who and why

| Persona | Top 3 goals |
|---|---|
| **Owner / Admin** (main user at go-live) | 1. Register existing clients and their past work *accurately*, including what is still owed. 2. Don't create duplicates, fake revenue, or emails to clients about old work. 3. Stop at any time and continue later. |
| **Manager** | 1. Start a new project for a new or existing client quickly. 2. Add first tasks and milestones. 3. Attach the brief or contract. |

**Success** (UX priority scenario 2): create an existing client + a historical completed project + a completed task priced €400 and still unpaid **in under 3 minutes**. Afterwards the client, project and task each show **€400 owed exactly once**, and no email or job was triggered.

## 2. Paths and steps

Step 1 picks the path. Steps that don't apply to a path are **not shown** (no greyed-out steps).

| # | Step | New work | In-progress work | Historical (completed) |
|---|---|---|---|---|
| 1 | **Path** | ✓ | ✓ | ✓ |
| 2 | **Client**: select existing, create new, or *Internal (no client)* | ✓ | ✓ | ✓ |
| 3 | **Project**: name, type, visibility, real dates, status | ✓ | ✓ | ✓ |
| 4 | **Work**: new tasks and milestones (optional) | ✓ | ✓ (open tasks) | — |
| 4 | **Past work**: historical tasks (optional) | — | ✓ (completed so far) | ✓ |
| 5 | **Money owed**: opening balances and declarations | — | ✓ | ✓ |
| 6 | **Services**: running subscriptions (optional) | ✓ | ✓ | ✓ |
| 7 | **Files & notes** (optional) | ✓ | ✓ | ✓ |
| 8 | **Review & create** ("check your answers") | ✓ | ✓ | ✓ |

The wizard never *forces* reconstruction: the Historical path can go Client → Project → Review with nothing else (ONB-01).

## 3. Step details

### Step 1: Path

Three large radio cards, each with an icon, a title and one sentence:
- **New work:** "A project starting now."
- **Work in progress:** "Started before Hankatt Ops; continues now."
- **Past work:** "Finished before 1 Dec 2026. May still be unpaid."

Below them is a muted note: "Tracking started on **1 Dec 2026**. Anything before that is recorded as history and never sends emails or creates new charges."

### Step 2: Client

- A **Combobox** "Find a client" (name, ΑΦΜ, email, reference). Results show the name, ΑΦΜ and a balance badge.
- `+ Create new client`:
  - fields: Organization/Individual, display name, legal name, ΑΦΜ, ΔΟΥ, primary contact (name, email, phone), billing address;
  - **duplicate check runs as you type** the name and on ΑΦΜ blur. An inline warning card lists similar clients with `Use this client`, or `It's a different client` (asks for a short reason when the ΑΦΜ is identical).
- `Internal project (no client)` checkbox: hides the money step (no client means no receivable).

### Step 3: Project

- **Fields:** name*, type, visibility (`Internal` / `Restricted` + members picker), started on, due on, **completed on** (Historical: required), status (filtered by path: Historical → Completed-category statuses), description.
- **Duplicate check:** same client + similar project name → inline warning with `Open existing` / `Continue`.
- Dates use the date picker and accept past dates freely. For the Historical path, a completed date after today is refused.

### Step 4b: Past work (historical tasks)

An **editable table** with one row per task. `Add task` adds a row; `Tab` moves across cells; `Enter` on the last cell adds a row.

| Task * | Completed on * | Assignee (optional) | Price | Payment |
|---|---|---|---|---|
| Logo redesign | 14/08/2026 | — | `Known` €400,00 | `Unpaid` |
| Website v1 | 20/03/2024 | Nikos | `Known` €1.000,00 | `Partly paid` → paid before tracking `Known` €700 |
| Hosting setup | 01/02/2024 | — | `Unknown` | `Settled` |

- **Price** is a segmented control: `Known` (amount field), `Free` (€0, explicit), `Unknown`. Unknown is never 0 (ONB-05).
- **Payment** options are `Settled`, `Partly paid`, `Unpaid`, `Unknown`. Task completion stays separate from payment (ONB-03).
  - `Partly paid` asks "Paid before tracking" (`Known` amount or `Unknown`).
  - The **amount still owed** is computed (price − paid) when both are known, otherwise typed in. If price and paid are unknown, owed must be typed in or the payment set to `Unknown`.
- **Per-row attachments** use a paperclip icon (upload or pick from the library).
- **Mobile:** each row becomes a card; `Add task` opens a bottom sheet with the same fields, one per line.

### Step 5: Money owed

This step shows what the past-work answers will create and lets you shape it before anything is saved.

```
Money owed by Beta Ltd on 1 Dec 2026 (when tracking started)

From past work
  ☑ Logo redesign          owed €400,00   → 1 opening balance
  ☑ Website v1             owed €300,00   → 1 opening balance
     (declared: agreed €1.000 · €700 paid before tracking. Not cash.)
  [ Combine selected into one existing bill… ]

Other amounts owed (not tied to a task)
  [+ Add opening balance]   amount owed · original date (optional) · AADE invoice no. (optional) · note

Already have an opening balance for this client?
  ⚠ CHG-2026-0149 · €400,00 · Aug 2026 · "Logo work"   [Link tasks to it instead]

Result: 2 opening balances · client will owe €700,00 (opening)   ← live summary
```

- **Combine into one bill** (S5): select tasks, choose `One bill covers these tasks`, then enter the bill amount once. This creates one opening charge plus coverage links, with **no per-task amounts**. A warning appears if the bill amount differs from the sum of the tasks' owed amounts ("Allowed. The bill is what counts.").
- **Link to an existing opening balance** (duplicate prevention): when the client already has opening balances, the step shows them and offers `Link tasks to it`, which creates no new money.
- **AADE reference** fields are optional: invoice number, date and PDF. They are metadata only.
- **Nothing here is cash.** "Paid before tracking" amounts are listed as *declarations* (S3).

### Step 6: Services

- `Add running service`:
  - catalog item (price and cycle prefilled from the catalog, editable: **this client's own price**, SUB-01);
  - started on (may be historical), **current period end***;
  - next renewal (computed: end + 1 day, shown read-only with an "Override" link that needs the permission);
  - payment status of the current period (`Paid before tracking` / `Owed` → creates an opening balance / `Unknown`);
  - **components:** e.g. domain name, registrar, **registrar expiry date** (separate from the period, SUB-02).
- **Reminders:** a toggle defaulting to **Off: "Turn on after review"**. When switched on, a preview lists the exact future reminders, such as "30 days before 31 Dec 2026 → 1 Dec 2026 (past, will be skipped)" and "7 days before → 24 Dec 2026". **Past reminders are never sent** (S7).

### Step 7: Files & notes

- Upload or pick from the library (attached to the project or client).
- One optional note (category + text).

### Step 8: Review & create ("check your answers")

```
Review                                                      Draft saved 10:42
Client      Beta Ltd (existing, CL-0042)                               [Change]
Project     Logo & Website · Completed 20 Mar 2024 · Internal          [Change]
Past work   3 tasks (2 with amounts owed)                              [Change]
Money owed  2 opening balances · €700,00 · declarations: 1             [Change]
Services    Hosting Pro · period ends 31 Dec 2026 · reminders off      [Change]
Files       2 files                                                    [Change]

⚠ Possible duplicates (1)
  Project "Logo and website" already exists for Beta Ltd (PRJ-2024-003)
  ( ) Use the existing project and add the tasks to it
  (•) Create a new project anyway
──────────────────────────────────────────────────────────────────────
What happens when you create
  ✓ Creates: 1 project, 3 tasks (completed), 2 opening balances (€700,00), 1 service
  ✓ No emails, notifications, charges or automations for past dates
  ✓ Client balance becomes €700,00 (opening balances)
  – No reminders scheduled (reminders are off)
                                              [Back]  [Create records]
```

- `Change` jumps to that step, and `Continue` there **returns to Review** (GOV.UK pattern).
- The duplicate block must be resolved: Create is disabled until each duplicate has a choice.
- **Submit** runs the server preview first; if the server finds new problems, the review re-renders with them. Then the single all-or-nothing commit runs.
- **Success page:** "Created" with links to the client, project and opening balances, plus `Register another for this client` and `Start new`.

## 4. Layout

### Desktop

```
┌ Register past work · Beta Ltd                    Saved 10:42 · [Save & close] ┐
├────────────────────┬──────────────────────────────────────────────────────────┤
│ ① Path        ✓    │  Past work                                               │
│ ② Client      ✓    │  Tasks completed before or during this project.          │
│ ③ Project     ✓    │  (editable table)                                        │
│ ④ Past work   ●    │                                                          │
│ ⑤ Money owed       │                                                          │
│ ⑥ Services         │                                                          │
│ ⑦ Files & notes    │                                                          │
│ ⑧ Review           │                                                          │
├────────────────────┴──────────────────────────────────────────────────────────┤
│                                                 [Back]  [Skip]  [Continue]    │  ← sticky footer
└────────────────────────────────────────────────────────────────────────────────┘
```

- The left stepper is a `nav` list. Completed steps are clickable; future steps are clickable only once the previous required steps are valid.
- `Skip` appears only on optional steps.

### Mobile

```
┌ ✕  Step 4 of 8 · Past work ┐
│ ████████░░░░░░░             │  ← progress bar + step menu (tap the title)
│ [Logo redesign       ] ›    │  ← task cards
│ [Website v1          ] ›    │
│ [+ Add task]                │
├─────────────────────────────┤
│ [Back]          [Continue]  │  ← sticky
└─────────────────────────────┘
```

## 5. Drafts and saving

- **Autosave:** on every step change and 2 s after the last edit. A "Saved 10:42" / "Saving…" / "Not saved, retry" indicator sits in the header.
- **Drafts list** at `/onboarding`: client (or "New client: name"), path, current step, last change, owner. Actions are `Resume` and `Delete` (with confirmation). Drafts older than 90 days are flagged as stale.
- **Conflict:** the same draft open in two windows → a 409 on save → banner "This draft was changed in another window. Reload to continue." The unsaved field values are shown so nothing is lost.
- **Leaving with unsaved changes:** the router blocker asks "Leave? Your last changes are saved as a draft" (they're autosaved, so no data is lost).

## 6. States

| State | Behaviour |
|---|---|
| Loading a draft | Stepper plus step skeleton |
| Validation | Inline per field when it is blurred and on Continue. Continue focuses the first error. Server-side step validation errors map to the same fields |
| Duplicate warnings | Inline cards in the step where the duplicate is detected, plus a summary on Review (blocking until resolved) |
| Commit failure | "Nothing was created" (all-or-nothing) + the reason + reference; the draft stays intact |
| Commit retry | A second commit of the same draft returns the existing result (idempotent): "Already created" + links |
| Permission | Requires `Onboarding.Use` plus the create permissions for what the draft contains. A missing permission is shown on the affected step ("You can't create services; ask an admin") before Review |
| Cutoff | Payments before 1 Dec 2026 can't be entered as cash here; the "Paid before tracking" field explains why |
| Long data | 50+ task rows: virtualised table and a sticky header; review shows counts with "Show all" |

## 7. Components

| Need | Existing | Gap (Slice 1) |
|---|---|---|
| Fields, errors | `Field`, `FieldLabel`, `FieldError`, `Input`, `Textarea`, `Select`, `Switch` | react-hook-form + zod wiring (`FormField`, `applyServerErrors`) |
| Client search | — | `Combobox` (popover + command) |
| Dates | `DateTime` (display only) | `DatePicker` |
| Money with Known/Free/Unknown | `ToggleGroup` | `MoneyInput` + `KnownAmountInput` (segmented + amount) |
| Editable rows | `Table` | Field-array table with keyboard row add; card mode for mobile |
| Stepper, sticky footer, progress | — | `WizardStepper`, `WizardFooter`, shadcn `progress` |
| Duplicate warnings, side-effect summary | `Alert` (default/destructive/error/success) | `warning` and `info` Alert variants |
| Files | `MediaUploader`, `MediaPicker` | Attach-on-upload target (§8 ARCHITECTURE) |
| Leave guard | — | Router `useBlocker` wrapper |
| Confirm delete draft | `ConfirmDialog` | — |

## 8. Acceptance criteria (each is an E2E test, plus a domain test where noted)

1. **Scenario 2 / S2:** existing client → Past work path → project completed Aug 2026 → one task €400 unpaid → Review → Create.
   - The client balance, project and task each show €400 owed (from one opening charge).
   - The job table contains **no** side-effect jobs.
   - It takes ≤ 8 screens and ≤ 3 minutes for a practised user.
2. **S3:** €1,000 agreed, €700 paid before tracking → an opening balance of €300 plus a declaration. "Collected" reports exclude €700.
3. **S4:** price and paid unknown, owed €250 → opening balance €250; agreed and paid shown as "Unknown".
4. **S5:** two tasks "covered by one existing bill" of €500 → one charge of €500 plus two coverage links; no per-task amounts.
5. **S7:** a historical project with an active hosting subscription and reminders off → no reminder jobs. Turning reminders on after review schedules only future ones (preview matches).
6. **Duplicates:** same ΑΦΜ → blocking warning; similar project name → warning; Create is disabled until resolved.
7. **Drafts:** close the tab mid-step → resume from `/onboarding` at the same step with the same data. Two windows → 409 banner, no lost input.
8. **Idempotency:** commit twice (double-click, retry) → one set of records.
9. **Keyboard:** the whole wizard is completable with Tab/Enter/Space, including adding table rows.
10. **Mobile (390 px):** the full Scenario 2 flow is completable with cards and bottom sheets, with no horizontal scroll.

## 9. Usability scenarios to run

- **Scenario 2** (above) with you as the user, timed. Record wrong turns.
- "Register a client whose 2024 website is fully paid, with nothing owed." Expect no money step entries and a client balance of €0.
- "This client owes €1,200 for three old jobs billed as one invoice." The user should find *Combine into one bill*.
- Interrupt halfway, then resume the next day from the drafts list.
