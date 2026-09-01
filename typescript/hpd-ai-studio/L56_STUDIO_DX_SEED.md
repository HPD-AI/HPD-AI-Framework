# L56 — Studio Developer Experience

## Status

Seed for review. L56 follows the existing L55 slice; it does not replace or renumber it.

## Intent

Turn the unified Studio foundation into a useful, polished, read-only developer inspection experience.

L52/L52A established Studio hosting, authentication, routing, module registration, finite observation, and the shared shell. L54 added generated scalar constraints, logical indexes, canonical schema authority, and safe provider evolution. L56 connects those foundations so developers can understand the system without reading raw contract payloads or internal identifiers.

The product rule is:

> Studio translates authenticated, finite authority into understandable developer information. It never invents state and never presents stale evidence as current.

## Scope

### 1. Stable shell and observation lifecycle

- Eliminate navigation and refresh flicker caused by overlapping observation generations.
- Preserve the current page while a replacement observation is pending.
- Replace content only after the new authorized generation has completed successfully.
- Distinguish initial loading, refreshing, stale, unavailable, denied, empty, and failed states.
- Bound every observation and retry; do not introduce polling loops with unbounded work.
- Keep desktop and compact layouts usable without changing the authority being presented.

### 2. Human-readable presentation contracts

- Replace raw labels such as `studio.section.summary`, `noItems`, and `Structured disclosed value` with stable product copy.
- Give every page and section a title, description, empty-state explanation, and evidence timestamp or generation where applicable.
- Preserve stable machine identifiers in an expandable technical-details surface.
- Format known scalar values, checksums, timestamps, counts, states, and identifiers consistently.
- Render unknown structured values safely as bounded, inspectable data rather than misleading prose.

### 3. Base Data explorer

- Show registered modules and collections.
- Provide collection detail views containing:
  - fields and wire names;
  - scalar kinds and formats;
  - required, optional, nullable, and missing semantics;
  - scalar and collection constraints;
  - relations and ownership;
  - logical indexes, compound parts, direction, null ordering, uniqueness, and predicates;
  - provider support and physical readiness;
  - schema generation and canonical checksum.
- Make empty collections and unsupported capabilities explicit rather than rendering generic empty cards.
- Do not add record mutation or schema-editing controls in this slice.

### 4. Schema and migration inspection

- Show accepted versus declared schema authority.
- Present compatibility, drift, readiness, and migration state.
- Render schema-plan operations as additions, alterations, removals, backfills, destructive changes, and blockers.
- Explain why an operation is safe structural, destructive, migration-required, unsupported, or drift-blocked.
- Surface provider evidence without exposing secrets or unrestricted raw payloads.
- Migration preview is read-only in L56; applying plans remains outside the Studio UX.

### 5. Useful module pages

- Overview summarizes application identity, runtime identity, provider readiness, schema generation, diagnostics, and attention items.
- Operations distinguishes registered reads, mutation operations, executions, and receipts with meaningful empty states.
- Automations distinguishes activations, schedules, effects, and pending work.
- Subjects explains contracts, consumers, barriers, lifecycle state, and retirement evidence.
- Search explains text/vector index availability, rebuild state, and attention items.
- Security renders policies, grants, explanations, and denials as recognizable structured rows.
- Infrastructure reports only disclosed provider/runtime authority and explains unavailable evidence.
- Diagnostics separates incidents, health, accounting, and contributor evidence.

### 6. Shared Studio design system

- Add reusable page headers, evidence badges, summary metrics, structured tables, definition lists, empty states, error states, skeletons, and technical-detail disclosures.
- Establish consistent spacing, typography, density, focus treatment, and responsive behavior.
- Keep module-specific presentation inside module packages while shared visual primitives remain in `hpd-studio-design`.
- Meet keyboard navigation, focus visibility, semantic landmark, contrast, and reduced-motion requirements.

## Authority and safety requirements

- Studio consumes registered presentation/observation contracts; it does not query provider internals directly.
- All displayed data is permission-filtered before it reaches presentation code.
- A newer pending observation cannot erase the last successfully authorized view.
- A failed refresh cannot silently retain prior data as if it were current.
- Stale evidence, when policy permits it to remain visible, is visibly and consistently marked stale.
- Unknown modules, views, values, and contract versions fail closed with bounded diagnostics.
- No compatibility retrofit is required: HPD remains pre-1.0 and obsolete Studio contracts may be removed rather than maintained in parallel.

## Explicit non-goals

- Editing collection schemas.
- Applying or approving migrations.
- Creating, updating, or deleting records.
- Executing arbitrary provider queries.
- Security-policy or grant administration.
- Automation control, activation cancellation, or destructive lifecycle actions.
- Replacing Base, Gateway, or module authority with Studio-owned state.

## Suggested delivery order

1. Stabilize shell observation replacement and navigation state.
2. Establish shared presentation primitives and human-readable copy.
3. Implement the Base overview and Data explorer against L54 contracts.
4. Implement schema and migration-plan inspection.
5. Upgrade Operations, Automations, Subjects, Search, Security, Infrastructure, and Diagnostics.
6. Complete responsive, accessibility, browser, and failure-state qualification.

## Completion matrix

L56 is complete only when:

- navigation across every registered page does not blank or flicker during normal refresh;
- initial load, refresh, stale, denied, unavailable, empty, and failed states have deterministic tests;
- Base collections expose fields, constraints, relations, and L54 logical-index details;
- schema generation, checksum, readiness, drift, and migration previews are understandable without raw contract knowledge;
- raw localization keys and generic structured-value placeholders are absent from normal supported views;
- desktop and compact layouts pass browser coverage;
- keyboard and accessibility checks pass for navigation, tables, disclosures, loading, and errors;
- unauthorized fields and actions are absent rather than merely disabled;
- source tests, type checks, package builds, generated assets, host integration tests, and browser evidence all pass;
- the unified v9/L52A integration preserves current main/L53/L54 framework authority.

## Review questions

- Do the current Studio observation contracts expose enough finite metadata for collection and schema detail pages, or does L56 require narrowly scoped presentation-contract additions?
- Which migration-plan explanations belong in Base-owned descriptors versus Studio-owned copy?
- Which evidence may remain visible while stale, and which views must disappear immediately when refresh authority fails?
- Should large structured values use a shared bounded inspector in L56, or should unsupported shapes remain summarized until a later slice?

