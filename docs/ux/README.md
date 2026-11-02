# Hankatt Operations: Screen Briefs (Gate 3)

**Status:** approved on 2026-11-02 (Gate 3). Screens are built from these briefs; changes found in usability testing are recorded back here.

| Brief | Screen | Priority scenarios covered (UX-DESIGN-STANDARD) |
|---|---|---|
| [dashboard.md](dashboard.md) | Dashboard + Action Center | 1 (overdue balance vs old paid work), 3 (renewals, three statuses), 7 (mobile) |
| [client-360.md](client-360.md) | Client 360° | 1, 3, 6 (files), 7 |
| [onboarding-wizard.md](onboarding-wizard.md) | Project creation / historical onboarding wizard | 2 (existing client + historical project + €400 unpaid task), 7 |

The design language follows CLAUDE.md: a dark neutral console, mauve primary, tinted status badges, shadcn components and our own sidebar. Every brief lists the existing boilerplate components it reuses and the gaps that Slice 1 must fill.

---

## Shared patterns (used by every brief)

These are decided once here so the three screens and every later module use them the same way.

### Money

- **Always show amount and currency** (`€1,240.00`), formatted with `Intl` in the user's language: `1.240,00 €` in Greek.
- Use tabular numbers and right alignment in tables.
- **Negative amounts and credits** use a minus sign and the word ("Credit"), never colour alone.
- **Unknown** is shown as the word "Unknown" in muted italic, never `€0.00` or a dash (ONB-05).
- **Opening balances** carry an `Opening balance` chip (muted badge) with a tooltip: "Owed on 1 Dec 2026, when tracking started. Not new revenue."
- **Historical declarations** ("agreed €1,000, €700 paid before tracking") appear only in a collapsible *Historical declarations* block labelled "Not cash. For reference only."

### Status badges (semantic, never colour-only: badge text is always present)

| Category | Badge variant | Examples |
|---|---|---|
| Needs action / overdue | `destructive` tint | Overdue, Failed, Declined |
| Waiting / at risk | `warning` | Due soon, Pending approval, Pending fulfillment, Partially paid |
| In progress / open | `info` | Open, In progress, Charged |
| Done / good | `success` | Paid, Approved, Renewed at provider, Completed |
| Neutral / inactive | `muted` | Draft, Archived, Not needed, Unknown |
| Historical | `purple` (+ clock icon) | Historical, Imported |

**The three renewal dimensions are always three separate badges in a fixed order**: Agreement · Payment · Fulfillment. For example `Approved · Unpaid · Pending at registrar`. They are never merged into one "status".

### Restricted data

- Rows a finance user may see but whose project they can't open show **"Restricted project"** with a lock icon, not a link (ARCHITECTURE Q1).
- Anything else the user can't access is simply absent (it never shows a disabled teaser).

### Page frame

- `PageHeader` (title, short description, primary action on the right, `back` link on detail pages).
- **One primary button per page.** Secondary actions go in an outline button or the `⋮` menu.
- **Destructive actions** use `ConfirmDialog` with the object's name in the title. Archive is *not* destructive (reversible) and needs no confirmation.

### States every screen designs explicitly

| State | Rule |
|---|---|
| Loading | Skeletons shaped like the content, per section, so a slow widget doesn't block the page |
| Empty | What it is, why it's empty, and one action (`TableEmpty` pattern) |
| Error / partial failure | Inline per section: "Couldn't load … · Retry", with the reference id from ProblemDetails |
| Validation | Inline under the field (`FieldError`), focus on the first invalid field, summary only on long forms |
| Conflict (409) | Toast "Someone else changed this… Reload", plus a Reload button that keeps the user's unsaved input visible |
| Permission | Navigation item not shown; direct URL → "You don't have access to this page" state, not a broken page |
| Long data | Truncate with tooltip; never let long client names break the layout (tested with 80-character names) |
| Small viewport | Specific mobile layout per brief, not a squeezed desktop layout |
| Keyboard | Every action is reachable by Tab; the `Ctrl/⌘+K` palette; visible focus ring |

---

## References examined

Documentation and help pages were **read** for each reference. **No product was inspected live** (no accounts). Patterns below come from official docs unless marked otherwise. No branding, assets or screen designs are copied.

| Reference | What we took | Risk noted | Source |
|---|---|---|---|
| **Linear: My Issues (Focus grouping), Triage, Inbox** | "Most urgent first" grouping for personal work; a separate *triage* list for unplanned incoming work with accept/decline/snooze actions; notifications kept separate from work lists | Linear's keyboard-heavy flows assume power users; we keep visible buttons too | [My issues (mirror of linear.app docs)](https://linear-web.pages.dev/docs/my-issues), [Triage](https://linear.app/docs/triage), [Inbox](https://linear.app/docs/inbox), [Daily workflows](https://linear.app/learn/daily-workflows) |
| **Stripe Dashboard: Home** | Home = business charts + **"recent actions needed"**; user adds/removes widgets in an explicit edit mode ("Edit overview → Add → Apply") | Stripe notes home charts are *estimates*: we must not show estimated money as fact, and every number needs a definition | [Web Dashboard](https://docs.stripe.com/dashboard/basics), [Home page charts](https://support.stripe.com/questions/customize-your-dashboard-home-page-charts-for-better-business-insights) |
| **Attio: record pages** | Record page = header actions + **up to six highlight attributes** at the top of Overview + tabs (Overview, Activity, Notes, Tasks, Files…) + a left details panel (Record details, Lists) | Highly configurable layouts add setup cost; we fix the layout per object instead | [Configure record pages](https://attio.com/help/reference/managing-your-data/records/configure-record-pages), [Understanding records](https://attio.com/help/reference/attio-101/attios-data-model/understanding-records) |
| **Invoice Ninja: client overview** | The client page leads with **balance and paid-to-date**, then invoices, payments, credits and activity in one place; the client model separates `balance`, `paid_to_date`, `credit_balance` | Mixes all money into one "paid to date" figure; we must separate live cash from historical declarations | [Clients (v5 docs)](https://invoice-ninja.readthedocs.io/en/v5.0/clients.html), [Clients](https://invoice-ninja.readthedocs.io/en/latest/clients.html) |
| **ERPNext: Opening Invoice Creation Tool** | Opening receivables carry **only the amount outstanding at cutover**, with the legacy invoice number and dates for ageing. "Designed to continue collections and payments, not reproduce every historical transaction." Fully paid invoices aren't needed. | Its "Create missing party" auto-creates customers: a duplicate risk we avoid with explicit duplicate review | [Opening Invoice Creation Tool](https://docs.frappe.io/erpnext/user/manual/en/opening-invoice-creation-tool), [Opening balance](https://docs.frappe.io/erpnext/opening-balance.md) |
| **Xero: conversion balances** (third-party guides, not Xero docs) | A conversion date; outstanding invoices entered **individually** so later payments match them; no double counting with the opening totals; changing the date after transactions is hard | Secondary sources only | [Digit: conversion balances guide](https://digit.business/insights/xero/xero-conversion-balances-setup-guide), [Outbooks: conversion balance](https://outbooks.co.uk/training-and-support/bookkeeping-vat-return/conversion-balance-in-xero) |
| **GOV.UK Design System: question pages, check answers** | Back link + one clear continue action per step; a **"Check your answers"** summary with *Change* links before submitting; *Change* returns to the summary instead of replaying the journey; services commonly allow save and return | Strict one-thing-per-page is too slow for staff entering many rows; we group related fields per step | [Check answers](https://design-system.service.gov.uk/patterns/check-answers), [Question pages](https://design-system.service.gov.uk/patterns/question-pages), [Scottish GDS: check answers](https://designsystem.gov.scot/patterns/check-answers) |
