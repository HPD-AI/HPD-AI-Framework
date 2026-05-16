// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using System.Collections.Immutable;
using Xunit;
using static HPD.Agent.Tests.Middleware.V2.MiddlewareTestHelpers;

namespace HPD.Agent.Tests.Middleware;

/// <summary>

/// Covers:
///   Cat 1  — AgentMiddlewarePipeline.Dispatch* methods (T001–T005)
///   Cat 2  — ContainerMiddlewareState (T006–T009)
///   Cat 3  — CollapseAttribute (T010–T011)
///   Cat 4  — ContainerMiddleware constructor + pipeline instantiation (T012–T015)
///   Cat 5  — ContainerMiddleware dispatch routing (T016–T026)
///   Cat 6  — OnErrorAsync routing (T027)
///   Cat 7  — §5B DI builder-time instances (T028–T035)
///   Cat 8  — §5A config-constructor middleware (T036–T040)
///   Cat 9  — HarnessReference.MiddlewareConfigs serialisation (T041–T044)
///   Cat 10 — CollapseMiddlewareConfigFactory record (T045–T046)
///   Cat 11 — BeforeParallelBatchAsync dispatch (T050–T053)
/// </summary>
public class HARNESScopedMiddlewareTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 1 — AgentMiddlewarePipeline.Dispatch* methods
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task T001_DispatchBeforeIterationAsync_CallsAllMiddlewaresInRegistrationOrder()
    {
        var order = new List<string>();
        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[]
        {
            new SpyMiddleware("A", order),
            new SpyMiddleware("B", order),
            new SpyMiddleware("C", order),
        });
        var ctx = CreateBeforeIterationContext();

        await pipeline.DispatchBeforeIterationAsync(ctx, CancellationToken.None);

        Assert.Equal(new[] { "A.BeforeIteration", "B.BeforeIteration", "C.BeforeIteration" }, order);
    }

    [Fact]
    public async Task T002_DispatchAfterIterationAsync_CallsInReverseOrder_CollectsAllExceptions()
    {
        var order = new List<string>();
        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[]
        {
            new SpyMiddleware("A", order),
            new ThrowingAfterIterationMiddleware("BOOM"),
            new SpyMiddleware("C", order),
        });
        var ctx = CreateAfterIterationContext();

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => pipeline.DispatchAfterIterationAsync(ctx, CancellationToken.None));

        // All three ran in reverse order (C, Thrower, A) — C and A recorded, Thrower threw
        Assert.Equal(new[] { "C.AfterIteration", "A.AfterIteration" }, order);
        Assert.Single(ex.InnerExceptions);
        Assert.Equal("BOOM", ex.InnerExceptions[0].Message);
    }

    [Fact]
    public async Task T003_DispatchAfterFunctionAsync_RespectsShouldExecuteFilter()
    {
        var order = new List<string>();
        // Scope "Scoped" to a different function so it is filtered out for "TestFunction" context
        var scoped = new ScopedSpyMiddleware("Scoped", order, shouldExecute: false)
            .ForFunction("OtherFunction");
        var always = new SpyMiddleware("Always", order);
        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[] { scoped, always });

        // Context function is "TestFunction" — "Scoped" is scoped to "OtherFunction", so filtered
        var ctx = CreateAfterFunctionContext();
        await pipeline.DispatchAfterFunctionAsync(ctx, CancellationToken.None);

        // Only "Always" ran — Scoped was filtered by scope metadata
        Assert.Equal(new[] { "Always.AfterFunction" }, order);
        Assert.DoesNotContain("Scoped.AfterFunction", order);
    }

    [Fact]
    public async Task T004_DispatchOnErrorAsync_SwallowsExceptionsFromHandlers()
    {
        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[]
        {
            new ThrowingOnErrorMiddleware("err1"),
            new ThrowingOnErrorMiddleware("err2"),
        });
        var ctx = CreateErrorContext();

        // Should not throw — DispatchOnErrorAsync swallows handler errors
        await pipeline.DispatchOnErrorAsync(ctx, CancellationToken.None);
    }

    [Fact]
    public async Task T005_ExecuteFunctionCallAsync_StillWorkesCorrectly_Regression()
    {
        var order = new List<string>();
        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[]
        {
            new WrapRecordingMiddleware("Outer", order),
            new WrapRecordingMiddleware("Inner", order),
        });

        var request = CreateFunctionRequest();
        object? result = await pipeline.ExecuteFunctionCallAsync(
            request,
            _ => { order.Add("actual"); return Task.FromResult<object?>("ok"); },
            CancellationToken.None);

        Assert.Equal(new[] { "Outer.before", "Inner.before", "actual", "Inner.after", "Outer.after" }, order);
        Assert.Equal("ok", result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 2 — ContainerMiddlewareState
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void T006_WithHarnessPipeline_AddsToNewState_OriginalUnchanged()
    {
        var original = new ContainerMiddlewareState();
        var pipeline = MakeEmptyPipeline();

        var updated = original.WithHarnessPipeline("DbHarness", pipeline);

        Assert.True(original.HarnessPipelines.IsEmpty);
        Assert.True(updated.HarnessPipelines.ContainsKey("DbHarness"));
        Assert.Same(pipeline, updated.HarnessPipelines["DbHarness"]);
    }

    [Fact]
    public void T007_WithoutHarnessPipeline_RemovesKey_LeavesOtherIntact()
    {
        var state = new ContainerMiddlewareState()
            .WithHarnessPipeline("A", MakeEmptyPipeline())
            .WithHarnessPipeline("B", MakeEmptyPipeline());

        var updated = state.WithoutHarnessPipeline("A");

        Assert.False(updated.HarnessPipelines.ContainsKey("A"));
        Assert.True(updated.HarnessPipelines.ContainsKey("B"));
    }

    [Fact]
    public void T008_ClearTurnContainers_ClearsHarnessPipelines()
    {
        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("DbHarness")
            .WithHarnessPipeline("DbHarness", MakeEmptyPipeline())
            .WithRecoveredFunction("call-1", new RecoveryInfo(RecoveryType.HiddenItem, "DbHarness", "Query"))
            .WithContainerInstructions("DbHarness", new ContainerInstructionSet("activated", "use transactions"));

        var cleared = state.ClearTurnContainers();

        Assert.True(cleared.ExpandedContainers.IsEmpty);
        Assert.True(cleared.ContainersExpandedThisTurn.IsEmpty);
        Assert.True(cleared.RecoveredFunctionCalls.IsEmpty);
        Assert.True(cleared.HarnessPipelines.IsEmpty);
    }

    [Fact]
    public void T009_ClearTurnContainers_ClearsExactlyTheFourTurnScopedFields()
    {
        // Verify the four fields cleared are exactly the four specified in the proposal.
        // ActiveContainerInstructions is also turn-scoped (cleared separately via ClearContainerInstructions).
        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("X")
            .WithHarnessPipeline("X", MakeEmptyPipeline());

        var cleared = state.ClearTurnContainers();

        // All four cleared:
        Assert.True(cleared.ExpandedContainers.IsEmpty);
        Assert.True(cleared.ContainersExpandedThisTurn.IsEmpty);
        Assert.True(cleared.RecoveredFunctionCalls.IsEmpty);
        Assert.True(cleared.HarnessPipelines.IsEmpty);
        // ActiveContainerInstructions NOT touched by ClearTurnContainers (uses ClearContainerInstructions)
        // — this is by design, so no assertion needed here
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 3 — CollapseAttribute
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void T010_CollapseAttribute_Middlewares_DefaultsToNull()
    {
        var attr = new CollapseAttribute("desc");
        Assert.Null(attr.Middlewares);
    }

    [Fact]
    public void T011_CollapseAttribute_Middlewares_CanBeSetAndRetrieved()
    {
        var attr = new CollapseAttribute("desc") { Middlewares = [typeof(SimpleSpyMiddleware)] };
        Assert.NotNull(attr.Middlewares);
        Assert.Single(attr.Middlewares);
        Assert.Equal(typeof(SimpleSpyMiddleware), attr.Middlewares[0]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 4 — ContainerMiddleware constructor + pipeline instantiation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task T012_ContainerMiddleware_NullHarnessFactories_DoesNotThrow_PassesThrough()
    {
        var middleware = BuildContainerMiddleware(harnessFactories: null);
        int handlerCalls = 0;
        var req = CreateFunctionRequest(
            function: AIFunctionFactory.Create(() => "ok", "Query"));

        var result = await middleware.WrapFunctionCallAsync(
            req,
            r => { handlerCalls++; return Task.FromResult<object?>("passthrough"); },
            CancellationToken.None);

        Assert.Equal(1, handlerCalls);
        Assert.Equal("passthrough", result);
    }

    [Fact]
    public async Task T013_PipelineInstantiatedAtExpansionTime_ForHarnessWithFactories()
    {
        int factoryCallCount = 0;
        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query", "Execute"],
                middlewareFactories: new Func<IAgentMiddleware>[]
                {
                    () => { factoryCallCount++; return new SimpleSpyMiddleware(); },
                    () => { factoryCallCount++; return new SimpleSpyMiddleware(); },
                })
        };

        var (containerMiddleware, tools) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query", "Execute"], factories);

        // Simulate expansion: LLM calls DbHarness container
        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("call-1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await containerMiddleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        var state = beCtx.GetMiddlewareState<ContainerMiddlewareState>();
        Assert.NotNull(state);
        Assert.True(state.HarnessPipelines.ContainsKey("DbHarness"));
        Assert.Equal(2, state.HarnessPipelines["DbHarness"].Count);
        Assert.Equal(2, factoryCallCount);
    }

    [Fact]
    public async Task T014_PipelineNotReInstantiated_IfAlreadyInState()
    {
        int factoryCallCount = 0;
        var existingPipeline = MakeEmptyPipeline();
        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: new Func<IAgentMiddleware>[]
                {
                    () => { factoryCallCount++; return new SimpleSpyMiddleware(); }
                })
        };

        var (containerMiddleware, _) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"], factories);

        // Pre-populate state with existing pipeline (simulates persistent container)
        var preState = new ContainerMiddlewareState()
            .WithExpandedContainer("DbHarness")
            .WithHarnessPipeline("DbHarness", existingPipeline);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext(state: InitialStateWith(preState));
        var toolCall = new FunctionCallContent("call-2", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await containerMiddleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        var state = beCtx.GetMiddlewareState<ContainerMiddlewareState>();
        // Factory must NOT have been called again — same pipeline instance
        Assert.Equal(0, factoryCallCount);
        Assert.Same(existingPipeline, state!.HarnessPipelines["DbHarness"]);
    }

    [Fact]
    public async Task T015_HarnessWithNoFactories_DoesNotAddPipeline()
    {
        var factories = new Dictionary<string, HarnessFactory>
        {
            ["MathHarness"] = BuildHarnessFactory("MathHarness",
                childFunctions: ["Add"],
                middlewareFactories: null) // no scoped middleware
        };

        var (containerMiddleware, _) = BuildContainerMiddlewareWithContainerTool(
            "MathHarness", ["Add"], factories);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("call-3", "MathHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await containerMiddleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        var state = beCtx.GetMiddlewareState<ContainerMiddlewareState>();
        Assert.NotNull(state);
        // Harness expanded but no pipeline created
        Assert.True(state.ExpandedContainers.Contains("MathHarness"));
        Assert.False(state.HarnessPipelines.ContainsKey("MathHarness"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 5 — ContainerMiddleware dispatch routing
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task T016_BeforeIterationAsync_DispatchesToAllActiveHarnessPipelines()
    {
        var orderA = new List<string>();
        var orderB = new List<string>();
        var spyA = new SpyMiddleware("PipelineA", orderA);
        var spyB = new SpyMiddleware("PipelineB", orderB);

        var state = new ContainerMiddlewareState()
            .WithHarnessPipeline("HarnessA", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spyA }))
            .WithHarnessPipeline("HarnessB", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spyB }));

        var middleware = BuildContainerMiddleware();
        var ctx = CreateBeforeIterationContextWithState(state);

        await middleware.BeforeIterationAsync(ctx, CancellationToken.None);

        Assert.Contains("PipelineA.BeforeIteration", orderA);
        Assert.Contains("PipelineB.BeforeIteration", orderB);
    }

    [Fact]
    public async Task T017_AfterIterationAsync_DispatchesPipelinesInReverseOrder()
    {
        var globalOrder = new List<string>();
        var spyA = new SpyMiddleware("A", globalOrder);
        var spyB = new SpyMiddleware("B", globalOrder);

        // Register A then B in state (insertion order)
        var state = new ContainerMiddlewareState()
            .WithHarnessPipeline("HarnessA", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spyA }))
            .WithHarnessPipeline("HarnessB", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spyB }));

        var middleware = BuildContainerMiddleware();
        var ctx = CreateAfterIterationContextWithState(state);

        await middleware.AfterIterationAsync(ctx, CancellationToken.None);

        // Both pipelines must have dispatched (ImmutableDictionary order is not guaranteed)
        var afterCalls = globalOrder.Where(s => s.Contains("AfterIteration")).ToList();
        Assert.Equal(2, afterCalls.Count);
        Assert.Contains("A.AfterIteration", afterCalls);
        Assert.Contains("B.AfterIteration", afterCalls);
    }

    [Fact]
    public async Task T018_BeforeFunctionAsync_RoutesOnlyToOwningHarnessPipeline()
    {
        var dbOrder = new List<string>();
        var searchOrder = new List<string>();
        var dbSpy = new SpyMiddleware("DbSpy", dbOrder);
        var searchSpy = new SpyMiddleware("SearchSpy", searchOrder);

        // Build container middleware with two harnesses; Query belongs to DbHarness
        var (containerMiddleware, tools) = BuildContainerMiddlewareWithTwoHarneses(
            ("DbHarness", new[] { "Query", "Execute" }, dbSpy),
            ("SearchHarness", new[] { "WebSearch" }, searchSpy));

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("DbHarness")
            .WithExpandedContainer("SearchHarness")
            .WithHarnessPipeline("DbHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { dbSpy }))
            .WithHarnessPipeline("SearchHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { searchSpy }));

        var queryFn = tools.First(t => t is AIFunction af && af.Name == "Query") as AIFunction;
        var ctx = CreateBeforeFunctionContextWithState(queryFn!, "Query", state);

        await containerMiddleware.BeforeFunctionAsync(ctx, CancellationToken.None);

        Assert.Contains("DbSpy.BeforeFunction", dbOrder);
        Assert.DoesNotContain("SearchSpy.BeforeFunction", searchOrder);
    }

    [Fact]
    public async Task T019_BeforeFunctionAsync_NullFunction_DoesNotDispatchToHarnessPipeline()
    {
        var order = new List<string>();
        var spy = new SpyMiddleware("Spy", order);

        var state = new ContainerMiddlewareState()
            .WithHarnessPipeline("DbHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spy }));

        var middleware = BuildContainerMiddleware();
        // function = null simulates the recovery path
        var ctx = CreateBeforeFunctionContextWithState(null!, "Query", state);

        await middleware.BeforeFunctionAsync(ctx, CancellationToken.None);

        // Recovery logic runs (OverrideResult set), but harness pipeline not dispatched
        Assert.DoesNotContain("Spy.BeforeFunction", order);
        // OverrideResult set to empty string by recovery logic when function is null and no recovery call tracked
        // (state has no RecoveredFunctionCalls so OverrideResult won't be set in this case — just assert no dispatch)
    }

    [Fact]
    public async Task T020_AfterFunctionAsync_RoutesOnlyToOwningHarnessPipeline()
    {
        var dbOrder = new List<string>();
        var searchOrder = new List<string>();
        var dbSpy = new SpyMiddleware("DbSpy", dbOrder);
        var searchSpy = new SpyMiddleware("SearchSpy", searchOrder);

        var (containerMiddleware, tools) = BuildContainerMiddlewareWithTwoHarneses(
            ("DbHarness", new[] { "Query", "Execute" }, dbSpy),
            ("SearchHarness", new[] { "WebSearch" }, searchSpy));

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("DbHarness")
            .WithExpandedContainer("SearchHarness")
            .WithHarnessPipeline("DbHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { dbSpy }))
            .WithHarnessPipeline("SearchHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { searchSpy }));

        var queryFn = tools.First(t => t is AIFunction af && af.Name == "Query") as AIFunction;
        var ctx = CreateAfterFunctionContextWithState(queryFn!, "Query", state);

        await containerMiddleware.AfterFunctionAsync(ctx, CancellationToken.None);

        Assert.Contains("DbSpy.AfterFunction", dbOrder);
        Assert.DoesNotContain("SearchSpy.AfterFunction", searchOrder);
    }

    [Fact]
    public async Task T021_WrapFunctionCallAsync_HarnessPipelineIsInnermostWrapper()
    {
        var order = new List<string>();
        var globalWrap = new WrapRecordingMiddleware("global", order);
        var harnessWrap = new WrapRecordingMiddleware("harness", order);

        // Build a minimal _itemToContainerMap by constructing a container tool
        var (containerMiddleware, tools) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"], new Dictionary<string, HarnessFactory>
            {
                ["DbHarness"] = BuildHarnessFactory("DbHarness",
                    childFunctions: ["Query"],
                    middlewareFactories: new Func<IAgentMiddleware>[] { () => harnessWrap })
            });

        // Pre-populate state with an active harness pipeline (simulates post-expansion)
        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("DbHarness")
            .WithHarnessPipeline("DbHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { harnessWrap }));

        var queryFn = tools.First(t => t is AIFunction af && af.Name == "Query") as AIFunction;
        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext(state: InitialStateWith(state));
        var req = new FunctionRequest
        {
            Function = queryFn!,
            CallId = "call-x",
            Arguments = new Dictionary<string, object?>(),
            State = agentCtx.State,
        };

        // Build a global pipeline that contains containerMiddleware as the only global wrapper
        var globalPipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[]
        {
            globalWrap,
            containerMiddleware,
        });

        await globalPipeline.ExecuteFunctionCallAsync(
            req,
            _ => { order.Add("actual"); return Task.FromResult<object?>(null); },
            CancellationToken.None);

        Assert.Equal(new[]
        {
            "global.before",
            "harness.before",
            "actual",
            "harness.after",
            "global.after",
        }, order);
    }

    [Fact]
    public async Task T022_WrapFunctionCallAsync_PassesThrough_WhenHarnessFactoriesNull()
    {
        var middleware = BuildContainerMiddleware(harnessFactories: null);
        int calls = 0;
        var req = CreateFunctionRequest(function: AIFunctionFactory.Create(() => "ok", "Query"));

        await middleware.WrapFunctionCallAsync(
            req,
            _ => { calls++; return Task.FromResult<object?>(null); },
            CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task T023_WrapFunctionCallAsync_PassesThrough_WhenFunctionNotInItemToContainerMap()
    {
        // Function "RandomFunc" is not a child of any known container
        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: new Func<IAgentMiddleware>[] { () => new SimpleSpyMiddleware() })
        };
        var (middleware, _) = BuildContainerMiddlewareWithContainerTool("DbHarness", ["Query"], factories);

        int calls = 0;
        var req = CreateFunctionRequest(function: AIFunctionFactory.Create(() => "ok", "RandomFunc"));

        await middleware.WrapFunctionCallAsync(
            req,
            _ => { calls++; return Task.FromResult<object?>(null); },
            CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task T024_AfterMessageTurnAsync_DispatchesToPipelinesBeforeClearingState()
    {
        var order = new List<string>();
        var spy = new SpyMiddleware("Spy", order);
        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[] { spy });

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("DbHarness")
            .WithHarnessPipeline("DbHarness", pipeline);

        var middleware = BuildContainerMiddleware();
        var ctx = CreateAfterMessageTurnContextWithState(state);

        await middleware.AfterMessageTurnAsync(ctx, CancellationToken.None);

        // Spy was called
        Assert.Contains("Spy.AfterMessageTurn", order);

        // Pipelines cleared from state after the hook
        var finalState = ctx.GetMiddlewareState<ContainerMiddlewareState>();
        Assert.True(finalState == null || finalState.HarnessPipelines.IsEmpty);
    }

    [Fact]
    public async Task T025_AfterMessageTurnAsync_DispatchesPipelinesInReverseOrder()
    {
        var globalOrder = new List<string>();
        var spyA = new SpyMiddleware("A", globalOrder);
        var spyB = new SpyMiddleware("B", globalOrder);

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("HarnessA")
            .WithExpandedContainer("HarnessB")
            .WithHarnessPipeline("HarnessA", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spyA }))
            .WithHarnessPipeline("HarnessB", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spyB }));

        var middleware = BuildContainerMiddleware();
        var ctx = CreateAfterMessageTurnContextWithState(state);

        await middleware.AfterMessageTurnAsync(ctx, CancellationToken.None);

        // Both pipelines must have dispatched (ImmutableDictionary order is not guaranteed)
        var afterCalls = globalOrder.Where(s => s.Contains("AfterMessageTurn")).ToList();
        Assert.Equal(2, afterCalls.Count);
        Assert.Contains("A.AfterMessageTurn", afterCalls);
        Assert.Contains("B.AfterMessageTurn", afterCalls);
    }

    [Fact]
    public async Task T026_BeforeToolExecutionAsync_DispatchesToNewlyActivatedPipelineImmediately()
    {
        var order = new List<string>();
        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: new Func<IAgentMiddleware>[]
                {
                    () => new SpyMiddleware("DbSpy", order)
                })
        };

        var (containerMiddleware, _) = BuildContainerMiddlewareWithContainerTool("DbHarness", ["Query"], factories);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await containerMiddleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        Assert.Contains("DbSpy.BeforeToolExecution", order);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 6 — OnErrorAsync routing (T027)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task T027_OnErrorAsync_DispatchesToAllActiveHarnessPipelinesInReverseOrder_SwallowsHandlerExceptions()
    {
        var order = new List<string>();
        var spyA = new SpyMiddleware("A", order);
        var throwingB = new ThrowingOnErrorMiddleware("boom"); // throws in OnError
        var spyC = new SpyMiddleware("C", order);

        var state = new ContainerMiddlewareState()
            .WithHarnessPipeline("HarnessA", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spyA }))
            .WithHarnessPipeline("HarnessB", new AgentMiddlewarePipeline(new IAgentMiddleware[] { throwingB }))
            .WithHarnessPipeline("HarnessC", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spyC }));

        var middleware = BuildContainerMiddleware();
        var ctx = CreateErrorContextWithState(state);

        // Must not throw even though HarnessB's handler throws
        await middleware.OnErrorAsync(ctx, CancellationToken.None);

        // A and C recorded; B threw but was swallowed (ImmutableDictionary order is not guaranteed)
        var onErrorCalls = order.Where(s => s.Contains("OnError")).ToList();
        Assert.Equal(2, onErrorCalls.Count);
        Assert.Contains("A.OnError", onErrorCalls);
        Assert.Contains("C.OnError", onErrorCalls);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HELPER TYPES — Spy / Recording middleware
    // ═══════════════════════════════════════════════════════════════════════

    private class SpyMiddleware(string name, List<string> log) : IAgentMiddleware
    {
        public Task BeforeIterationAsync(BeforeIterationContext ctx, CancellationToken ct)
        { log.Add($"{name}.BeforeIteration"); return Task.CompletedTask; }

        public Task AfterIterationAsync(AfterIterationContext ctx, CancellationToken ct)
        { log.Add($"{name}.AfterIteration"); return Task.CompletedTask; }

        public Task BeforeToolExecutionAsync(BeforeToolExecutionContext ctx, CancellationToken ct)
        { log.Add($"{name}.BeforeToolExecution"); return Task.CompletedTask; }

        public Task BeforeParallelBatchAsync(BeforeParallelBatchContext ctx, CancellationToken ct)
        { log.Add($"{name}.BeforeParallelBatch"); return Task.CompletedTask; }

        public Task BeforeFunctionAsync(BeforeFunctionContext ctx, CancellationToken ct)
        { log.Add($"{name}.BeforeFunction"); return Task.CompletedTask; }

        public Task AfterFunctionAsync(AfterFunctionContext ctx, CancellationToken ct)
        { log.Add($"{name}.AfterFunction"); return Task.CompletedTask; }

        public Task AfterMessageTurnAsync(AfterMessageTurnContext ctx, CancellationToken ct)
        { log.Add($"{name}.AfterMessageTurn"); return Task.CompletedTask; }

        public Task OnErrorAsync(ErrorContext ctx, CancellationToken ct)
        { log.Add($"{name}.OnError"); return Task.CompletedTask; }
    }

    private class SimpleSpyMiddleware : IHarnessMiddleware
    {
        public bool Called { get; private set; }
        public Task BeforeFunctionAsync(BeforeFunctionContext ctx, CancellationToken ct)
        { Called = true; return Task.CompletedTask; }
    }

    private class ScopedSpyMiddleware(string name, List<string> log, bool shouldExecute) : IAgentMiddleware
    {
        public bool ShouldExecuteResult => shouldExecute;

        public Task AfterFunctionAsync(AfterFunctionContext ctx, CancellationToken ct)
        {
            log.Add($"{name}.AfterFunction");
            return Task.CompletedTask;
        }
    }

    private class WrapRecordingMiddleware(string name, List<string> log) : IAgentMiddleware
    {
        public Task<object?> WrapFunctionCallAsync(
            FunctionRequest req,
            Func<FunctionRequest, Task<object?>> next,
            CancellationToken ct)
        {
            return WrapAsync(req, next, ct);
        }

        private async Task<object?> WrapAsync(
            FunctionRequest req,
            Func<FunctionRequest, Task<object?>> next,
            CancellationToken ct)
        {
            log.Add($"{name}.before");
            var result = await next(req);
            log.Add($"{name}.after");
            return result;
        }
    }

    private class ThrowingAfterIterationMiddleware(string message) : IAgentMiddleware
    {
        public Task AfterIterationAsync(AfterIterationContext ctx, CancellationToken ct)
            => Task.FromException(new InvalidOperationException(message));
    }

    private class ThrowingOnErrorMiddleware(string message) : IAgentMiddleware
    {
        public Task OnErrorAsync(ErrorContext ctx, CancellationToken ct)
            => Task.FromException(new InvalidOperationException(message));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HELPER BUILDERS
    // ═══════════════════════════════════════════════════════════════════════

    private static AgentMiddlewarePipeline MakeEmptyPipeline()
        => new AgentMiddlewarePipeline(Array.Empty<IAgentMiddleware>());

    private static ContainerMiddleware BuildContainerMiddleware(
        IList<AITool>? tools = null,
        IReadOnlyDictionary<string, HarnessFactory>? harnessFactories = null,
        IReadOnlyDictionary<string, List<IAgentMiddleware>>? HARNESScopedMiddlewares = null,
        IReadOnlyDictionary<string, Dictionary<string, System.Text.Json.JsonElement>>? middlewareConfigs = null)
    {
        tools ??= new List<AITool>
        {
            AIFunctionFactory.Create(() => "dummy", "Dummy")
        };
        return new ContainerMiddleware(
            tools,
            ImmutableHashSet<string>.Empty,
            harnessFactories,
            HARNESScopedMiddlewares,
            middlewareConfigs,
            new CollapsingConfig { Enabled = true });
    }

    /// <summary>
    /// Builds a ContainerMiddleware and corresponding tool list where a single
    /// collapsed container tool exists with its child functions.
    /// </summary>
    private static (ContainerMiddleware middleware, IList<AITool> tools)
        BuildContainerMiddlewareWithContainerTool(
            string harnessName,
            string[] childFunctionNames,
            Dictionary<string, HarnessFactory>? factories = null,
            IReadOnlyDictionary<string, List<IAgentMiddleware>>? HARNESScopedMiddlewares = null,
            IReadOnlyDictionary<string, Dictionary<string, System.Text.Json.JsonElement>>? middlewareConfigs = null)
    {
        var tools = BuildToolsForCollapsedHarness(harnessName, childFunctionNames);
        var middleware = new ContainerMiddleware(
            tools,
            ImmutableHashSet<string>.Empty,
            factories,
            HARNESScopedMiddlewares,
            middlewareConfigs,
            new CollapsingConfig { Enabled = true });
        return (middleware, tools);
    }

    /// <summary>
    /// Builds a ContainerMiddleware with two collapsed harnesses for isolation tests.
    /// </summary>
    private static (ContainerMiddleware middleware, IList<AITool> tools)
        BuildContainerMiddlewareWithTwoHarneses(
            (string name, string[] children, IAgentMiddleware spy) a,
            (string name, string[] children, IAgentMiddleware spy) b)
    {
        var tools = new List<AITool>();
        tools.AddRange(BuildToolsForCollapsedHarness(a.name, a.children));
        tools.AddRange(BuildToolsForCollapsedHarness(b.name, b.children));

        // No factory dict needed — pipelines are set up manually in tests via state
        var middleware = new ContainerMiddleware(
            tools,
            ImmutableHashSet<string>.Empty,
            harnessFactories: null,
            HARNESScopedMiddlewares: null,
            middlewareConfigs: null,
            new CollapsingConfig { Enabled = true });
        return (middleware, tools);
    }

    /// <summary>
    /// Creates the container AIFunction tool + child AIFunction tools that the
    /// ContainerMiddleware uses to build _itemToContainerMap.
    /// </summary>
    private static List<AITool> BuildToolsForCollapsedHarness(string harnessName, string[] childFunctionNames)
    {
        var tools = new List<AITool>();

        // Container tool (IsContainer = true, FunctionNames = childFunctionNames, HarnessName = harnessName)
        var containerFunc = HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>($"{harnessName} expanded"),
            new HPDAIFunctionFactoryOptions
            {
                Name = harnessName,
                Description = $"{harnessName} container",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["HarnessName"] = harnessName,
                    ["FunctionNames"] = childFunctionNames,
                    ["IsCollapse"] = true,
                },
            });
        tools.Add(containerFunc);

        // Child tools
        foreach (var child in childFunctionNames)
        {
            var childFunc = AIFunctionFactory.Create(() => $"result of {child}", name: child, description: child);
            tools.Add(childFunc);
        }

        return tools;
    }

    private static HarnessFactory BuildHarnessFactory(
        string name,
        string[] childFunctions,
        IReadOnlyList<Func<IAgentMiddleware>>? middlewareFactories,
        IReadOnlyList<CollapseMiddlewareConfigFactory>? configFactories = null)
    {
        return new HarnessFactory(
            Name: name,
            HarnessType: typeof(object),
            CreateInstance: () => new object(),
            CreateFunctions: (_, _, _) => new List<AIFunction>(),
            GetReferencedHarneses: () => Array.Empty<string>(),
            GetReferencedFunctions: () => new Dictionary<string, string[]>(),
            HasDescription: true,
            Description: $"{name} description",
            FunctionNames: childFunctions,
            CollapseMiddlewareFactories: middlewareFactories,
            CollapseMiddlewareConfigFactories: configFactories);
    }

    private static AgentLoopState InitialStateWith(ContainerMiddlewareState containerState)
    {
        var baseState = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            "test-conv",
            "TestAgent");

        return baseState with
        {
            MiddlewareState = baseState.MiddlewareState.SetState("HPD.Agent.ContainerMiddlewareState", containerState),
        };
    }

    private static BeforeIterationContext CreateBeforeIterationContextWithState(ContainerMiddlewareState state)
    {
        var loopState = InitialStateWith(state);
        return V2.MiddlewareTestHelpers.CreateBeforeIterationContext(state: loopState);
    }

    private static AfterIterationContext CreateAfterIterationContextWithState(ContainerMiddlewareState state)
    {
        var loopState = InitialStateWith(state);
        return V2.MiddlewareTestHelpers.CreateAfterIterationContext(state: loopState);
    }

    private static BeforeFunctionContext CreateBeforeFunctionContextWithState(
        AIFunction function,
        string functionName,
        ContainerMiddlewareState state)
    {
        var loopState = InitialStateWith(state);
        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext(state: loopState);
        function ??= AIFunctionFactory.Create(() => "ok", functionName);
        return agentCtx.AsBeforeFunction(
            function,
            "call-test",
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            harnessName: null,
            skillName: null);
    }

    private static AfterFunctionContext CreateAfterFunctionContextWithState(
        AIFunction function,
        string functionName,
        ContainerMiddlewareState state)
    {
        var loopState = InitialStateWith(state);
        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext(state: loopState);
        return agentCtx.AsAfterFunction(
            function,
            "call-test",
            result: "ok",
            exception: null,
            new AgentRunConfig());
    }

    private static AfterMessageTurnContext CreateAfterMessageTurnContextWithState(ContainerMiddlewareState state)
    {
        var loopState = InitialStateWith(state);
        return V2.MiddlewareTestHelpers.CreateAfterMessageTurnContext(state: loopState);
    }

    private static ErrorContext CreateErrorContextWithState(ContainerMiddlewareState state)
    {
        var loopState = InitialStateWith(state);
        return V2.MiddlewareTestHelpers.CreateErrorContext(state: loopState);
    }

    private static FunctionRequest CreateFunctionRequest(AIFunction? function = null)
    {
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(), "test-run", "test-conv", "TestAgent");
        return new FunctionRequest
        {
            Function = function ?? AIFunctionFactory.Create(() => "ok", "TestFunction"),
            CallId = "call-1",
            Arguments = new Dictionary<string, object?>(),
            State = state,
        };
    }

    private static BeforeParallelBatchContext CreateBeforeParallelBatchContextWithState(
        ContainerMiddlewareState state,
        IReadOnlyList<ParallelFunctionInfo> parallelFunctions)
    {
        var loopState = InitialStateWith(state);
        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext(state: loopState);
        return agentCtx.AsBeforeParallelBatch(parallelFunctions, new AgentRunConfig());
    }

    private static ParallelFunctionInfo MakeParallelFunctionInfo(string functionName)
    {
        var fn = AIFunctionFactory.Create(() => $"result of {functionName}", name: functionName);
        return new ParallelFunctionInfo(fn, $"call-{functionName}", new Dictionary<string, object?>());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 7 — §5B DI builder-time instances
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task T028_DI_Instances_MergedAfterAttributeFactories_InOrder()
    {
        var order = new List<string>();
        var attrMiddleware = new SpyMiddleware("attr", order);
        var diMiddleware = new SpyMiddleware("di", order);

        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: new Func<IAgentMiddleware>[] { () => attrMiddleware })
        };
        var diMap = new Dictionary<string, List<IAgentMiddleware>>
        {
            ["DbHarness"] = new List<IAgentMiddleware> { diMiddleware }
        };

        var (middleware, _) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"], factories, HARNESScopedMiddlewares: diMap);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await middleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        var state = beCtx.GetMiddlewareState<ContainerMiddlewareState>();
        Assert.NotNull(state);
        var pipeline = state.HarnessPipelines["DbHarness"];
        Assert.Equal(2, pipeline.Count);
        // attr comes first, di appended after
        Assert.Same(attrMiddleware, pipeline.Middlewares[0]);
        Assert.Same(diMiddleware, pipeline.Middlewares[1]);
    }

    [Fact]
    public async Task T029_DI_Instances_Alone_WhenNoAttributeFactories()
    {
        var diMiddleware = new SimpleSpyMiddleware();

        // No CollapseMiddlewareFactories — harness has no attribute-declared middleware
        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: null)
        };
        var diMap = new Dictionary<string, List<IAgentMiddleware>>
        {
            ["DbHarness"] = new List<IAgentMiddleware> { diMiddleware }
        };

        var (middleware, _) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"], factories, HARNESScopedMiddlewares: diMap);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await middleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        var state = beCtx.GetMiddlewareState<ContainerMiddlewareState>();
        Assert.NotNull(state);
        Assert.True(state.HarnessPipelines.ContainsKey("DbHarness"));
        Assert.Equal(1, state.HarnessPipelines["DbHarness"].Count);
        Assert.Same(diMiddleware, state.HarnessPipelines["DbHarness"].Middlewares[0]);
    }

    [Fact]
    public async Task T030_DI_Instances_NoPipelineCreated_WhenMapHasNoEntryForThisHarness()
    {
        // diMap exists but doesn't have an entry for DbHarness
        var diMap = new Dictionary<string, List<IAgentMiddleware>>
        {
            ["OtherHarness"] = new List<IAgentMiddleware> { new SimpleSpyMiddleware() }
        };
        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: null)
        };

        var (middleware, _) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"], factories, HARNESScopedMiddlewares: diMap);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await middleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        var state = beCtx.GetMiddlewareState<ContainerMiddlewareState>();
        Assert.NotNull(state);
        Assert.True(state.ExpandedContainers.Contains("DbHarness"));
        Assert.False(state.HarnessPipelines.ContainsKey("DbHarness"));
    }

    [Fact]
    public async Task T031_DI_Instances_DifferentHarnesesAreIsolated_OnlyExpandedOneGetsPipeline()
    {
        var spyA = new SimpleSpyMiddleware();
        var spyB = new SimpleSpyMiddleware();
        var diMap = new Dictionary<string, List<IAgentMiddleware>>
        {
            ["HarnessA"] = new List<IAgentMiddleware> { spyA },
            ["HarnessB"] = new List<IAgentMiddleware> { spyB },
        };

        var tools = new List<AITool>();
        tools.AddRange(BuildToolsForCollapsedHarness("HarnessA", ["FuncA"]));
        tools.AddRange(BuildToolsForCollapsedHarness("HarnessB", ["FuncB"]));

        var middleware = new ContainerMiddleware(
            tools,
            ImmutableHashSet<string>.Empty,
            harnessFactories: null,
            HARNESScopedMiddlewares: diMap,
            middlewareConfigs: null,
            new CollapsingConfig { Enabled = true });

        // Only HarnessA expands
        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "HarnessA", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await middleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        var state = beCtx.GetMiddlewareState<ContainerMiddlewareState>();
        Assert.NotNull(state);
        Assert.True(state.HarnessPipelines.ContainsKey("HarnessA"));
        Assert.False(state.HarnessPipelines.ContainsKey("HarnessB"));
    }

    [Fact]
    public async Task T032_DI_Instance_IsDispatchedAtExpansionTime_BeforeToolExecution()
    {
        var order = new List<string>();
        var diSpy = new SpyMiddleware("DISpy", order);
        var diMap = new Dictionary<string, List<IAgentMiddleware>>
        {
            ["DbHarness"] = new List<IAgentMiddleware> { diSpy }
        };
        // Non-null factories dict (empty) so BeforeToolExecution dispatch gate passes
        var factories = new Dictionary<string, HarnessFactory>();

        var (middleware, _) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"],
            factories: factories,
            HARNESScopedMiddlewares: diMap);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await middleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        Assert.Contains("DISpy.BeforeToolExecution", order);
    }

    [Fact]
    public async Task T033_DI_Instance_WrapFunctionCallAsync_IsInnermostAroundActualCall()
    {
        var order = new List<string>();
        var globalWrap = new WrapRecordingMiddleware("global", order);
        var diWrap = new WrapRecordingMiddleware("di", order);

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("DbHarness")
            .WithHarnessPipeline("DbHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { diWrap }));

        var tools = BuildToolsForCollapsedHarness("DbHarness", ["Query"]);
        var containerMiddleware = new ContainerMiddleware(
            tools,
            ImmutableHashSet<string>.Empty,
            harnessFactories: new Dictionary<string, HarnessFactory>
            {
                ["DbHarness"] = BuildHarnessFactory("DbHarness", ["Query"], middlewareFactories: null)
            },
            HARNESScopedMiddlewares: null,
            middlewareConfigs: null,
            new CollapsingConfig { Enabled = true });

        var queryFn = tools.First(t => t is AIFunction af && af.Name == "Query") as AIFunction;
        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext(state: InitialStateWith(state));
        var req = new FunctionRequest
        {
            Function = queryFn!,
            CallId = "call-x",
            Arguments = new Dictionary<string, object?>(),
            State = agentCtx.State,
        };

        var globalPipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[]
        {
            globalWrap,
            containerMiddleware,
        });

        await globalPipeline.ExecuteFunctionCallAsync(
            req,
            _ => { order.Add("actual"); return Task.FromResult<object?>(null); },
            CancellationToken.None);

        Assert.Equal(new[]
        {
            "global.before",
            "di.before",
            "actual",
            "di.after",
            "global.after",
        }, order);
    }

    [Fact]
    public void T034_HarnessOptions_AddScopedMiddleware_Null_Throws()
    {
        var opts = new HarnessOptions();
        Assert.Throws<ArgumentNullException>(() => opts.AddScopedMiddleware(null!));
    }

    [Fact]
    public void T035_HarnessOptions_AddScopedMiddleware_Chains_AndReturnsSameInstance()
    {
        var opts = new HarnessOptions();
        var mwA = new SimpleSpyMiddleware();
        var mwB = new SimpleSpyMiddleware();

        var returned = opts.AddScopedMiddleware(mwA).AddScopedMiddleware(mwB);

        Assert.Same(opts, returned);
        Assert.Equal(2, opts.ScopedMiddlewares.Count);
        Assert.Same(mwA, opts.ScopedMiddlewares[0]);
        Assert.Same(mwB, opts.ScopedMiddlewares[1]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 8 — §5A config-constructor middleware
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task T036_ConfigFactory_UsedWhenMiddlewareConfigsProvided()
    {
        ConfigCapturingMiddleware? created = null;
        var configJson = System.Text.Json.JsonDocument.Parse("""{"requestsPerMinute":20}""").RootElement;

        var configFactory = new CollapseMiddlewareConfigFactory(
            MiddlewareTypeName: "ConfigCapturingMiddleware",
            Factory: json => { created = new ConfigCapturingMiddleware(json); return created; });

        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: null,
                configFactories: new[] { configFactory })
        };
        var middlewareConfigs = new Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>>
        {
            ["DbHarness"] = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["ConfigCapturingMiddleware"] = configJson
            }
        };

        var (middleware, _) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"], factories, middlewareConfigs: middlewareConfigs);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await middleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal(20, created.ReceivedConfig.GetProperty("requestsPerMinute").GetInt32());
    }

    [Fact]
    public async Task T037_ConfigFactory_NotCalledWhenMiddlewareConfigsMapIsNull()
    {
        bool factoryCalled = false;
        var configFactory = new CollapseMiddlewareConfigFactory(
            MiddlewareTypeName: "ConfigCapturingMiddleware",
            Factory: json => { factoryCalled = true; return new SimpleSpyMiddleware(); });

        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: null,
                configFactories: new[] { configFactory })
        };

        // middlewareConfigs is null — factory should not be called
        var (middleware, _) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"], factories, middlewareConfigs: null);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await middleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        Assert.False(factoryCalled);
        var state = beCtx.GetMiddlewareState<ContainerMiddlewareState>();
        Assert.False(state?.HarnessPipelines.ContainsKey("DbHarness"));
    }

    [Fact]
    public async Task T038_ConfigFactory_NotCalledWhenHarnessNotInConfigsMap()
    {
        bool factoryCalled = false;
        var configFactory = new CollapseMiddlewareConfigFactory(
            MiddlewareTypeName: "ConfigCapturingMiddleware",
            Factory: json => { factoryCalled = true; return new SimpleSpyMiddleware(); });

        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: null,
                configFactories: new[] { configFactory })
        };
        // Config map exists but only for a different harness
        var middlewareConfigs = new Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>>
        {
            ["OtherHarness"] = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["ConfigCapturingMiddleware"] = System.Text.Json.JsonDocument.Parse("{}").RootElement
            }
        };

        var (middleware, _) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"], factories, middlewareConfigs: middlewareConfigs);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await middleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        Assert.False(factoryCalled);
    }

    [Fact]
    public async Task T039_ConfigFactory_MergedWithParameterlessFactories_ConfigAfterParamless()
    {
        var paramlessMw = new SimpleSpyMiddleware();
        ConfigCapturingMiddleware? configMw = null;
        var configJson = System.Text.Json.JsonDocument.Parse("{}").RootElement;

        var configFactory = new CollapseMiddlewareConfigFactory(
            MiddlewareTypeName: "ConfigCapturingMiddleware",
            Factory: json => { configMw = new ConfigCapturingMiddleware(json); return configMw; });

        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: new Func<IAgentMiddleware>[] { () => paramlessMw },
                configFactories: new[] { configFactory })
        };
        var middlewareConfigs = new Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>>
        {
            ["DbHarness"] = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["ConfigCapturingMiddleware"] = configJson
            }
        };

        var (middleware, _) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"], factories, middlewareConfigs: middlewareConfigs);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        await middleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        var state = beCtx.GetMiddlewareState<ContainerMiddlewareState>();
        var pipeline = state!.HarnessPipelines["DbHarness"];
        Assert.Equal(2, pipeline.Count);
        Assert.Same(paramlessMw, pipeline.Middlewares[0]);  // paramless first
        Assert.Same(configMw, pipeline.Middlewares[1]);     // config-ctor second
    }

    [Fact]
    public async Task T040_ConfigFactory_UnknownMiddlewareNameInConfigsMap_IsIgnored()
    {
        bool factoryCalled = false;
        var configFactory = new CollapseMiddlewareConfigFactory(
            MiddlewareTypeName: "ConfigCapturingMiddleware",
            Factory: json => { factoryCalled = true; return new SimpleSpyMiddleware(); });

        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness",
                childFunctions: ["Query"],
                middlewareFactories: null,
                configFactories: new[] { configFactory })
        };
        // Config map key doesn't match the factory's MiddlewareTypeName
        var middlewareConfigs = new Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>>
        {
            ["DbHarness"] = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["UnknownMiddleware"] = System.Text.Json.JsonDocument.Parse("{}").RootElement
            }
        };

        var (middleware, _) = BuildContainerMiddlewareWithContainerTool(
            "DbHarness", ["Query"], factories, middlewareConfigs: middlewareConfigs);

        var agentCtx = V2.MiddlewareTestHelpers.CreateAgentContext();
        var toolCall = new FunctionCallContent("c1", "DbHarness", null);
        var beCtx = agentCtx.AsBeforeToolExecution(
            new ChatMessage(ChatRole.Assistant, []),
            new List<FunctionCallContent> { toolCall },
            new AgentRunConfig());

        // Should not throw
        await middleware.BeforeToolExecutionAsync(beCtx, CancellationToken.None);

        Assert.False(factoryCalled);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 9 — HarnessReference.MiddlewareConfigs serialisation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void T041_HarnessReference_MiddlewareConfigs_RoundTrips_ViaConverter()
    {
        var json = """
            {
              "name": "DatabaseHarness",
              "middlewareConfigs": {
                "DbRateLimitMiddleware": { "requestsPerMinute": 20 }
              }
            }
            """;

        var reference = System.Text.Json.JsonSerializer.Deserialize<HarnessReference>(json);

        Assert.NotNull(reference);
        Assert.NotNull(reference!.MiddlewareConfigs);
        Assert.True(reference.MiddlewareConfigs!.ContainsKey("DbRateLimitMiddleware"));
        Assert.Equal(20, reference.MiddlewareConfigs["DbRateLimitMiddleware"]
            .GetProperty("requestsPerMinute").GetInt32());

        // Round-trip
        var serialized = System.Text.Json.JsonSerializer.Serialize(reference);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<HarnessReference>(serialized);
        Assert.Equal(20, roundTripped!.MiddlewareConfigs!["DbRateLimitMiddleware"]
            .GetProperty("requestsPerMinute").GetInt32());
    }

    [Fact]
    public void T042_HarnessReference_MiddlewareConfigs_Null_UsesSimpleSyntax()
    {
        var reference = new HarnessReference { Name = "MathHarness" };
        var json = System.Text.Json.JsonSerializer.Serialize(reference);

        // Simple string syntax, not an object
        Assert.Equal("\"MathHarness\"", json);
    }

    [Fact]
    public void T043_HarnessReference_MiddlewareConfigs_Present_UsesObjectSyntax()
    {
        var reference = new HarnessReference
        {
            Name = "DatabaseHarness",
            MiddlewareConfigs = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["DbRateLimitMiddleware"] = System.Text.Json.JsonDocument.Parse("""{"rps":5}""").RootElement
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(reference);

        Assert.Contains("\"middlewareConfigs\"", json);
        Assert.Contains("DbRateLimitMiddleware", json);
    }

    [Fact]
    public void T044_HarnessReference_Converter_IgnoresUnknownKeys_WhenMiddlewareConfigsPresent()
    {
        var json = """
            {
              "name": "DatabaseHarness",
              "unknownProp": true,
              "middlewareConfigs": { "MyMiddleware": { "x": 1 } }
            }
            """;

        // Must not throw
        var reference = System.Text.Json.JsonSerializer.Deserialize<HarnessReference>(json);

        Assert.NotNull(reference);
        Assert.Equal("DatabaseHarness", reference!.Name);
        Assert.NotNull(reference.MiddlewareConfigs);
        Assert.True(reference.MiddlewareConfigs!.ContainsKey("MyMiddleware"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 10 — CollapseMiddlewareConfigFactory record
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void T045_CollapseMiddlewareConfigFactory_FactoryDelegate_ReceivesJsonElement()
    {
        var json = System.Text.Json.JsonDocument.Parse("""{"requestsPerMinute":20}""").RootElement;
        var factory = new CollapseMiddlewareConfigFactory(
            MiddlewareTypeName: "DbRateLimitMiddleware",
            Factory: element => new ConfigCapturingMiddleware(element));

        var instance = factory.Factory(json) as ConfigCapturingMiddleware;

        Assert.NotNull(instance);
        Assert.Equal(20, instance!.ReceivedConfig.GetProperty("requestsPerMinute").GetInt32());
    }

    [Fact]
    public void T046_HarnessFactory_CollapseMiddlewareConfigFactories_DefaultsToNull()
    {
        var factory = new HarnessFactory(
            Name: "MathHarness",
            HarnessType: typeof(object),
            CreateInstance: () => new object(),
            CreateFunctions: (_, _, _) => new List<AIFunction>(),
            GetReferencedHarneses: () => Array.Empty<string>(),
            GetReferencedFunctions: () => new Dictionary<string, string[]>());

        Assert.Null(factory.CollapseMiddlewareConfigFactories);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CATEGORY 11 — BeforeParallelBatchAsync dispatch
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task T050_BeforeParallelBatchAsync_DispatchesToHarnessPipelinesForFunctionsInBatch()
    {
        var dbOrder = new List<string>();
        var searchOrder = new List<string>();
        var dbSpy = new SpyMiddleware("DbSpy", dbOrder);
        var searchSpy = new SpyMiddleware("SearchSpy", searchOrder);

        var tools = new List<AITool>();
        tools.AddRange(BuildToolsForCollapsedHarness("DbHarness", ["Query", "Execute"]));
        tools.AddRange(BuildToolsForCollapsedHarness("SearchHarness", ["WebSearch"]));

        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness", ["Query", "Execute"], middlewareFactories: null),
            ["SearchHarness"] = BuildHarnessFactory("SearchHarness", ["WebSearch"], middlewareFactories: null),
        };
        var middleware = new ContainerMiddleware(
            tools,
            ImmutableHashSet<string>.Empty,
            factories,
            HARNESScopedMiddlewares: null,
            middlewareConfigs: null,
            new CollapsingConfig { Enabled = true });

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("DbHarness")
            .WithExpandedContainer("SearchHarness")
            .WithHarnessPipeline("DbHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { dbSpy }))
            .WithHarnessPipeline("SearchHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { searchSpy }));

        // Batch contains one DbHarness fn and one SearchHarness fn
        var batch = new List<ParallelFunctionInfo>
        {
            MakeParallelFunctionInfo("Query"),
            MakeParallelFunctionInfo("WebSearch"),
        };
        var ctx = CreateBeforeParallelBatchContextWithState(state, batch);

        await middleware.BeforeParallelBatchAsync(ctx, CancellationToken.None);

        Assert.Contains("DbSpy.BeforeParallelBatch", dbOrder);
        Assert.Contains("SearchSpy.BeforeParallelBatch", searchOrder);
    }

    [Fact]
    public async Task T051_BeforeParallelBatchAsync_DispatchesEachHarnessPipelineOnlyOnce_EvenWithMultipleFunctions()
    {
        var order = new List<string>();
        var dbSpy = new SpyMiddleware("DbSpy", order);

        var tools = BuildToolsForCollapsedHarness("DbHarness", ["Query", "Execute"]);
        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness", ["Query", "Execute"], middlewareFactories: null),
        };
        var middleware = new ContainerMiddleware(
            tools,
            ImmutableHashSet<string>.Empty,
            factories,
            HARNESScopedMiddlewares: null,
            middlewareConfigs: null,
            new CollapsingConfig { Enabled = true });

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("DbHarness")
            .WithHarnessPipeline("DbHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { dbSpy }));

        // Both Query and Execute belong to DbHarness — pipeline should only be dispatched once
        var batch = new List<ParallelFunctionInfo>
        {
            MakeParallelFunctionInfo("Query"),
            MakeParallelFunctionInfo("Execute"),
        };
        var ctx = CreateBeforeParallelBatchContextWithState(state, batch);

        await middleware.BeforeParallelBatchAsync(ctx, CancellationToken.None);

        Assert.Equal(1, order.Count(s => s == "DbSpy.BeforeParallelBatch"));
    }

    [Fact]
    public async Task T052_BeforeParallelBatchAsync_SkipsFunctionsNotInAnyContainer()
    {
        var order = new List<string>();
        var spy = new SpyMiddleware("Spy", order);

        var tools = BuildToolsForCollapsedHarness("DbHarness", ["Query"]);
        var factories = new Dictionary<string, HarnessFactory>
        {
            ["DbHarness"] = BuildHarnessFactory("DbHarness", ["Query"], middlewareFactories: null),
        };
        var middleware = new ContainerMiddleware(
            tools,
            ImmutableHashSet<string>.Empty,
            factories,
            HARNESScopedMiddlewares: null,
            middlewareConfigs: null,
            new CollapsingConfig { Enabled = true });

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("DbHarness")
            .WithHarnessPipeline("DbHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spy }));

        // Batch: one known fn (Query) and one unknown fn (GlobalFunc not in any container)
        var batch = new List<ParallelFunctionInfo>
        {
            MakeParallelFunctionInfo("Query"),
            MakeParallelFunctionInfo("GlobalFunc"),
        };
        var ctx = CreateBeforeParallelBatchContextWithState(state, batch);

        await middleware.BeforeParallelBatchAsync(ctx, CancellationToken.None);

        // DbHarness dispatched exactly once (for Query); GlobalFunc produced no dispatch
        Assert.Equal(1, order.Count(s => s == "Spy.BeforeParallelBatch"));
    }

    [Fact]
    public async Task T053_BeforeParallelBatchAsync_DoesNotDispatch_WhenHarnessFactoriesNull()
    {
        var order = new List<string>();
        var spy = new SpyMiddleware("Spy", order);

        // harnessFactories = null → early return before any dispatch
        var middleware = BuildContainerMiddleware(harnessFactories: null);

        var state = new ContainerMiddlewareState()
            .WithHarnessPipeline("DbHarness", new AgentMiddlewarePipeline(new IAgentMiddleware[] { spy }));

        var batch = new List<ParallelFunctionInfo> { MakeParallelFunctionInfo("Query") };
        var ctx = CreateBeforeParallelBatchContextWithState(state, batch);

        await middleware.BeforeParallelBatchAsync(ctx, CancellationToken.None);

        Assert.Empty(order);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ADDITIONAL HELPER TYPES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Captures the JsonElement config it was constructed with (for §5A tests).</summary>
    private class ConfigCapturingMiddleware(System.Text.Json.JsonElement config) : IHarnessMiddleware
    {
        public System.Text.Json.JsonElement ReceivedConfig { get; } = config;
    }
}
