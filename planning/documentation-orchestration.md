# Rhodium Docs Goal Instructions

Internal orchestration instructions. Build docs in `/Users/ewoof/Desktop/Rhodium`.

## Goal

Create source-accurate docs for Rhodium users. Answer: what to write, where,
when it runs, what rules/errors matter, and which example to copy.
Internals only explain DX constraints, surprises, errors, or perf.

## Truth

Code/tests beat stale docs/proposals.

- Source truth: `HPD-OS/.../Rhodium/src`
- Tests-as-spec: `HPD-OS/.../Rhodium/test`
- Cookbook style: `HPD-Agent/cookbook/GettingStarted`
- Nautilus IA: `HPD-Agent-InternalDocs/.../nautilus_trader/docs`
- Existing docs/status: `HPD-OS/.../Rhodium/docs`
- Event proposal: `HPD-Agent-InternalDocs/.../Rhodium.EventHookArchitecture.Proposal.md`

## Style

Write operational docs, not implementation tours. Start from user code, then
explain the rule. Avoid beginner pages leading with dispatch loops, world-state,
tensors, or generator plumbing unless that explains visible behavior.


## Artifacts

Agents write files, not reports. Final chat: files changed, blockers, urgent
notes only.

Artifacts: `planning/briefs`, `planning/contracts`, `planning/reviews`,
`planning/verification`, `planning/runs`.

## Agent Modes
You can launch i belvie 5-6 at e same time yuo an check how mny but yeah . 
- Explorer: read assigned paths, write `planning/briefs/*`; do not edit docs.
- Synthesizer: write `planning/contracts/*`; do not write docs unless assigned.
- Writer: edit assigned docs directly; no unrelated files.
- Cookbook writer: edit assigned `.cs`; use `#:project` and `#:property TargetFramework=net10.0`.
- Reviewer: audit docs against source/tests; write `planning/reviews/*`; do not
  edit docs unless assigned.
- Verifier: build/run examples where feasible; write `planning/verification/*`;
  filter warning noise; report errors, failing tests, commands, relevant
  warnings only.
- Editor/integrator: reconcile outputs, normalize terms, fix navigation/links.

## Level Loop

Multi-run campaign. One turn/wave will not finish. Each level:

1. Inspect repo and planning artifacts.
2. Choose level shape: horizontal, vertical, or hybrid.
3. Choose objective.
4. Create `planning/runs/level-NNN/{orchestration.md,prompts/*.md,outcomes.md}`.
5. Launch agents with file/artifact contracts.
6. Review/integrate/reconcile outputs.
7. Record what changed, what is trusted, what remains open, and what comes next.

Horizontal = broad sections. Vertical = one user journey through docs/cookbook/
review/verification. Hybrid = broad discovery plus focused slices. Choose
adaptively; no rigid waterfall.

## Context Transfer

Each prompt transfers context: objective, mode, owned files/artifact, required
paths, source-truth rule, audience, relevant DX constraints, forbidden edits,
brief final response. Compact, not starved.

## Starting Direction

Start with horizontal exploration, then vertical slices: first file app, first
backtest, first strategy, generated fields/hooks, simulation matching/fills.

## Done

Stop when priority pages are written/scoped out; getting started reaches setup,
first strategy, first backtest; strategy authoring, simulation, and market model
cover core workflows; cookbook `.cs` files are filled and verified where
feasible; claims are source/test checked; terminology/navigation are consistent;
final review finds no major unsupported claims, missing first-run path, or broken examples.
