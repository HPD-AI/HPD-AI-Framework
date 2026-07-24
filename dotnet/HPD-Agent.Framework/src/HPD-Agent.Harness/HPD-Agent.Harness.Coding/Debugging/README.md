# HPD debugging runtime

This directory contains HPD's reusable Debug Adapter Protocol (DAP) runtime and the
model-facing implementation behind `CodingToolHarness.Debug`. The public boundary is one
closed discriminated `DebugOperation` union with 49 actions. Raw DAP requests, adapter JSON,
adapter executable commands, process handles, credentials, and protocol traces do not cross
that boundary.

The public function returns bounded XML and publishes typed structures through the
`coding.debug.*` metadata keys. Launch and attach are planned from semantic targets by trusted
execution planners and adapter factories. Planning is inert; long-lived runners start only
after a debug-tree identity has been reserved. Every subsequent operation uses opaque tree, session, source, frame,
variable, instruction, memory, location, and continuation tokens.

Continuation tokens are owner-, tree-, protocol-session-, generation-, and
query-bound. A caller continuing `getStackTrace`, `getVariables`, `getModules`,
`getLoadedSources`, `getCompletions`, or `disassemble` must repeat the originating
query arguments—including its page size—and change only the continuation token.
HPD reports `continuation_query_mismatch` when those arguments differ.

## Model-facing boundary

- `CodingHarness.Debug.Contracts.cs` defines the complete closed request union.
- `DebugExecutionPlanningService` performs target canonicalization, evidence discovery, and
  deterministic semantic planner selection.
- `IDebugExecutionTargetPlanner` implementations classify direct source, executable, .NET
  application-project, and .NET test targets and return inert execution plans.
- `DebugExecutionPlanActivator` starts tree-owned hosted resources, validates official
  readiness, and creates one trusted adapter start plan.
- `DebugExecutionStartOrchestrator` reserves ownership before activation and publishes the
  resulting protocol session and background handle.
- `DebugRuntimeServiceFactory` constructs invocation-local semantic facades over the
  runtime-owned session manager.
- `DebugPermissionMiddleware` classifies the already-bound request and records an
  invocation-local authorization decision; privileged proofs are minted only by
  `DebugPermissionAuthorizationService`.
- `DebugOperationDispatcher` exhaustively maps all 49 operations to semantic services.
- `DebugResultFormatter` bounds and escapes all model-facing XML.

`snapshot` and `inspectStop` are compound projections. They intentionally compose session
state, breakpoints, capabilities, stack/scopes/variables, and bounded output instead of
exposing protocol-shaped responses. Their bounded capability summary names supported and
unsupported optional public actions, execution options, and valid exception-filter IDs from
the capabilities negotiated with that live adapter and semantic projections HPD can satisfy
from authoritative adapter events. For example, `getModules` remains available when an adapter
emits module events but does not implement the optional DAP `modules` request. Models must use
this session evidence rather than adapter-name assumptions.
`getModules` labels adapter-request inventories as `Authoritative` and retained event
projections as `ObservedOnly`; an event-backed count is the bounded set HPD observed, not a
claim about every module the target has ever loaded.

Stopped-state projections preserve the adapter-designated focal thread independently from
the set of threads suspended by an `allThreadsStopped` event. `snapshot` exposes
`primary_stopped_thread_id`, and `getThreads` marks exactly that stopped thread as primary.
The model may omit `threadId` from inspection, stack, continue, and stepping requests to use
the focal thread safely. Explicit IDs remain available for intentional per-thread control.
Non-focal threads suspended by an all-thread stop do not inherit the focal thread's breakpoint
reason.

Resume and step state changes are transactional. HPD projects the requested single-thread or
all-thread resume scope while the adapter request is in flight, consumes a matching continued
event without double-counting it, and restores the stopped state if the adapter rejects the
request. A newer adapter event supersedes the pending transition and is never overwritten by
rollback.

The launch target union contains exactly:

- `sourceFile` for files an adapter can execute directly;
- `applicationProject` for evaluated executable projects;
- `executable` for existing artifacts;
- `test` for framework-semantic test execution.

Arguments belong to source, application, and executable targets. Test filters belong only to
the `test` target and are converted into discrete runner arguments. A test project cannot be
launched as an application, and a library cannot be treated as an executable merely because a
DLL exists. A managed artifact that exactly matches the evaluated output of a test project is
also rejected from direct executable launch and must use the semantic `test` target.

Exception breakpoint filters are validated after DAP initialization against the selected
adapter's negotiated `exceptionBreakpointFilters`. Initial and later exception mutations use
the same validator and protocol composer. Unknown filters, duplicate IDs, and unsupported
conditions fail locally as `invalid_exception_filter` before DAP transmission.

A launch without `stopOnEntry` or an initial source, function, or exception breakpoint remains
valid, but its public result includes `no_initial_stop_strategy` so an agent can relaunch a
short-lived inspection target deliberately. Retained terminal trees accept repeated terminate
as an evidence-preserving no-op; no protocol request is sent and retained snapshot/output data
is not removed.

Breakpoint results distinguish requested, adapter-acknowledged, verified, and pending state.
Pending breakpoints are expected when a module has not loaded and are never described as
confirmed. Verified means the adapter resolved the breakpoint; it does not prove that execution
hit it. Execution-control results explicitly mark prior suspension-bound tokens invalid and
direct callers to inspect the new stop. Successful variable and expression mutations likewise
mark prior variable-derived tokens invalid while preserving frame tokens and any fresh token
returned by the mutation. Natural termination disposes live sessions and owned host processes while retaining
a bounded, owner-scoped terminal record for status, snapshot, breakpoint, and output queries.

The canonical wire contract is generated from the pinned DAP schema under `Protocol/Generated`.
The generated feature matrix describes wire availability and semantic ownership. Runtime
capabilities are still negotiated per session; an adapter capability is never treated as
available merely because its generated wire type exists.

## Supported adapter declarations

| Adapter ID | Default transport | Status | Qualification evidence |
|---|---|---|---|
| `debugpy` | stdio | supported | Full launch, stop, threads, stack, continue, and termination against debugpy 1.8.21 |
| `netcoredbg` | stdio | supported | Direct launch plus public hosted xUnit, NUnit, MSTest, and source-generator flows against netcoredbg 3.2.0-1092 |
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
`DebugRealAdapterQualificationTests.cs`; execution-planning contract coverage lives in
`DebugExecutionPlanningV3Tests.cs`. The netcoredbg lane also starts a real
`dotnet test` runner, parses the official VSTest host-debug handshake, and
attaches to the exact reported testhost PID. `DebugPublicHostedRealAdapterTests`
qualifies that route through the generated public `CodingToolHarness.Debug`
function, production local process provider, ownership publication, restart,
cancellation, crash, natural-terminal, and repeated-lifecycle paths. Set only the variables for
adapters present on a CI worker:

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
  -f net10.0 --filter "FullyQualifiedName~DebugRealAdapterQualificationTests|FullyQualifiedName~DebugPublicHostedRealAdapterTests"
```

The .NET test strategy uses the official `VSTEST_HOST_DEBUG=1` handshake. HPD owns the
`dotnet test` process tree, parses the invariant testhost readiness message, and attaches
netcoredbg to the exact reported PID. It never scans the process table. Microsoft Testing
Platform is launched directly only when evaluated project metadata proves that its output is
the executable runner.

MSBuild evaluation is a bounded, network-isolated foreground query. It selects an exact
framework and `TargetPath`, classifies application/test/library shape, evaluates referenced
project outputs, and checks both each referenced artifact and the copy the debuggee will load.
Stale sources, projects, `Directory.Build.props`/`Directory.Build.targets`, referenced outputs,
or copied source-generator assemblies produce `debug_build_required` instead of silently
launching stale code.

Typed result metadata uses the `coding.debug.executionPlan`,
`coding.debug.executionActivation`, `coding.debug.projectEvaluation`,
`coding.debug.breakpointState`, `coding.debug.exceptionFilters`,
`coding.debug.launchNotices`, and `coding.debug.terminalRecord` keys. Durable lifecycle
events cover planning, activation, owned host start/readiness/exit, activation failure,
terminal retention, and classified terminal eviction without recording raw environment
variables, commands, or runner output.

## Production evidence

| Gate | Evidence |
|---|---|
| Strict framing and malformed input | `DebugProtocolFramerTests`; malformed JSON and envelope fault tests in `DebugProtocolClientTests` |
| Correlation, cancellation, progress, and reverse requests | `DebugProtocolClientTests` |
| Adapter crash and pending-request settlement | `Adapter_eof_and_disposal_settle_pending_requests_exactly_once` |
| Disposal and waiter cleanup | protocol lifecycle soak, owned-resource rollback, and terminal-record tests |
| Protocol and diagnostic resource limits | `DebugProtocolTransportTests`; client pending/event/reverse-request limit tests |
| Transport isolation | `DebugRuntimeBindingTests` verifies direct commands, captured environment bindings, approved endpoints, and shell-free execution |
| Ownership and authorization | `DebugExecutionStartOrchestratorV3Tests`, `DebugExecutionPlanningV3Tests`, `DebugPhase6FoundationTests`, and `DebugSessionProjectionTests` |
| Output, artifact, trace, and secret boundaries | `DebugSessionProjectionTests`, `DebugProtocolClientTests`, and `DebugHostRequestBrokerTests` |
| Native AOT | `test/HPD-Agent.Debugging.AotSmoke` |
| Real adapters | `DebugRealAdapterQualificationTests`, `DebugPublicHostedRealAdapterTests`, and the compatibility table above |
| Public model boundary | `DebugModelFacingTests`, `CodingHarnessGeneratedContractTests`, and the Native AOT smoke |

The generated-contract and AOT fixtures verify all four target shapes, rejection of removed
shapes and launch-level arguments, and exhaustive dispatcher reachability for all 49 operation
records.

## Security and lifecycle rules

- Adapter resolution is host-authorized and environment-scoped.
- Adapter commands are direct process fields, never shell command strings.
- Launch adapters remain fail-closed OS-isolated. Process-attach adapters run
  outside the process sandbox only after trusted planning, adapter trust,
  action-sensitive permission, target ownership, and activation revalidation;
  the current Environment contract cannot enforce a per-PID debugger grant.
- A hosted VSTest runner is an attach target and therefore uses the same narrow
  process-isolation exception as an explicit attach target. The current
  Environment contract cannot express both a per-PID debugger grant and
  VSTest's dynamically allocated local control endpoint. The exception is
  available only after launch permission, trusted semantic planning, fixed
  executable/argument construction, workspace containment, adapter trust,
  reservation, and activation revalidation; the entire runner process tree is
  then owned and stopped by the debug tree.
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
schema, verifies all 49 actions and all four target branches, rejects removed request shapes,
source-generated-binds representative nested, compound, and
privileged branches, materializes V3 result metadata and every new durable execution event, then
validates semantic adapter configuration and protocol framing. It
does not use reflection-based adapter discovery or runtime schema generation.
