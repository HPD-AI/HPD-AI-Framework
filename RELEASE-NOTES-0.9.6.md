# HPD-Agent 0.9.6

## Release status

HPD-Agent 0.9.6 is a pre-1.0 stabilization release. Public APIs, serialized
events, persistence formats, hosted DTOs and routes, generated SDK contracts,
and TUI integration contracts remain compatibility candidates for 1.0.
Breaking changes remain possible until 1.0.0 is released.

## Highlights

- The session-first TypeScript SDK now covers the generated runtime surface,
  including stored agents, sessions, threads, content, executions, subagents,
  evaluations, observations, history projection, and client tools.
- Portable OpenAPI authoring schemas preserve generated component types instead
  of exposing broad untyped policy and operation fields.
- Agent definitions support partial updates with explicit field clearing,
  metadata clearing, and closed unknown-field handling.
- Tool-harness builder configuration and named executable policies materialize
  into durable policy and runtime configuration.
- Client-tool harness and definition aliases now derive from generated models,
  including provider-authentication discriminants and reusable client-family
  configuration.
- Headless thread state exposes extensible projections, tool work parts,
  timeline and revision controls, branch navigation, and fork controls.
- HPD-OS validates the framework against a real downstream application and now
  builds against the current framework main.

## Breaking changes

- HPD-Agent runtime packages now target .NET 10 LTS and .NET 11 STS. .NET 8 and
  .NET 9 are no longer supported because they reached end of support on
  2026-11-10.
- Generated SDK models use the normalized server contract. Consumers should use
  the exported generated definitions and helpers instead of maintaining
  parallel portable interfaces.
- `AgentRunConfig` is the one execution configuration surface. Durable agent
  behavior belongs to serialized agent definitions.

## SDKs

The TypeScript client continues to expose the session-first API while its model
and service layers come from SDK IR generated from the hosted OpenAPI document.
Generated C#, Python, and Go clients use the same normalized contract.

The TypeScript client-tool runtime continues to provide automatic invocation
handling, responder routing, replay validation, and background operation fields.
Typed tool-schema authoring and first-class background continuation helpers are
planned for a later pre-1.0 release.

## Validation

The 0.9.6 release gate covers:

- .NET 10 and .NET 11 solution builds.
- Windows, macOS, and Linux verification jobs.
- The agent and environment test suites.
- Generated API-surface checks.
- C#, TypeScript, Python, and Go SDK generation and conformance.
- TypeScript runtime, type, and conformance tests.
- Headless UI and Svelte checks.
- NuGet package identity, contents, dependency, and availability validation.

## Release train

The combined `Release HPD Agent and Dependencies` workflow publishes the
release-train dependencies in dependency order, then validates and publishes the
Agent packages against those exact versions.

## Deprecated packages

- `HPD-Agent.ToolHarness.FileSystem` remains deprecated and is not a supported
  workspace-isolation or command-execution boundary.

## Upgrade

Applications targeting .NET 8 or .NET 9 must retarget to `net10.0` or
`net11.0`. Applications with local copies of portable tool, harness, client, or
provider-authentication shapes should use the generated public types exported by
`HPD-Agent` and the generated clients.
