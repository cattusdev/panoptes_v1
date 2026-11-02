# Hankatt Operations — Planning Pack

**Status:** Gate 1 (read-only boilerplate audit) delivered, awaiting review. **No implementation is authorized** until Gates 1–3 are approved (see `IMPLEMENTATION-ROADMAP.md`).

This pack documents the agreed functional scope and the gated delivery process for building Hankatt Operations on the Panoptes v1 boilerplate.

## Start here

1. Read `PRODUCT-REQUIREMENTS.md` and `BUSINESS-RULES.md`.
2. Read `UX-DESIGN-STANDARD.md`, `SCREEN-PLANNING.md`, and `IMPLEMENTATION-ROADMAP.md`.
3. Read `BOILERPLATE-AUDIT-REPORT.md` (Gate 1): what the boilerplate already provides and what must be extended or built.
4. Review findings before agreeing to an architecture, migrations, or implementation.

## Files

| File | Purpose |
|---|---|
| `PRODUCT-REQUIREMENTS.md` | Consolidated PRD v1.1: modules, scope, acceptance scenarios |
| `BUSINESS-RULES.md` | Cross-module invariants and important edge cases |
| `UX-DESIGN-STANDARD.md` | UX research, design gate, usability and accessibility standard |
| `SCREEN-PLANNING.md` | Preliminary navigation, priority screens, UI review specifications |
| `IMPLEMENTATION-ROADMAP.md` | Ordered discovery/design/implementation milestones and gates |
| `BOILERPLATE-AUDIT-REPORT.md` | Gate 1: evidence-based capability/reuse matrix, risks, adoption path |
| `ARCHITECTURE.md` | Gate 2: architecture and domain design proposal (pending) |
| `ux/` | Gate 3: screen briefs for Dashboard, Client 360°, onboarding wizard (pending) |

The original agent prompts (`INIT-PROMPT`, `BOILERPLATE-AUDIT`) were not part of the pack as received; the audit followed the roadmap's Gate 1 description instead.

## Non-negotiables

- Internal single-organization system, not a tenant SaaS product.
- Preserve and reuse suitable existing boilerplate capabilities after evidence-based audit.
- Existing boilerplate UI is the primary design foundation.
- Historical completed projects and tasks can have **current unpaid balances**.
- Historical entries never create fabricated new-period receipts or retroactive communications.
- Official invoicing remains external (currently AADE timologio).
- Project and file authorization is enforced on the backend.
- Finance, renewal, and automation events must be idempotent and auditable.

Documentation is a functional baseline, not evidence that underlying code already exists.
