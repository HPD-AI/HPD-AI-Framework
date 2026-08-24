# L52A — Unified Studio Forward Integration onto L53

Status: corrective integration slice and prerequisite for L54.

## Immutable baselines

- Integration base: `main@660ae3d0`
- Studio donor: `6af21b0e`
- Historical branch point: `dcd6350b`
- Main is the framework authority.
- `6af21b0e` is a selective donor, never the merge authority.

HPD is pre-1.0. Make clean forward contract changes where required. Do not add compatibility shims, legacy aliases, duplicated authority, or retrofit layers for unreleased behavior.

## Objective

Forward-integrate the unified Studio architecture and selected newer product work onto current L53 main without regressing any L47–L53 authority, provider semantics, or conformance guarantees.

The governing rule is:

> Main owns framework authority and durable semantics. `6af21b0e` owns the unified Studio architecture and selected newer product work.

## Prohibited integration methods

- Do not merge `6af21b0e` wholesale.
- Do not replace complete directories from either branch.
- Do not use broad `ours` or `theirs` conflict resolution.
- Do not resurrect older BASE, SQLite, Graph, client, activation, lifecycle, mutation, receipt, scheduling, or search implementations.
- Do not create parallel Studio-owned copies of BASE authority.
- Do not resurrect `ExecutionManager.cs`.
- Do not manually edit generated clients, bundles, locks, ABI checksums, content digests, or embedded asset manifests.

## Ownership

### Current main

Main remains authoritative for:

- BASE transactions, atomic execution, receipts, replay, and module mutations;
- activations, schedules, and L53 semantic activations;
- lifecycle, retirement, full-text search, policy, and authorization;
- InMemory and SQLite provider semantics and certification;
- generated BASE client contracts;
- Gateway runtime contracts;
- Graph scheduling and workflows.

Studio may observe or invoke this authority only through bounded current-main contracts.

### Studio donor

Forward-port and adapt:

- Platform Studio application graph, authentication, bootstrap, module catalog, runtime contributions, routes, observations, commands, resources, leases, late-work handling, and content-addressed assets;
- Studio Core canonical contracts, activation ABI, runtime map, routing, navigation, pages, observations, commands, activities, resources, preferences, and bootstrap processing;
- shared Studio design components and unified shell source;
- BASE Studio presentation and bounded runtime contributions;
- unified Gateway and Graph Studio presentation/host adapters.

Generated assets are rebuilt after source integration.

## BASE Studio integration

Forward-port the Studio-only authority projections as additive seams on current main:

- immutable BASE Studio authority snapshot;
- principal mapping and response-authority validation;
- bounded observations and commands;
- control inspection and dynamic store-authority capture;
- evidence and infrastructure inventories;
- diagnostics, security, search, subjects, operations, and automation projections.

Do not replace the underlying BASE implementations.

### L53 semantic activations

Semantic activations become first-class unified Studio resources while main retains identity, version, checksum, binding/key identity, lifecycle, execution, receipts, inspection, and maintenance authority.

The integration must:

- add semantic activation to the Studio definition/resource taxonomy;
- include semantic authority in Studio authority/version checksums;
- register a semantic-activation page and bounded observations;
- expose only commands backed by existing L53 control authority;
- adapt the current semantic presentation/controller to the unified component and observation model;
- never create parallel semantic state or disclose protected keys/provider rows.

## Providers

Add current-main-compatible InMemory and SQLite implementations for dynamic store authority, control inspection, bounded evidence, and infrastructure inventory. These are additive interfaces or partials.

Do not import the donor's older schema initializer, activation tables, mutation persistence, lifecycle/retirement persistence, search implementation, or provider registration authority.

Unavailable infrastructure metadata must produce an intentional bounded unavailable or empty result, not fail the complete workspace.

## TypeScript and product modules

Current main remains the generated BASE-client source of truth. Resolve C# contracts, regenerate the client, preserve all L53 codecs/DTOs/worker/semantic operations, then add Studio-facing adapters.

Use the unified BASE module descriptor and activation ABI as the structural base. Every registered backend page must have an exact frontend binding, including semantic activations.

For Gateway and Graph, port only unified host/presentation adapters. Preserve current main runtime authority and the deletion of `ExecutionManager.cs`.

Remove obsolete separate `.Studio` packages only after a reference scan and complete build prove all consumers use unified Platform hosting.

Audio and Payments are ported in separate commits after BASE plus Studio is green. Neither may restore older BASE adapters. Donor fixes already superseded by main are excluded.

## Required sequence

1. Create an integration branch from `main@660ae3d0`.
2. Record a disposition for every overlapping path.
3. Port Studio Core and tests.
4. Port Platform Studio hosting and tests.
5. Port design components and shell source.
6. Add unified BASE Studio contracts and runtime contributions.
7. Adapt BASE Studio authority to L53, including semantic activations.
8. Add current-main-compatible InMemory and SQLite projections.
9. Integrate the BASE TypeScript module with regenerated L53 clients.
10. Port Gateway and Graph Studio adapters.
11. Remove obsolete Studio packages only after reference verification.
12. Make the combined BASE and Studio matrices green.
13. Port Audio and Payments in separate commits.
14. Regenerate all artifacts deterministically.
15. Validate through HPD Cloud.

Every overlapping path receives exactly one ledger disposition:

- `main`
- `Studio addition reapplied`
- `generated`

## Deterministic generation order

1. Resolve authoritative C# contracts.
2. Regenerate clients.
3. Resolve package manifests.
4. Regenerate package locks.
5. Build Studio sources.
6. Recompute frontend ABI and content digests.
7. Update embedded resources through the normal build.
8. Rebuild hosts.

## Reviewable commit structure

Keep Studio Core, Platform host, design/shell, BASE contracts, InMemory, SQLite, L53 semantic integration, Gateway/Graph, obsolete-package removal, Audio, Payments, and generated artifacts independently reviewable.

## Acceptance

L52A completes only when the following pass from the same main-based integration commit:

- full current BASE and SQLite suites;
- the current L47–L53 conformance matrix without skipped regression categories;
- unchanged transaction/receipt, mutation, lifecycle/retirement, search, activation/schedule, and semantic-activation conformance;
- Platform Studio host, Studio Core, shell, design, BASE Studio, Gateway Studio, and Graph Studio tests/typechecks/builds;
- InMemory and SQLite projection tests for every Studio evidence family;
- route/component coverage for every registered page;
- semantic-activation inspection through unified Studio;
- populated, empty, and unavailable infrastructure states;
- navigation and session renewal without stale observation replacement;
- strict CSP;
- negative tests proving inspection cannot mutate authority, bypass L53 authorization, or disclose protected/undisclosed values;
- deterministic double generation/build of clients, locks, bundles, ABI checksums, and content-addressed paths;
- reference proof that obsolete `.Studio` APIs and `ExecutionManager.cs` were not resurrected;
- HPD Cloud authentication, routing, renewal, semantic inspection, infrastructure, CSP, and served-asset digest smoke tests;
- Audio and Payments native/reproducibility matrices after their separate commits.

Passing the donor branch's Studio matrix alone does not complete L52A. The current L47–L53 authority matrix and the forward-ported Studio matrix must pass together.
