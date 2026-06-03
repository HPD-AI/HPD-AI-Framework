# Realtime Parity Test Audit

This audit maps existing `IChatClient`-driven agent behavior tests to realtime transport coverage. The goal is not to copy chat tests line-for-line. The goal is to preserve the same behavioral contracts now that `Agent.cs` can run model turns through either `IChatClient` or `IRealtimeClientSession`.

Repository scan at audit start:

- `123` `*Tests.cs` files in `test/HPD-Agent.Tests`
- about `1,783` `[Fact]` / `[Theory]` tests
- high-signal folders for realtime parity: `Phase0_Characterization`, `Core`, `Middleware/V2`, selected `Middleware`, selected `Session`

## Audit Method

The test suite is too large to review test-by-test. Treat chat behavior as a contract inventory:

1. Cluster tests by behavioral intent.
2. Skip pure serialization, source generation, content store, secret, and branch-tree CRUD unless they affect model-turn behavior.
3. For transport-sensitive behavior, decide whether realtime is:
   - covered by shared `Agent.cs` tests,
   - covered by executor-level realtime tests,
   - missing a realtime equivalent,
   - not applicable because realtime provider sessions have different mechanics.

Realtime differs from chat at the transport boundary:

- Chat gets projected message history on every model call.
- Realtime keeps provider-side session state and should receive only unseen user conversation items plus tool results.
- Branch/session history remains HPD's durable source of truth for both transports.

## Priority Key

- `P0`: bugs like live regressions already seen: hangs, missing turn sync, duplicated final text, tool loop not continuing, branch history wrong.
- `P1`: important parity or middleware coverage, lower live-break probability.
- `P2`: nice-to-have or mostly covered through shared logic.

## Initial Parity Matrix

| Priority | Behavior contract | Existing chat/source tests | Realtime risk | Current realtime coverage | Missing realtime recommendation |
|---|---|---|---|---|---|
| P0 | Simple streamed text completes exactly once with `TextMessageStart`, deltas, `TextMessageEnd`, `AgentTurnFinished`, `MessageTurnFinished`. | `Phase0_Characterization/SimpleTextResponseTest.cs::CurrentBehavior_SimpleTextResponse_EmitsCorrectEventSequence` | Provider stream can stay open after terminal text; done event may duplicate content. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_SimpleText_CompletesTurnAndCommitsBranchText`, `RealtimeModelTurnExecutorTests.RunAsync_FinalText_ReturnsControlWithoutWaitingForProviderCompletion`, `RunAsync_TextDone_DoesNotDuplicatePriorTextDelta` | Covered for current realtime mapper. |
| P0 | Single tool call executes through HPD tool core, sends result, continues to final answer. | `CharacterizationTests.CurrentBehavior_SingleToolCall_ExecutesAndReturnsResult` | Realtime can hang after final tool call or fail to continue after tool result. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_ExecutesToolsThroughAgentLoopAndContinuesRealtimeSession`, `RunAsync_RealtimeTransport_PersistsToolCallAndToolResultMessages`, executor final-tool hang test | Covered for normal single-tool flow. |
| P0 | Multiple sequential tool calls continue across model iterations. | `PerformanceBaselineTests.Baseline_MultipleToolCalls_CompletesQuickly`, live chat loop behavior | Realtime response loop must return control at each tool call and resume same session after each result. | Current realtime agent test covers Add then Multiply in one run; live CLI validated three turns. | Add deterministic agent-level realtime test for three sequential tool calls before final text; assert `AgentTurnStartedEvent` count and input result order. |
| P0 | Multi-turn session/branch context reaches the model on later turns. | `SyncMessageAPIIntegrationTests.Sync_Messages_Property_Reflects_Agent_Responses`, `Mixing_Sync_And_Async_APIs_Works_Correctly` | Realtime should not replay full branch, but must send new user turns into existing session. This caused a live bug. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_MultiTurn_SendsFollowUpUserTextToExistingSession`, `RealtimeModelTurnExecutorTests.RunAsync_ExistingSession_SendsOnlyNewUserMessages`, live CLI validated 20 -> 51 -> 43 | Covered for follow-up user text sync. |
| P0 | Branch persistence records tool-call events and reconstructs function call/result content. | `BranchEventStoreTests.Agent_PersistsToolCallEvents_WhenToolEventsAreStreamed` | Realtime normalized tool calls must survive event conversion/projector path; CLI text view hides them. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_PersistsToolCallAndToolResultMessages`, `RunAsync_RealtimeTransport_MultipleToolCallsInOneResponse_ExecutesAllAndPersistsResults` | Covered for single and same-response multi-tool persistence. |
| P0 | Final text done marker should not duplicate streamed deltas in branch history. | `StreamingCoalescingTests.ConstructChatResponseFromUpdates_CoalescesConsecutiveTextContent` | Realtime `OutputTextDone` may carry full final text after deltas. This caused live duplicate text. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_TextDone_DoesNotDuplicateBranchText`, `RealtimeModelTurnExecutorTests.RunAsync_TextDone_DoesNotDuplicatePriorTextDelta` | Covered. |
| P0 | Provider errors fail/cancel the model turn and route through agent error handling. | `BranchEventStoreTests.Agent_PersistsTurnFailed_WhenSessionTurnFaults`, error paths in Phase0 | Realtime `ErrorRealtimeServerMessage` / failed lifecycle must throw or produce failure events consistently. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_ProviderError_FailsTurnWithoutAssistantTextCommit`, `RealtimeModelTurnExecutorTests.RunAsync_MapsRealtimeErrorUpdate` | Covered for thrown provider error and no assistant branch commit. |
| P0 | Tool result submission should not create duplicate response requests or stale response state. | Chat implicitly sends next full request after tool result. | Realtime owns `_responseRequested`; wrong state caused hangs/loops. | `SubmitToolResultsAsync_SendsRealtimeFunctionResultAndCreatesNextResponse`, `SubmitToolResultsAsync_TwoConsecutiveCycles_DoesNotDuplicateResponseRequests` | Covered. |
| P1 | Parallel/multiple tool calls in one assistant response all execute and emit unique events. | `CharacterizationTests.CurrentBehavior_ParallelToolCalls_ExecutesAllTools` | Realtime output item mapping may yield one call per item; multiple calls in same response/item need coverage. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_MultipleToolCallsInOneResponse_ExecutesAllAndPersistsResults` | Covered for multiple function calls in one realtime output item. Optional executor-only coverage can be added if mapper shape changes. |
| P1 | Circuit breaker stops repeated identical tool calls. | `CharacterizationTests.CurrentBehavior_CircuitBreaker_TerminatesOnRepeatedCalls`, `AgentDecisionEngineTests` signature tests | Shared state should work, but realtime repeated tool calls are fed from provider session not full chat requests. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_CircuitBreakerStopsRepeatedIdenticalToolCalls` | Covered for repeated identical realtime tool calls. |
| P1 | Consecutive tool errors terminate after configured limit. | `CharacterizationTests.CurrentBehavior_ConsecutiveErrors_TerminatesAfterLimit`, `ErrorTrackingMiddlewareV2Tests` | Shared error middleware should work, but realtime continuation after failed tool result needs coverage. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_ConsecutiveToolErrorsTerminateAfterLimit` | Covered. |
| P1 | Max-iteration / continuation permission limits are honored. | `CharacterizationTests.CurrentBehavior_MaxIterations_TerminatesWhenLimitReached` | Realtime transport should still respect `Agent.cs` iteration limits and permission middleware. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_MaxIterationContinuationDeniedStopsBeforeNextRealtimeResponse` | Covered. |
| P1 | CoalesceDeltas run/config behavior applies to transport-neutral model updates. | `CoalesceDeltasTests.*` | Realtime text deltas are normalized then converted to chat updates; coalesce mode should still work. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_CoalesceDeltas_EmitsSingleTextDelta` plus default simple-text test emits multiple deltas. | Covered for run-level true/default false. Config-default override remains optional. |
| P1 | Model-turn middleware wraps realtime model turns, not only chat. | `PipelineV2Tests.WrapModelCallStreaming_RetryPattern_BuildsChain` | New hook is transport-neutral, but real agent selection should invoke it for realtime. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_ModelTurnMiddlewareSeesRealtimeRequest` | Covered for agent-level realtime model-turn hook. |
| P1 | Middleware context gets session/content/run config during model turn. | `ModelRequestSessionTests.*`, middleware context tests | Realtime `AgentModelTurnRequest` should expose same context as chat for TTS/progressive/etc. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_ModelTurnMiddlewareSeesRealtimeRequest` asserts transport, realtime client, messages, and tools. | Add deeper spy coverage for session/content store/run config if those properties become critical for realtime-specific middleware. |
| P1 | Reasoning is persisted but optionally excluded from later model history. | `BranchEventStoreTests.Agent_PersistsReasoningEvents_WhenReasoningExcludedFromModelHistory`, `StreamingCoalescingTests` reasoning tests | Realtime may later support reasoning updates; current mapper lacks reasoning message coverage. | Not applicable until realtime provider emits mapped reasoning. | Track as future test if/when realtime reasoning mapping is added. |
| P1 | Unknown tool behavior follows config. | `AgentDecisionEngineTests.DecideNextAction_UnknownTool_*` and tool execution core tests | Realtime tool calls can name unknown functions; should not wedge session. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_UnknownToolDefault_AllowsModelRecovery`, `RunAsync_RealtimeTransport_UnknownToolTerminate_DoesNotContinueModelLoop` | Covered for default recovery and terminate-for-handoff modes. |
| P1 | Function retry/timeout middleware wraps tool execution after realtime tool call. | `FunctionRetryMiddlewareTests`, `FunctionTimeoutMiddlewareTests`, `PipelineV2Tests.WrapFunctionCall_BuildsChain` | Tool execution is shared, but realtime path must still create normal `FunctionRequest`. | `RealtimeAgentModelTurnTests.RunAsync_RealtimeTransport_FunctionRetryMiddlewareRetriesToolCall`, `RunAsync_RealtimeTransport_FunctionTimeoutMiddlewareSubmitsTimedOutToolResult` | Covered. |
| P2 | Background response continuation behavior. | `BackgroundResponsesIntegrationTests`, `BackgroundResponsesTests` | Mostly chat/Responses-API specific; realtime semantics differ. | Not covered. | Mark not applicable unless realtime provider supports compatible continuation tokens. |
| P2 | Branch compaction boundaries preserve tool-call groups. | `BranchHistoryCompactionTests.Planner_ToolCallGroupBoundary_ExpandsToMatchingCallResult` | Shared persisted branch format; realtime only needs to produce same content/events. | Covered if P0 branch persistence test is added. | No separate realtime compaction test unless branch content differs. |
| P2 | Channel routing/interruption flags for text events. | `ChannelRoutingTests` | Shared event coordinator behavior; realtime emits same `TextDeltaEvent`. | Indirect. | Optional realtime smoke test for event channels if audio interruption depends on it. |

## First Implementation Slice

Completed first implementation slices:

1. Agent-level realtime simple text finalization and branch text test.
2. Agent-level realtime branch persistence for tool calls/results.
3. Agent-level realtime multi-turn follow-up user sync.
4. Agent-level realtime delta + done duplicate guard.
5. Agent-level realtime provider-error failure persistence.
6. Agent-level same-response multiple tool calls.
7. Agent-level coalesced realtime deltas.
8. Agent-level realtime model-turn middleware spy.
9. Agent-level realtime circuit breaker.
10. Agent-level realtime function retry.
11. Executor repeated tool-result/response state sequence.
12. Realtime consecutive tool errors and max-iteration termination.
13. Unknown-tool terminate/pass-through behavior.
14. Function timeout middleware after realtime tool call.

Still useful next additions:

1. Realtime provider-specific integration smoke tests against actual OpenAI realtime events.
2. Future reasoning/audio-output mapper tests when those realtime update types are normalized into `AgentModelUpdate`.
3. Optional config-default `CoalesceDeltas` realtime test if config/run override precedence changes.

These directly cover the live regressions already found and keep the suite small.

## Subagent Continuation Notes

Next audit pass should inspect:

- `FunctionRetryMiddlewareTests`
- `FunctionTimeoutMiddlewareTests`
- `CircuitBreakerMiddlewareTests`
- `PermissionTests`
- `ToolScopingMiddlewareTests`
- `StructuredOutputToolModeTests`
- `ClientToolMiddlewareTests`

For each, decide whether realtime needs a dedicated agent-level test or whether shared `Agent.cs` coverage plus one realtime smoke test is enough.
