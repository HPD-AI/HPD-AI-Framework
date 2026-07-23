# HPD debugging runtime

This directory contains HPD's reusable Debug Adapter Protocol (DAP) runtime and the
model-facing implementation behind `CodingToolHarness.Debug`. The public boundary is one
closed discriminated `DebugOperation` union with 49 actions. Raw DAP requests, adapter JSON,
adapter executable commands, process handles, credentials, and protocol traces do not cross
that boundary.

The public function returns bounded XML and publishes typed structures through the
`coding.debug.*` metadata keys. Launch and attach are planned from semantic targets by trusted
adapter factories. Every subsequent operation uses opaque tree, session, source, frame,
variable, instruction, memory, location, and continuation tokens.

## Model-facing boundary

- `CodingHarness.Debug.Contracts.cs` defines the complete closed request union.
- `DebugStartPlanningService` performs target canonicalization, adapter selection, trust
  evaluation, configuration composition, and launch-plan construction.
- `DebugRuntimeServiceFactory` constructs invocation-local semantic facades over the
  runtime-owned session manager.
- `DebugPermissionMiddleware` classifies the already-bound request and records an
  invocation-local authorization decision; privileged proofs are minted only by
  `DebugPermissionAuthorizationService`.
- `DebugOperationDispatcher` exhaustively maps all 49 operations to semantic services.
- `DebugResultFormatter` bounds and escapes all model-facing XML.

`snapshot` and `inspectStop` are compound projections. They intentionally compose session
state, breakpoints, capabilities, stack/scopes/variables, and bounded output instead of
exposing protocol-shaped responses.

The canonical wire contract is generated from the pinned DAP schema under `Protocol/Generated`.
The generated feature matrix describes wire availability and semantic ownership. Runtime
capabilities are still negotiated per session; an adapter capability is never treated as
available merely because its generated wire type exists.

## Supported adapter declarations

| Adapter ID | Default transport | Status | Qualification evidence |
|---|---|---|---|
| `debugpy` | stdio | supported | Full launch, stop, threads, stack, continue, and termination against debugpy 1.8.21 |
| `netcoredbg` | stdio | supported | Full launch flow against netcoredbg 3.2.0-1092 and .NET 8 |
| `lldb-dap` | stdio | supported | Full launch flow against Apple LLDB-DAP and a compiled Rust executable |
| `gdb` | stdio | supported when installed | Generated/configuration/unit coverage; real-adapter qualification not yet run on this host |
| `codelldb` | TCP server | disabled by default | Generated/configuration/unit coverage; real-adapter qualification not yet run on this host |
| `delve` | stdio/TCP | supported when installed | Generated/configuration/unit coverage; real-adapter qualification requires Go and Delve |
| `javascript` | TCP server | experimental | Official vscode-js-debug 1.117.0 starts, connects, and initializes; standalone launch is not qualified |
| `rdbg` | stdio/socket | supported when installed | Generated/configuration/unit coverage; real-adapter qualification requires modern Ruby and rdbg |

An unavailable optional adapter is not a debugger-runtime failure. Selection returns bounded
installation guidance and does not execute an unapproved candidate.

## Real-adapter qualification

Real-adapter tests are opt-in. Core protocol qualifications live in
`DebugRealAdapterQualificationTests.cs`; public generated-function qualifications live in
`DebugSessionStartOrchestratorTests.cs`. Set only the variables for adapters present on a CI
worker:

```text
HPD_DEBUGPY_PYTHON=/path/to/python-with-debugpy
HPD_LLDB_DAP=/path/to/lldb-dap
HPD_RUSTC=/path/to/rustc
HPD_NETCOREDBG=/path/to/netcoredbg
HPD_DOTNET=/path/to/dotnet
HPD_NODE=/path/to/node
HPD_JS_DEBUG_SERVER=/path/to/dapDebugServer.js
```

Run the suite with:

```text
dotnet test test/HPD-Agent.Harness.Coding.Tests/HPD-Agent.Harness.Coding.Tests.csproj \
  -f net8.0 --filter FullyQualifiedName~DebugRealAdapterQualificationTests
```

The primary Linux/.NET 10 CI job provisions pinned debugpy and netcoredbg distributions, verifies
the netcoredbg archive checksum, and runs their core and public-function qualifications without a
skip path. The public qualification crosses generated request binding, trusted planning, adapter
selection, launch, initial breakpoints, stopped-state stack/scopes/variables, continue-to-exit,
bounded output snapshotting, and cleanup. The current locally verified set contains three full
core implementations—debugpy, LLDB-DAP, and netcoredbg—and the public debugpy path. JavaScript is
recorded separately because TCP initialization alone is not launch qualification.

## Production evidence

| Gate | Evidence |
|---|---|
| Strict framing and malformed input | `DebugProtocolFramerTests`; malformed JSON and envelope fault tests in `DebugProtocolClientTests` |
| Correlation, cancellation, progress, and reverse requests | `DebugProtocolClientTests` |
| Adapter crash and pending-request settlement | `Adapter_eof_and_disposal_settle_pending_requests_exactly_once` |
| Disposal and waiter cleanup | protocol lifecycle soak plus repeated tree start/termination tests |
| Protocol and diagnostic resource limits | `DebugProtocolTransportTests`; client pending/event/reverse-request limit tests |
| Transport isolation | `DebugRuntimeBindingTests` verifies direct commands, captured environment bindings, approved endpoints, and shell-free execution |
| Ownership and authorization | `DebugSessionStartOrchestratorTests`, `DebugPhase6FoundationTests`, and `DebugSessionProjectionTests` |
| Output, artifact, trace, and secret boundaries | `DebugSessionProjectionTests`, `DebugProtocolClientTests`, and `DebugHostRequestBrokerTests` |
| Native AOT | `test/HPD-Agent.Debugging.AotSmoke` |
| Real adapters | `DebugRealAdapterQualificationTests`, public real-adapter cases in `DebugSessionStartOrchestratorTests`, and the compatibility table above |
| Public model boundary | `DebugModelFacingTests` and the model-facing lifecycle cases in `DebugSessionStartOrchestratorTests` |

The public-boundary lifecycle fixtures invoke the generated `Debug` function, not only the
semantic service. They cover generated binding, semantic launch planning, adapter trust and
selection, configuration composition, session publication, snapshot metadata, termination,
reverse `runInTerminal`, `startDebugging` child sessions, real debugpy execution, and exhaustive
dispatcher reachability for all 49 operation records.

## Security and lifecycle rules

- Adapter resolution is host-authorized and environment-scoped.
- Adapter commands are direct process fields, never shell command strings.
- Remote TCP/Unix endpoints require approved opaque endpoint identity and authority.
- Launch and attach configuration is composed from closed semantic inputs; arbitrary model JSON
  is not forwarded.
- Adapter output and evaluated values are untrusted data. Inline output, traces, diagnostics,
  memory, disassembly, and artifacts have independent bounds.
- Raw protocol traces are opt-in host diagnostics, redacted, and never model-visible.
- Tree ownership is checked on every semantic operation.
- Capability removal takes effect immediately.
- Protocol faults, adapter exits, cancellation, disposal, publication rollback, failed child
  starts, and failed disconnects all settle waiters and release owned resources exactly once.

## Native AOT

The AOT smoke project creates the complete Coding harness, materializes the public Debug
schema, verifies all 49 actions, source-generated-binds representative nested, compound, and
privileged branches, then validates semantic adapter configuration and protocol framing. It
does not use reflection-based adapter discovery or runtime schema generation.
