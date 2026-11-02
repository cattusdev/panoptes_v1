# Hankatt Operations — UI/UX Design & Quality Standard

**Applies to all major UI features.** UI is a product-quality requirement, not a final visual-polish phase.

## Principles

1. **Workflow-first:** define user, top tasks, essential context, decisions and success criteria before layout.
2. **Existing visual system first:** inventory actual boilerplate tokens, layout, form, table, overlay, navigation, accessibility and responsive patterns before adding any new library or styling system.
3. **Clarity over decoration:** clear hierarchy, readable type, spacing, restrained color, no gratuitous nested cards, oversized typography, animations or decorative clutter.
4. **Consistency:** shared patterns across clients, projects, tickets, finance, files and automation.
5. **Accessibility and keyboard:** use keyboard navigation, focus states, labels and helpful errors; don't rely on color or hover alone.
6. **Mobile is a distinct experience:** don't just compress desktop boards, tables and dashboards. Provide appropriate mobile interactions and PWA install behavior.
7. **Safety:** clear financial amounts/units/statuses; destructive changes require guardrails and meaningful recovery. Draft states, loading, empty, error and success are designed intentionally.
8. **Authorization:** no UI affordance may leak project-restricted data; hiding buttons is not security. Server must enforce access.

## Reference research (when accessible)

For each major view, inspect 2–3 *relevant patterns*, not competitors as whole products. Possible references:

- Linear — tasks, issue detail, command navigation, streamlined workspace.
- Attio — CRM records, entity details, linked data, list previews.
- Plane / Jira / YouTrack — Kanban, grouping, saved filters, ticket flows.
- Invoice Ninja / ERPNext — charge/payment visibility and renewal-oriented finance screens.

Record *actual observed patterns and source links*; distinguish live inspection from promotional screenshots or general assumptions. Do not copy proprietary branding, assets or screen designs.

Research should focus on: information hierarchy, main actions, navigation depth, progressive disclosure, error prevention, bulk workflows, keyboard/touch interactions and common user mistakes.

## Required UX design gate for major screens

Before code, deliver a short screen brief containing:

- User persona/role and top 3 goals.
- Primary and secondary actions.
- List of fields/data sorted by priority.
- Required loading, empty, error, validation, confirmation and permission-denied states.
- Main user flow and minimum necessary steps.
- Desktop layout concept + mobile layout/interaction concept.
- Matching existing boilerplate components and any confirmed gaps.
- References examined, observed strengths and risks.
- Wireframe/prototype or well-described layout, sufficient for review.
- Usability scenarios and acceptance criteria.

**Gate:** No major screen implementation until the above is reviewed/approved. Simple forms may follow established, approved patterns through a lighter review.

## Usability verification

Use realistic cases, not pristine dummy records: large lists; long client names; historic completed tasks still unpaid; restricted projects; bundle domains expiring before package; poor connection; validation errors; empty histories; crowded Kanban.

Track: whether task succeeded; steps/time; errors and recovery; whether status and financial meaning were understood; ability to use by keyboard and on mobile. Do not assert that a design is usable solely from screenshots.

### Priority scenarios

1. Find an overdue client balance without mistaking old paid work for new revenue.
2. Create existing client + historical completed project + completed unpaid €400 task through wizard.
3. Find hosting or domain nearing expiry and tell agreement, paid and provider-fulfilled status apart.
4. Assign task/change status from board and from details.
5. Convert support ticket to task without marking work done.
6. Upload/reuse files without exposing restricted contents.
7. Complete essential actions on mobile without hover controls or dense, unusable grids.

## UI patterns to standardize

Page header, breadcrumbs, main action, details panel, tabbed record page, searchable data table, filter toolbar, status badge semantics, task card, field validation, quick-create, confirmation dialog, sheet/drawer, file picker, activity timeline, notification, empty state, and read-only permission state.

## Definition of Done for major frontend features

- User flow and reference study documented.
- Existing design tokens/components reused where appropriate.
- Responsive and keyboard interaction inspected.
- Permissions verified end-to-end.
- Loading/empty/error/success states complete.
- Realistic usability scenarios tested, issues recorded and resolved or acknowledged.
- No new design framework without justification and approval.

## Workflow

**Source audit → targeted reference study → user-flow definition → information architecture → wireframe → review/approval → implementation → usability validation.**

This standard defines the process; it does not authorize implementation during the initial audit.
