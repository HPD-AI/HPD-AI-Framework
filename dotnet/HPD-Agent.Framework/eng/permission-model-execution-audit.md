# Permission execution boundary audit

Invariant: no governed function body may execute while its execution owns an unsaved reusable approval change. Every framework body entry passes through `FunctionExecutionCore.ExecuteFunctionBodyAsync`, whose first operation is `PermissionStateCheckpoint.EnsureSaved`. Permission state and pending markers belong to the permission subsystem; `AgentContext`, `Session`, and `ISessionStore` gain no permission members.

## Reviewed boundaries

| Boundary | Admission and save path | Verification |
| --- | --- | --- |
| Agent single call | `Agent.cs` awaits the checkpoint after admission and before the core body method. | `ExecutionDriverSavesOnceAfterAdmissionBeforeAnyBody(1)` |
| Agent parallel batch | Admissions run serially; one awaited checkpoint precedes all body tasks. An interrupting denial cancels the batch before any body. | `ExecutionDriverSavesOnceAfterAdmissionBeforeAnyBody(3)`, `InterruptingDenialPreventsApprovedAndUnadmittedBatchBodies` |
| Provider protocol function executor | `AgentRuntimeFunctionExecutor` enters the same core; its sessionless context cannot authorize governed calls and fails with `PermissionJournalUnavailable`. | Direct lifecycle and full-suite coverage pass. |
| Sub-agent initial execution | `SubAgentRuntime` calls `childAgent.RunAsync`, which enters the guarded Agent boundaries above. | Source path inspected; `GeneratedAgentCapabilityInvocationTests` exercises the governed parent wrapper with its durable session. |
| Sub-agent continuation | The second `childAgent.RunAsync` call uses the same path. | Source path inspected; continuation enters the same guarded body entry. |
| Workflow agent node | `AgentNodeHandler` calls `agent.RunAsync(request, ct)`. | Source path inspected; `GeneratedAgentCapabilityInvocationTests` executes the multi-agent workflow path. |
| Parent tool harness | Parent journal append reports child execution; the harness has no function-body invocation. | Source inspection. |

Nested invocation wrappers in `McpRuntime`, `GoalMiddleware`, `SubAgentActions`, and `ReflectionToolFactory` execute a wrapped function from inside an already admitted `HPDAIFunction` body. They therefore share the outer core guard and authority verification. `McpRuntime` delegates to the original MCP function only after that guard.

## Merge gate

The merge gate passed on 2026-11-07. The final source audit found no unguarded top-level function-body path: the two Agent driver calls and `FunctionExecutionCore.ExecuteFunctionAsync` all enter `ExecuteFunctionBodyAsync`, whose first operation verifies the checkpoint. The full .NET suite, cross-version permission tests, TypeScript adapters, and Native AOT smoke pass. Repeat the body-call-site search after subsequent execution changes; route any added top-level boundary through the core guard and test its save-before-body order.

A checkpoint failure leaves the execution pending and prevents reuse of the failed in-memory Session object. Required permission serialization and the store write are fatal; unrelated middleware serialization retains its existing best-effort behavior. `RequiredSerializerFailureBlocksCurrentAndLaterUseOfFailedSessionObject`, `StoreFailurePreventsEveryBatchBody`, and `UnrelatedSerializerFailureDoesNotBlockRequiredApprovalSave` cover these distinctions.
