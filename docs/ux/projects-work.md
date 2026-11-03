# Screen brief: Projects, project page, task board and task details

**Routes:** `/projects`, `/projects/:id` with tab routes (`…/tasks`, `…/files`, `…/activity`), `/tasks` (global board and list). Task details open in a side sheet on any of these pages, kept in the URL as `?task=T-1234`. **Status:** proposed 2026-11-03 for Slice 3, awaiting approval.
**Refs:** PRD PRJ-01…08, WRK-01…04; BR A, F; SCREEN-PLANNING §4; ARCHITECTURE §3.3, §4.2, §4.3, §9.2, §10; UX-DESIGN-STANDARD priority scenario 4 (assign and change status from the board and from details) and 7 (mobile).

## 0. Slice 3 scope (what this brief covers)

| In Slice 3 | Later (where) |
|---|---|
| Projects: client-linked and internal, type, configurable statuses, real dates (may be in the past), visibility Internal/Restricted, members (Lead/Member/Viewer), milestones, archive, complete | Templates, handover checklist, post-completion service activation (with Subscriptions, Slice 6); related-project links (PRJ-04, Slice 7) |
| `ProjectAccess` replaces `AdminOnlyProjectAccess`: restricted projects and everything in them (tasks, files, activity, search) visible only to members and Admin | — |
| Tasks: project, client and internal tasks; one level of subtasks; checklist; several assignees with one primary; priority, tags, start/due/completed dates, estimate | Time tracking and timer (WRK-05, see decision S3-1); comments and mentions (COL-01, with notes in Slice 7); dependencies; recurring tasks (WRK-06, with Subscriptions) |
| Shared task workflow with configurable statuses mapped to categories, and server-checked transitions (also on drag and drop) | Custom per-project workflows |
| Project board and global board (`/tasks`) + list view of the same data; filters in the URL; progress per §4.3 | Saved views, swimlane grouping, Timeline and Calendar views (PRJ-07, UI-04: Gate 5) |
| Client 360°: Projects tab, Work tab (tasks part), `New ▾ → Project / Task` | Tickets part of the Work tab (Slice 7) |
| Settings → **Projects & tasks**: project statuses, project types, task statuses and allowed transitions | — |
| Ctrl+K finds projects (`PRJ-2026-007`, name) and tasks (`T-1234`, title) through their own access-filtered providers | — |

## 1. Who and why

| Persona | Top 3 goals |
|---|---|
| **Owner / Manager** | 1. See which projects are active, late or stuck, per client. 2. Start a project with the right team and visibility. 3. Assign work and move it along without opening every task. |
| **Staff** | 1. "What do I have to do, and what is late?" 2. Update status, checklist and dates in seconds. 3. Find a task by reference or title. |
| **Finance / Support** | 1. See whether a client's project is done (done ≠ paid, PRJ-08). 2. Open the work linked to a client. 3. Never see a restricted project they are not part of. |

**Success:**
- A staff member finds their overdue tasks and moves one to *In progress* in under 10 seconds, on desktop with the mouse or keyboard, and on a phone.
- Nobody outside a restricted project can find any trace of it: lists, boards, search, the Client 360° tabs, files and activity all return nothing (S14).
- Project progress never counts a subtask twice and never shows 0% for a finished historical project without tasks.

## 2. Actions

**Projects list:** primary `New project`. Row click opens the project.

**Project page:**
- **Primary:** `New task` (opens quick create with the project filled in).
- **Secondary:** `Edit details`, `Complete project…` (or `Reopen` when completed), `⋮ → Archive / Unarchive`, `⋮ → Copy reference`. `Manage members` sits in the details panel next to the member list.

**Board / list:**
- **Primary:** `New task`. Each column also has a `+` that creates a task straight in that status.
- **Contextual:** drag a card to another column or position; `⋮` on a card (Move to…, Assign to me, Copy link, Delete); in the list view, row selection with bulk *Move to status*, *Assign*, *Set priority* (all or nothing, ARCHITECTURE §1.4).

**Task details sheet:** inline-editable fields (no separate Edit mode), `Add subtask`, `Add checklist item`, `⋮ → Copy link / Move to project… / Delete`.

**No delete for projects in this slice:** archive covers the "not active any more" case. A task can be deleted (`Tasks.Delete`), with a `ConfirmDialog` naming the task and its subtask count, and an activity entry is kept.

## 3. Content, by priority

### 3.1 Projects list (`/projects`)

```
Projects                                                       [New project]
[Search…] [Status: Open ▾] [Client ▾] [Lead ▾] [Mine] [Show archived]   Columns ▾
Ref           Name                 Client      Status       Progress   Due        Lead
PRJ-2026-012  Website redesign     Beta Ltd    [Active]     ▓▓▓░░ 62%  15 Dec     Maria P.
PRJ-2026-011  🔒 ERP integration   Gamma OE    [On hold]    ▓░░░░ 20%  —          Nikos K.
PRJ-2026-009  Brand book           Internal    [Completed]  100%       done 2 Nov Maria P.
```

- **Columns, by priority:** name with ref, client (or the word "Internal"), status badge (tone from the *category*), progress bar with %, due date (red "Overdue" chip when past and not completed; "done {date}" when completed), lead avatar(s), restricted 🔒 chip. Hideable: type, started, members count.
- **Default filter "Open":** categories Planned, Active, On hold. "All" adds Completed and Cancelled. Archived projects appear only with *Show archived* (they stay searchable, PRJ-07).
- **Mine** = projects where I am a member.
- Server-side sort, paging and search (`ListRequest`, escaped `ILike`), as in Clients.

### 3.2 Project page (`/projects/:id`)

**Header (always visible):**
- Name (h1), `PRJ-2026-012`, status badge, 🔒 Restricted chip, client link (`Beta Ltd →`) or "Internal project".
- Second line: progress bar with % and "8 of 13 tasks done", due date with Overdue chip, completed date when completed.

**Details panel (right, desktop; "About" card above the tabs at 1024–1279 px, accordion on mobile):**
- **Team:** members with role (Lead first), `Manage members` (with `Projects.ManageMembers`). For restricted projects a line says "Only members and administrators can see this project."
- **Dates:** Started, Due, Completed (business dates; past dates are allowed).
- **Type**, **Visibility**, **Origin** (Historical chip when the onboarding wizard created it, Slice 4), Created by/at.

**Tabs:**

| Tab | Content |
|---|---|
| **Overview** | Progress by milestone (name, due, bar, "3/5"), with add/edit/reorder milestones; "Needs attention" (overdue tasks, blocked tasks, tasks without an assignee); recent activity (last 8) |
| **Tasks** | The board for this project (default) or the list view: same filters and the same component as `/tasks`, with the project filter fixed |
| **Files** | `RecordFiles` for the project, same component as the Client Files tab. Files linked here carry the project id, so a restricted project's files are hidden from everyone else (strictest link wins, already enforced) |
| **Activity** | Full project timeline: project changes, member changes, task events |

### 3.3 Task board (`/tasks` and the project Tasks tab)

```
Tasks                                          [Board | List]          [New task]
[Search…] [Project ▾] [Client ▾] [Assignee: Me ▾] [Priority ▾] [Due ▾] [Show done]
┌ To do (12)      + ┐┌ In progress (4) + ┐┌ Blocked (1)    + ┐┌ Done (30)      + ┐
│ ┌───────────────┐ ││ ┌───────────────┐ ││ ┌───────────────┐ ││ last 14 days     │
│ │T-1234  ⚑ High │ ││ │T-1201 ⚑ Urgent│ ││ │T-1188         │ ││ ┌───────────────┐│
│ │Homepage hero  │ ││ │SSL renewal    │ ││ │Waiting for DNS│ ││ │T-1170 ✓       ││
│ │Website redes… │ ││ │Beta Ltd       │ ││ │Gamma OE       │ ││ │Logo export    ││
│ │☑ 2/5  ⑂ 1/3   │ ││ │📅 Today       │ ││ │📅 Overdue 2d  │ ││ └───────────────┘│
│ │📅 15 Dec  (MP)│ ││ │         (NK)  │ ││ │         (MP)  │ ││ Show all 30 ›    │
│ └───────────────┘ ││ └───────────────┘ ││ └───────────────┘ ││                  │
```

- **Columns = task statuses** in their configured order. A **Cancelled** column is hidden by default (like Linear's hidden columns, a card can still be dropped on it).
- **Done** shows tasks completed in the last 14 days, with "Show all" leading to the list view filtered to Done, so the board stays fast with years of history.
- **Card content, by priority:** title (2 lines max), ref, priority flag (not shown for "No priority"), project or client name (on the global board), due date chip (Today / Overdue Nd / date), primary assignee avatar (+n for the others), checklist count ☑ and subtask count ⑂ when present, 🔒 when the project is restricted.
- **Subtasks are not separate cards.** They live inside their parent (count on the card, list in the sheet), so the board shows each piece of work once (the same rule as progress).
- **Default filter on `/tasks`:** Assignee = Me, not done. Clearing it shows everything the user can see. Filters live in the URL (`useUrlFilters`), so a filtered board can be bookmarked or shared.
- **List view:** `DataTable` with the same filters, sort by due/priority/updated, bulk actions; it is the only view where completed history is browsed.

### 3.4 Drag and drop rules

- Dropping a card on another column sends **one** request (`PATCH /api/work/tasks/{id}/move` with `statusId`, the neighbours for the rank, and `version`). The card moves at once (optimistic update).
- **Transition not allowed:** while dragging, columns the task cannot move to are dimmed with a "not allowed" cursor (the client reads the transition table). If the server refuses anyway, the card goes back and a toast names the reason ("Tasks can't move from Done to Blocked").
- **Conflict:** someone else moved or edited the task → 409, the card goes back, toast "Someone else changed this task · Reload".
- **Keyboard:** focus a card, `Space` picks it up, arrows move it, `Space` drops, `Esc` cancels (the drag library's accessible mode with screen-reader announcements). The card `⋮ → Move to…` menu does the same without dragging.
- **No permission to edit** (`Tasks.Edit`, or Viewer in the project): cards are not draggable and have no move items; the board is read-only, with no disabled teasers.
- **Moving into a Done-category status** sets `CompletedOn` to today (business clock). Moving out of it clears the date. The date stays editable in the sheet, for work finished earlier.

### 3.5 Task details (side sheet)

```
┌──────────────────────────────────────────────── T-1234 · ⋮ · ✕ ┐
│ Homepage hero                                     (click to edit)│
│ [To do ▾]  ⚑ High ▾   📅 Due 15 Dec ▾   Website redesign →       │
│ ─────────────────────────────────────────────────────────────── │
│ Assignees   (MP) Maria P. ★ primary   (NK) Nikos K.   [+]       │
│ Dates       Start —   Due 15 Dec   Completed —                   │
│ Estimate    4h        Milestone  Launch ▾      Tags  web, design │
│ ─────────────────────────────────────────────────────────────── │
│ Description (rich text, sanitized)                               │
│ ─────────────────────────────────────────────────────────────── │
│ Subtasks 1/3                                    [+ Add subtask] │
│  ✓ T-1235 Copy from client                                       │
│  ○ T-1236 Hero image                      (MP)  📅 12 Dec        │
│ Checklist 2/5                                   [+ Add item]    │
│  ☑ Colours  ☑ Fonts  ☐ Mobile check  ☐ …                         │
│ ─────────────────────────────────────────────────────────────── │
│ Activity                                                         │
│  Maria P. moved this from To do to In progress · 2h ago          │
└──────────────────────────────────────────────────────────────────┘
```

- **Width:** 720 px on desktop, full screen on mobile. The board stays visible behind it on desktop, so you can move through tasks (`J`/`K` keys for next/previous in the current column, as in Linear's peek).
- **Every field saves on its own** (status, priority, dates, assignees, tags…) with `version`. A 409 shows the conflict toast and reloads that field, keeping a long description draft visible.
- **Subtasks** open in the same sheet (with a "← T-1234 Homepage hero" back link). A subtask has no "Add subtask" (one level only, WRK-02).
- **Assignees:** add several. The first one added becomes primary; ★ moves primary to another assignee. Exactly one primary when there are assignees (BR A).
- **Project / client:** a project task shows its project (and through it the client). A client task without a project shows the client. An internal task shows "Internal". `Move to project…` changes it; moving into a restricted project warns "Only members of {project} will see this task."
- **Finance links** (linked charge) come with Finance (Slice 5): nothing is shown until then.

### 3.6 Create forms

- **New project** (sheet): Name\*, Client (Combobox with search, or "Internal project"), Type, Status (default of the category Planned or Active), Visibility (Internal default; Restricted with the explanation line), Lead (defaults to me), Started on, Due on. Members are added afterwards from the details panel. Opening `New ▾ → Project` on a Client 360° page fills in the client.
- **New task** (dialog, quick): Title\*, then optional Project or Client, Status (the column's status when started from `+`), Assignee (defaults to me), Priority, Due. `Create and open` opens the sheet for the rest; `Create another` keeps the dialog open.
- **Complete project…** (dialog): Completed on (default today, can be in the past), status (one of the Completed-category statuses). When tasks are still open it says "{n} tasks are still open. They stay as they are." Completion changes nothing else: no charges, subscriptions or tasks (PRJ-08). Reopen clears the date and picks an Active status.

### 3.7 Settings → Projects & tasks

One settings section (`SettingsCard`s, existing pattern, lighter review):
- **Project statuses:** name (en/el), category, order (drag or up/down), default. **Task statuses:** the same with task categories.
- **Allowed task moves:** a matrix (from × to) of checkboxes, all allowed by default. Moves into the same category are always allowed.
- **Project types:** name (en/el), order.
- A status or type in use can't be deleted: the message says how many projects/tasks use it ("Used by 12 tasks. Move them to another status first.").

## 4. Layout

### Desktop (≥ 1280 px)

- Projects list: full-width `DataTable`.
- Project page: same frame as Client 360° (header, main column + 320 px details panel, `RouteTabs`). The Tasks tab drops the details panel so the board gets the full width.
- Board: columns 288 px wide, horizontal scroll inside the board area (not the page); each column scrolls on its own and loads more cards as you scroll (50 per page).

### Mobile (< 768 px)

```
┌ ← Tasks              [⋮] ┐
│ [Me ▾] [Open ▾]  🔍       │
│ To do (12)  In progr. (4) │  ← status chips, horizontally scrollable
│ ┌──────────────────────┐ │
│ │ T-1234  ⚑ High   ⋮   │ │  ← one column at a time, full-width cards
│ │ Homepage hero        │ │
│ │ 📅 15 Dec   (MP)     │ │
│ └──────────────────────┘ │
│ …                        │
│ [ + New task ]           │  ← sticky bottom action
└──────────────────────────┘
```

- **No dragging on phones.** The status chips switch the column; `⋮ → Move to…` (a bottom sheet listing only the allowed statuses) changes the status. Tapping a card opens the full-screen sheet with large controls.
- The projects list becomes cards (name, client, status, progress bar, due).

## 5. States

| State | Behaviour |
|---|---|
| Loading | Header skeleton; board shows column skeletons with 3 card placeholders each; the sheet shows field skeletons |
| Not found / no access | Project or task: one "This project (task) doesn't exist or you don't have access" page or sheet, the same message for both (no leak), with a link back |
| Empty | Projects: "No projects yet · New project". Board with filters: "No tasks match these filters · Clear filters". Empty column: a dashed drop area with "+ Add task" |
| Archived project | Banner "Archived on {date}. Tasks and files are kept." with `Unarchive`. Its tasks leave the global board (unless filtered by that project) and creating a task asks "This project is archived. Create anyway?" |
| Completed project | Success-tone banner "Completed on {date}" with `Reopen`; the board still works (late fixes happen) |
| Restricted project, non-member | Absent everywhere: lists, board, search, Client 360° tabs, files, activity. Finance masking ("Restricted project") comes with Finance |
| Viewer member / no `Tasks.Edit` | Board read-only (no drag, no move menu), sheet fields read-only; no disabled teasers |
| Move refused / conflict | Card returns, toast with the reason or the 409 "Reload" action |
| Partial failure | Per section (milestones, activity, a board column): "Couldn't load · Retry · Ref …" |
| Validation | Inline (`FieldError`); server rule errors with `code` (`work.subtaskDepth`, `work.transitionNotAllowed`, `projects.statusInUse`) map to fields or toasts |
| Long data | 200-character project names and task titles truncate (card: 2 lines; header: 1 line, tooltip); 300 tasks in one column stay smooth (paged columns); 40 members show "+32" |

## 6. Main flows and step counts

1. **Start a project for a client** (Manager): Client page `New ▾ → Project` → name, type, visibility → `Create` → land on the project → `Manage members` → add 2 people. About 6 actions.
2. **Move my task to In progress** (scenario 4): `/tasks` opens filtered to me → drag the card (or `⋮ → Move to… → In progress`, or in the sheet the status menu). 1–2 actions.
3. **Assign a task from the board** (scenario 4): card `⋮ → Assign to me`, or open the sheet → Assignees `+` → pick. 2–3 actions.
4. **Record finished work after the fact:** sheet → status Done → Completed date → pick the real date. 3 actions.
5. **Complete a project:** `Complete project…` → keep today or pick the real date → `Complete`. 2 actions, nothing else changes.
6. **Restricted project** (scenario 6 and S14): New project → Visibility Restricted → members. A staff member who isn't a member doesn't see it on `/projects`, the board, Ctrl+K, the client's Projects tab or its files.

## 7. Components

| Need | Existing | Gap |
|---|---|---|
| Page frames, tabs, details panel | `PageHeader`, `RouteTabs`, Client 360° layout (`ClientLayout` header + About panel pattern) | Generalize the record-page frame so project and client pages share it |
| Lists | `DataTable` (sort, columns, bulk), `TablePagination`, `useUrlFilters`, `StatusBadge`, `DateTime` | `ProgressBar` (shadcn `progress`) |
| Board | — | `TaskBoard` + `TaskCard`, drag and drop with keyboard support (**new dependency: `@dnd-kit`**, see decision S3-4) |
| Task sheet | `Sheet`, `ActivityTimeline`, `TagsInput`, `Combobox`, `DatePicker`, `MultiSelect`, `Avatar` | Inline-edit field pattern (`InlineField`), `AssigneePicker` (users with a primary star), checklist editor |
| Rich text description | — | Minimal sanitized rich text (bold, lists, links). Proposal: plain text with line breaks in Slice 3, rich text with notes in Slice 7 (decision S3-5) |
| Members | `Dialog`, user search used in Users admin | `MembersDialog` (user, role, remove) |
| Settings | `SettingsCard`, `SettingsNav`, `ConfirmDialog` | Sortable list rows; transition matrix |
| Files tab | `ClientFiles` / `RecordFiles` | Make `ClientFiles` a shared `RecordFilesTab` taking the record endpoint |

## 8. Acceptance criteria

1. **Restricted project (S14):** for an outsider with every Projects/Tasks permission, a restricted project and its tasks are absent from `/api/projects`, the board, task lists, `/api/search`, the client's Projects/Work tabs, files and activity, and direct URLs return 404. A member and Admin see everything. (E2E permission-matrix test plus domain tests.)
2. **Transitions:** a disallowed move is refused by the server with `work.transitionNotAllowed` from the API, from drag and drop and from bulk moves (all or nothing).
3. **Board order:** a drag stores one rank change; two people moving the same card → the second gets 409, and the order is never silently changed.
4. **Subtasks:** a subtask can't get children (`work.subtaskDepth`); progress counts subtasks only inside their parent; cancelled tasks are excluded; a completed project without tasks shows 100%.
5. **Assignees:** at most one primary per task, enforced by the database index; removing the primary promotes the next assignee.
6. **Completion:** completing or archiving a project changes no task, file or (later) charge or subscription; a past completion date is stored as given.
7. **Search:** `PRJ-2026-012` and `T-1234` open the project and the task sheet from Ctrl+K, only for users who can see them.
8. **Mobile (390 px):** no horizontal page scroll; status chips + move menu work without drag; the sheet is full screen.
9. **Keyboard:** a card can be moved between columns with the keyboard only, and the move is announced.

## 9. Usability scenarios to run

- **Scenario 4:** "Assign T-1236 to Nikos and move it to In progress", once from the board and once from the details sheet.
- "Which of Beta Ltd's projects are late?" from the Client 360° Projects tab.
- "Mark the logo task as done on 20 October" (finished earlier, entered now).
- **Scenario 7:** move a task to Done on a phone.
- Restricted project: sign in as a non-member and try to find it by name, by reference and through the client page.

## 10. References examined

Documentation pages were read; no product was inspected live.

| Reference | What we took | Risk noted | Source |
|---|---|---|---|
| **Linear: board layout, peek** | Columns default to status; hidden columns still accept drops; keyboard moves to top/bottom of a column instead of long drags; peek opens details from the board without leaving it | Peek is view-only in Linear; our sheet edits inline, so conflicts must be handled per field | [Board layout](https://linear.app/docs/board-layout), [Peek preview](https://linear.app/docs/peek) |
| **GitHub Projects: board and table layouts** | Board and table are two views of the same items; dragging between columns changes the column field's value | Grouping options are limited; we keep status columns only in this slice | [Customizing the board layout](https://docs.github.com/en/issues/planning-and-tracking-with-projects/customizing-views-in-your-project/customizing-the-board-layout) |
| **Jira: board moves are workflow transitions** (community answers, not official docs) | A card move is a transition and must exist in the workflow; users get confused when a drop silently bounces back, so we dim disallowed columns while dragging and name the reason when refused | Secondary sources | [Atlassian community: can't move a card despite transitions](https://community.atlassian.com/t5/Jira-Core-questions/Cannot-move-a-card-to-a-column-despite-workflows-and-transitions/qaq-p/1305389) |
| **Plane: project member roles** | Project content is visible only to people added to the project; project roles (admin, contributor, commenter, guest) control what members can do; only project admins manage members | Plane's role set is larger than we need; we keep Lead / Member / Viewer | [Member roles](https://docs.plane.so/roles-and-permissions/member-roles), [Permissions matrix](https://docs.plane.so/roles-and-permissions/permissions-matrix) |

## 11. Decisions to approve

| # | Decision | Proposal |
|---|---|---|
| **S3-1** | Time tracking (timer + manual logs, WRK-05) | **Not in Slice 3.** A small follow-up slice right after it, so this slice stays reviewable. |
| **S3-2** | Member roles | Global permissions decide what kind of action you may take; **Viewer** members are read-only in that project whatever their permissions; **Lead** is the accountable person (shown in lists, any number). Managing members needs `Projects.ManageMembers` and being able to see the project. |
| **S3-3** | Default statuses (seeded, editable in Settings) | Projects: Planned, Active, On hold, Completed, Cancelled (one per category). Tasks: To do, In progress, Blocked, Done, Cancelled; all moves allowed by default. |
| **S3-4** | Drag and drop library | **`@dnd-kit`** (behaviour only, no styling; keyboard and screen-reader support built in). |
| **S3-5** | Task description | Plain text with line breaks now; rich text arrives with notes (Slice 7), and existing text carries over. |
| **S3-6** | Default role permissions (new installs; existing roles unchanged, as with Clients) | Manager: all Projects.\* and Tasks.\*. Staff: Projects.View, Tasks.View/Create/Edit. Support: Projects.View, Tasks.View/Create. Finance: Projects.View, Tasks.View. |
| **S3-7** | Comments on tasks | With notes and mentions in Slice 7 (they need notifications that re-check access). The task Activity section covers history until then. |
