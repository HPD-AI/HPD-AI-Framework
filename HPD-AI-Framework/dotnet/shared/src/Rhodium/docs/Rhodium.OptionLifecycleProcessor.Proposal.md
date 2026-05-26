# Rhodium Option Lifecycle Processor Proposal

**Document:** `Rhodium.OptionLifecycleProcessor.Proposal.md`  
**Version:** 1.0.0  
**Date:** May 2026  
**Status:** Proposal  
**Supersedes:** Distributed option expiry decisions across exchange, account, and option models  
**Depends On:** `InstrumentContract`, `OptionTerms`, `SimulationAccount`, `SimulatedVenueExchange`, `OptionLifecycleApplied`  
**Required By:** Simpler derivatives simulation, option lifecycle auditability, reduced exchange/account coupling

## Executive Summary

Rhodium's current options architecture is strong where it matters: canonical contracts, explicit option terms, settlement styles, assignment policies, auto-exercise rules, margin models, Greeks, and replay-visible lifecycle events.

The weakness is not the domain model. The weakness is orchestration. Option expiry and settlement decisions are currently distributed across:

- `ContractLifecycleScheduler`
- `SimulatedVenueExchange.ProcessDueOptionLifecycle`
- `SimulationAccount.ApplyOptionLifecycleResult`
- stale standalone option exercise models
- `DefaultOptionAssignmentModel`
- lifecycle reference price lookup in venue state and run config
- `SimulationPortfolioProjector`

This proposal keeps Rhodium's semantic advantages while reducing moving parts. It introduces a single option lifecycle decision component:

```csharp
OptionLifecycleProcessor
```

The processor decides what lifecycle outcomes should occur. The account applies those outcomes. The exchange only orchestrates due work and event emission.

The target shape:

```text
Exchange finds due contracts
OptionLifecycleProcessor decides lifecycle outcomes
SimulationAccount applies lifecycle outcomes using account-owned accounting
SimulationSession emits/projects lifecycle events
```

This gives Rhodium a simpler Nautilus-style operational path without losing Rhodium's richer derivatives semantics.

## Current Strengths To Keep

The proposal deliberately preserves these parts:

- `InstrumentContract` as the canonical instrument definition.
- `PayoffTerms.Option` and `OptionTerms` as the source of option contract truth.
- `OptionSettlementStyle`, `OptionExercisePolicy`, `OptionAssignmentPolicy`, `OptionPremiumStyle`, and `ExerciseStyle`.
- explicit `OptionLifecycleApplied` events.
- blocked lifecycle behavior for missing settlement/reference prices.
- physical delivery semantics.
- cash settlement semantics.
- assignment inputs from replay events or simulation config.
- option margin, strategy recognition, analytics, Greeks, and implied volatility models.
- account-owned cash, realized PnL, settled positions, and custody state.

These are the reasons Rhodium is stronger than a matching-engine-only approach for options.

## Problem To Avoid

The lifecycle path should not drift back into an account-centered decision tree.

The shape this proposal replaces is:

```text
SimulatedVenueExchange
    tracks last marks, settlement references, assignment inputs
    checks due contracts
    resolves reference prices
    calls account lifecycle entry point

SimulationAccount
    decides long exercise behavior
    decides short assignment behavior
    computes premium basis
    computes cash settlement
    computes physical delivery
    mutates cash/positions/realized PnL
    emits OptionLifecycleApplied events

SimulationSession
    drains account events
    projects lifecycle events into RhodiumRuntime
```

The result is a lot of places that must be read together to answer one question:

> At this timestamp, for this option position, what lifecycle outcome should happen and why?

The proposal makes that answer live in one place.

## Proposed Design

### New Component

Add a lifecycle processor in `Rhodium.Simulation.Exchange` or a new lifecycle namespace under `Rhodium.Simulation`:

```csharp
public sealed class OptionLifecycleProcessor
{
    public OptionLifecycleResult Process(OptionLifecycleRequest request);
}
```

The processor is deterministic and side-effect-free. It does not mutate the account directly. It returns domain-level lifecycle outcomes. The account remains the only owner of accounting state and translates those outcomes into cash, PnL, position, settlement, and custody changes.

### Request Shape

```csharp
public sealed record OptionLifecycleRequest
{
    public OptionLifecycleRequest(
        InstrumentContract contract,
        Qty quantity,
        OptionLifecycleReference reference,
        Instant now,
        SimulationOptionAssignmentInput? assignmentInput = null);

    public InstrumentContract Contract { get; }
    public Qty Quantity { get; }
    public OptionLifecycleReference Reference { get; }
    public Instant Now { get; }
    public SimulationOptionAssignmentInput? AssignmentInput { get; }
}
```

The request intentionally does not carry account-owned state such as average price, realized PnL, settled custody, or pending delivery. The processor receives only the position quantity needed to decide long/short lifecycle behavior. The account supplies accounting state while applying the returned outcomes.

The request is also a validation boundary. It must reject non-option contracts and zero quantities when constructed, and its properties must be construction-only so callers cannot create a valid lifecycle request and later patch it into an invalid one.

`OptionTerms` itself must be a construction boundary. It should reject unknown option enum values, nonpositive strike/multiplier/unit values, invalid activation/expiration ordering, invalid Bermudan exercise schedules, and non-Bermudan exercise schedules at construction time. Its properties should be construction-only so callers cannot use record `with` assignment to create malformed option terms after the fact. The processor should still fail closed defensively if corrupt option terms ever reach it, but the normal path should make such terms impossible to construct.

The reference object records the selected reference price and why it was selected:

```csharp
public sealed class OptionLifecycleReference
{
    public OptionLifecycleReference(
        Price? price,
        OptionLifecycleReferenceSource source,
        string? blockReason = null);

    public Price? Price { get; }
    public OptionLifecycleReferenceSource Source { get; }
    public string? BlockReason { get; }
}
```

Reference price selection may either stay in the venue as a small helper or move into the processor. The important thing is that the selected source is passed explicitly and appears on emitted events.

The reference type must reject contradictory states:

- missing price requires `OptionLifecycleReferenceSource.None`;
- missing price requires a nonblank block reason;
- resolved price requires a non-`None` source.
- resolved price must not carry a block reason.

### Result Shape

```csharp
public sealed class OptionLifecycleResult
{
    public OptionLifecycleResult(IReadOnlyList<OptionLifecycleOutcome> outcomes);

    public IReadOnlyList<OptionLifecycleOutcome> Outcomes { get; }
    public bool IsComplete { get; }
}
```

`OptionLifecycleResult` must reject null outcome lists, empty outcome lists, and null outcomes. It must snapshot the supplied outcomes during construction so caller-side list mutation cannot rewrite lifecycle history after the result is created. `IsComplete` is derived from the snapped outcomes, not supplied by callers. A single `Block` outcome is incomplete; otherwise the batch is complete. Blocked outcomes must not be mixed with settlement outcomes. Account position closure is derived from this result while applying the batch; individual outcomes must not carry close/remove-position instructions.

`OptionLifecycleOutcome` should be domain-level, not accounting-level:

```csharp
public abstract record OptionLifecycleOutcome
{
    public Qty Quantity { get; }
    public Instant AppliedAt { get; }
    public OptionLifecycleReferenceSource ReferenceSource { get; }
    public string Reason { get; }

    public sealed record Block : OptionLifecycleOutcome;
    public sealed record ExpireWorthless : OptionLifecycleOutcome;
    public sealed record ExpireUnexercised : OptionLifecycleOutcome;
    public sealed record ExpireUnassigned : OptionLifecycleOutcome;
    public sealed record CashSettle : OptionLifecycleOutcome;
    public sealed record PhysicalDeliver : OptionLifecycleOutcome;
}
```

The exact outcome names and payloads can change, but the rule should not: the processor decides the option lifecycle outcome; the account applies the accounting.

Outcome constructors should be explicit validation boundaries, not loose positional data bags. Only blocked outcomes may lack a reference price or use `OptionLifecycleReferenceSource.None`. Non-blocked outcomes such as `ExpireWorthless`, `ExpireUnexercised`, `ExpireUnassigned`, `CashSettle`, and `PhysicalDeliver` must carry a resolved `Price` and a non-`None` reference source. All outcomes must carry a nonzero quantity and a nonblank reason. Long-side outcomes such as `ExpireWorthless`, `ExpireUnexercised`, and exercise-initiated settlement must carry positive quantity. Short-side outcomes such as `ExpireUnassigned` and assignment-initiated settlement must carry negative quantity. Settlement outcomes such as `CashSettle` and `PhysicalDeliver` must also carry the initiating lifecycle kind, either `Exercise` or `Assignment`, so account code does not infer option law from position sign. Physical-delivery outcomes must also carry a nonblank premium/accounting reason.

`OptionLifecycleResult` should reject mixed-sign outcome batches. A pro-rata short assignment may produce multiple outcomes, but they must all be short-side outcomes. The account still checks exact coverage against the open position because only the account owns the current position quantity.

Expiration outcomes should not be overloaded:

- `ExpireWorthless` means the option was out of the money at expiry.
- `ExpireUnexercised` means a long option had value but was not exercised by policy.
- `ExpireUnassigned` means a short option or short residual quantity was not assigned.

The account must preserve the outcome reference source on every emitted lifecycle event that comes from the outcome. Physical exercise/assignment events, cash settlement events, and physical delivery events should all show the same selected source. Missing or defaulted reference sources are only valid for blocked lifecycle events.

The processor must not return low-level accounting instructions, account routing context, or account-owned state such as `AdjustCash`, `AddRealizedPnL`, `ClosePosition`, `AddSettledPosition`, strategy id, variant id, contract registration state, or position average price. Those create a shadow account system. The account should remain the only component that knows how lifecycle outcomes map onto account slices, cash, realized PnL, positions, pending delivery, settled custody, and statements.

`OptionAssignmentContext`, `OptionAssignmentRule`, and `OptionAssignmentDecision` are also validation boundaries. Assignment context properties must be construction-only. The context must carry a positive absolute short quantity and any pro-rata assignment ratio must be greater than zero and less than or equal to one. Assignment rules must reject negative minimum intrinsic value thresholds. Assigned decisions must carry a positive assigned quantity. Unassigned decisions must carry `Qty.Zero`. Contradictory assignment states should be impossible to construct, so the processor only needs to clamp valid assigned quantities down to the currently open short quantity.

The assignment model must also fail closed on unknown `OptionAssignmentPolicy` values. It should not reinterpret an unknown policy as "not assigned."

`SimulationOptionAssignmentInput` is a scenario/input validation boundary. Pro-rata assignment ratios must be greater than zero and less than or equal to one when the input is constructed, and supplied reason text must be nonblank. Its properties must be construction-only so invalid scenario data cannot be introduced later with record `with` assignment or object mutation. Invalid scenario data should fail before it reaches `DefaultOptionAssignmentModel`.

### Exchange Role

`SimulatedVenueExchange` should own venue orchestration, not option law.

It should:

- register expiring contracts;
- collect due contracts;
- provide available marks, settlement references, and assignment inputs;
- call `OptionLifecycleProcessor`;
- pass lifecycle outcomes to `SimulationAccount`;
- drain account events;
- emit lifecycle events.

It should not:

- decide whether a long option exercises;
- decide whether a short option assigns;
- compute cash settlement payoff;
- compute physical deliverable quantity;
- encode premium-style lifecycle accounting.

### Account Role

`SimulationAccount` should remain the authoritative owner of account state.

It should:

- reserve cash and margin for normal trading;
- apply fills;
- apply account transfers;
- apply option lifecycle outcomes;
- compute lifecycle cash adjustments;
- compute lifecycle position deltas and closures;
- track realized PnL;
- manage settled positions and pending delivery state;
- create account statements and custody snapshots.

It should not need to understand the whole option expiry decision tree. It should understand how a domain-level outcome changes account state.

The account still owns:

- premium basis;
- cash deltas;
- realized PnL;
- position close/removal;
- physical delivery bookkeeping;
- settled custody and pending delivery;
- statement and custody snapshot consistency.

The account must not reclassify processor outcomes based on accounting side effects. For example, a `CashSettle` outcome remains exercise- or assignment-initiated cash settlement even when premium basis makes net realized PnL zero. Zero realized PnL is an accounting result, not an `ExpireWorthless` lifecycle decision.

### Processor Role

`OptionLifecycleProcessor` should answer:

- Is the position long or short?
- Is the option in the money?
- Does policy permit long auto-exercise?
- Does policy permit short assignment?
- Does the assignment input select a random assignment?
- Does the pro-rata assignment ratio apply?
- Does assignment quantity need to be clamped to the open short quantity?
- Is settlement cash or physical?
- Is the settlement initiated by exercise or assignment?
- What domain-level outcome should occur?
- What deliverable and deliverable quantity result?
- Should the lifecycle block because no reference price exists?
- Which lifecycle event payload should be visible to replay?

This is the missing center of gravity.

## Proposed Runtime Flow

Runtime flow:

```text
Exchange due check
    -> Build OptionLifecycleRequest
    -> OptionLifecycleProcessor.Process(request)
    -> Account.ApplyOptionLifecycleResult(result)
    -> Account returns lifecycle application status
    -> Account emits OptionLifecycleApplied events
    -> Session drains and projects events
```

This is flatter, easier to test, and easier to reason about.

The processor decides domain outcomes. The account applies accounting. The exchange orchestrates due work.

### Scope Boundary

`OptionLifecycleProcessor` is option-only in the first implementation.

`SimulationAccount.ApplyOptionLifecycleResult` applies option lifecycle outcomes only. Binary and betting settlement use a separate cash-outcome lifecycle entry point:

- `PayoffTerms.Binary`
- `PayoffTerms.Betting`

Those paths are not the current source of lifecycle complexity and should not be pulled into the option processor. They should not emit `OptionLifecycleApplied`; that event stream is reserved for option exercise, assignment, settlement, delivery, expiration, and blocked option lifecycle decisions. A broader `ContractLifecycleProcessor` can be considered later only if binary and betting lifecycle behavior becomes complex enough to justify it.

## Behavior Contract

This is a breaking cleanup. The implementation should preserve correct financial behavior, replay semantics, and accounting results, but it should not preserve obsolete APIs, duplicate validation paths, compatibility wrappers, or stale lifecycle entry points.

Existing event types should remain:

- `OptionLifecycleApplied`
- `OptionLifecycleKind.Exercise`
- `OptionLifecycleKind.Assignment`
- `OptionLifecycleKind.ExpireWorthless`
- `OptionLifecycleKind.ExpireUnexercised`
- `OptionLifecycleKind.ExpireUnassigned`
- `OptionLifecycleKind.CashSettlement`
- `OptionLifecycleKind.PhysicalDelivery`
- `OptionLifecycleKind.Blocked`

Existing config should remain:

- `SimulationLifecycleConfig.SettlementReferencePrices`
- `SimulationLifecycleConfig.AssignmentInputs`
- `MissingReferencePricePolicy`
- `SimulationOptionAssignmentInput`

`SimulationLifecycleConfig` should be construction-owned. It must snapshot settlement reference prices and assignment inputs when constructed, reject null lifecycle inputs, reject unknown missing-reference policies, and expose explicit builder-style methods such as `WithSettlementReferencePrice`, `WithAssignmentInput`, and `WithMissingReferencePricePolicy`. Lifecycle config should not rely on record `with` assignment for core state.

`InstrumentContractValidator` should not duplicate option-term shape validation. `OptionTerms`, `OptionStrikeTerms`, and `PayoffTerms.Option` own local option construction invariants. The contract validator should only validate cross-object consistency, such as option lifecycle shape, lifecycle expiry matching the option expiration, and required underlying legs.

Projection behavior should preserve lifecycle quantities:

- `CashSettlement`, `ExpireWorthless`, `ExpireUnexercised`, and `ExpireUnassigned` reduce the runtime option position by the event quantity.
- `PhysicalDelivery` reduces the runtime option position by the event quantity and applies the deliverable position.
- nonzero cash flow adjusts runtime cash.

Projection must not flatten the whole option instrument from a partial lifecycle event. Pro-rata assignment can emit a `CashSettlement` for the assigned quantity followed by `ExpireUnassigned` for the residual quantity.

## Implementation Plan

### Phase 1: Add Golden Lifecycle Tests

Before moving code, add direct lifecycle tests around the current behavior:

- long cash-settled call ITM exercises and cash settles;
- long cash-settled call OTM expires worthless;
- manual long call ITM expires unexercised, not worthless;
- long physical call ITM creates deliverable;
- long physical put ITM creates short deliverable;
- short cash-settled call assigned;
- short random assignment not selected expires unassigned;
- short pro-rata assignment partially assigns and expires remainder;
- `OptionAssignmentDecision` enforces the assigned/unassigned quantity truth table;
- `OptionAssignmentContext` rejects nonpositive short quantities and invalid pro-rata ratios;
- `OptionAssignmentRule` rejects negative intrinsic value thresholds;
- `SimulationOptionAssignmentInput` rejects invalid pro-rata assignment ratios;
- valid assignment output cannot settle more than the open short quantity;
- blocked lifecycle results cannot mix blocked and settlement outcomes;
- lifecycle results cannot mix long-side and short-side outcomes;
- lifecycle outcome constructors reject side/kind mismatches such as positive assignment or negative exercise;
- account application rejects lifecycle batches that do not cover the open option position;
- settlement outcomes carry explicit `Exercise` or `Assignment` lifecycle kind;
- cash-settlement outcomes are not reclassified as worthless just because net realized PnL is zero;
- physical exercise, assignment, and delivery events preserve the processor-selected reference source;
- missing reference blocks;
- missing reference throws when configured;
- premium style accounting remains unchanged.

These tests are the guardrail against accidental cash/PnL or custody changes.

### Phase 2: Introduce Lifecycle Outcomes

Add `OptionLifecycleOutcome`, `OptionLifecycleResult`, and `SimulationAccount.ApplyOptionLifecycleResult(...)`.

Outcomes should be domain-level. They should describe what happened in the option lifecycle, not how account state is stored.

Examples:

- `Block`
- `ExpireWorthless`
- `ExpireUnexercised`
- `ExpireUnassigned`
- `CashSettle`
- `PhysicalDeliver`

The account should apply the result as one lifecycle batch using its existing accounting internals. The public account entry point should be named for the new lifecycle boundary, not for the old expiry-centric path.

`SimulationAccount.ApplyOptionLifecycleResult` must validate the batch before mutating account state:

- every outcome quantity must have the same sign as the open option position;
- outcome quantities must sum exactly to the open option position quantity;
- malformed batches must throw before cash, realized PnL, custody, or position state changes.

This prevents a partial lifecycle result from silently removing residual option exposure.

The account application result must not be a bare boolean. It should explicitly distinguish:

- `NoOpenPosition`: there was no account position to apply;
- `Blocked`: a blocked lifecycle event was emitted and the position remains open;
- `Completed`: the lifecycle batch was fully applied and the option position was closed.

### Phase 3: Extract The Processor

Move the lifecycle decision logic into `OptionLifecycleProcessor` and keep `SimulationAccount.ApplyOptionLifecycleResult` as an outcome application boundary.

The processor can initially call small internal helpers copied from `SimulationAccount`:

- intrinsic value calculation
- option in-the-money check
- assignment decision
- outcome selection for cash settlement vs physical delivery
- outcome selection for exercise, assignment, expiration, and blocked lifecycle

The processor should not copy premium basis, cash accounting, realized PnL, or custody accounting. Those stay in `SimulationAccount`.

At this point, stale wrapper APIs should be deleted instead of preserved. Binary and betting cash-outcome lifecycle settlement can remain in the account path, but the option decision branch should live only in `OptionLifecycleProcessor`.

### Phase 4: Slim The Venue

Keep `SimulatedVenueExchange.ProcessDueOptionLifecycle`, but reduce it to:

```text
copy due contracts
copy positions
resolve reference
load assignment input
process lifecycle
apply lifecycle result
drain emitted lifecycle events
mark complete/pending
```

The exchange should no longer encode option exercise/settlement semantics.

### Phase 5: Delete Duplicated Lifecycle Logic

Delete duplicated lifecycle decision logic from account/exchange after the processor tests and existing simulation architecture tests agree.

## Non-Goals

This proposal does not attempt to:

- rewrite the backtest engine;
- remove `SimulationAccount`;
- replace `SimulatedVenueExchange`;
- move cash, realized PnL, position, settlement, or custody ownership out of `SimulationAccount`;
- refactor binary or betting expiry in phase 1;
- remove option analytics, Greeks, or margin models;
- introduce external pricing dependencies;
- make options lifecycle match Nautilus synthetic-order semantics;
- redesign strategy hooks or generated contexts.

## Nautilus Comparison

Nautilus keeps option expiry simple by expressing expiry as synthetic orders and fills inside the matching engine. That is operationally clean, but it loses domain-level lifecycle events.

Rhodium should not copy that fully.

The useful lesson from Nautilus is not "hide lifecycle inside fills." The useful lesson is "put the operational path in one obvious place."

Rhodium should keep:

```text
explicit lifecycle events
explicit assignment/exercise policy
explicit settlement reference source
explicit cash vs physical semantics
```

But it should simplify the path to:

```text
one lifecycle processor decides
one account applies
one exchange orchestrates
```

## Risks

The main risk is accidentally changing cash/PnL accounting while extracting logic. To control that:

- preserve financially correct event payloads first;
- add golden lifecycle tests before moving outcome application;
- move code mechanically before redesigning types;
- delete stale wrapper APIs once call sites move to the new lifecycle entry point;
- keep premium basis, realized PnL, position close, and custody accounting in `SimulationAccount`;
- compare existing simulation architecture tests before and after.

Another risk is over-designing `OptionLifecycleOutcome`. Keep it domain-level. It should model lifecycle outcomes, not account internals. If an outcome starts looking like `AdjustCash`, `AddRealizedPnL`, or `ClosePosition`, it has crossed the boundary and should move back into account application logic.

## Success Criteria

This refactor is successful when:

- option lifecycle decisions can be understood by reading one processor;
- exchange code no longer contains option exercise or settlement rules;
- account code no longer contains the full option lifecycle decision tree;
- account code remains the only owner of cash, realized PnL, positions, settlement, and custody mutation;
- existing option lifecycle behavior remains correct;
- lifecycle tests are smaller and more direct;
- blocked lifecycle behavior remains replay-visible;
- strategy/runtime projections remain unchanged.

## Recommendation

Proceed with the refactor.

Rhodium should keep its derivatives-native model. That is the advantage. The simplification should target orchestration, not semantics.

The architecture should move from:

```text
rich domain model plus scattered lifecycle execution
```

to:

```text
rich domain model plus centralized lifecycle execution
```

That keeps the benefits of the current design while making the system easier to maintain, test, and extend.
