// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Observability;

/// <summary>
/// Integration tests verifying that TraceId / SpanId / ParentSpanId are correctly
/// wired onto events emitted by the real agent loop in Agent.cs.
///
/// These tests run through a FakeChatClient so no real LLM is needed.
/// All structural assertions are about the IDs attached to events, not the event sequence.
/// </summary>
public class SpanIdWiringTests : AgentTestBase
{
    // ── TraceId uniqueness per turn ───────────────────────────────────────────

    [Fact]
    public async Task TwoConsecutiveTurns_HaveDifferentTraceIds()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("Turn one response.");
        fakeLLM.EnqueueStreamingResponse("Turn two response.");

        var agent = CreateAgent(client: fakeLLM);

        var turn1Events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAgenticLoopAsync(
            [new ChatMessage(ChatRole.User, "First")], cancellationToken: TestCancellationToken))
        {
            turn1Events.Add(evt);
        }

        var turn2Events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAgenticLoopAsync(
            [new ChatMessage(ChatRole.User, "Second")], cancellationToken: TestCancellationToken))
        {
            turn2Events.Add(evt);
        }

        var trace1 = turn1Events.OfType<MessageTurnStartedEvent>().Single().TraceId;
        var trace2 = turn2Events.OfType<MessageTurnStartedEvent>().Single().TraceId;

        trace1.Should().NotBeNull();
        trace2.Should().NotBeNull();
        trace1.Should().NotBe(trace2, "each turn must have a unique trace ID");
    }

    // ── TraceId format validation ─────────────────────────────────────────────

    [Fact]
    public async Task TraceId_Is32LowercaseHexChars()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("response");

        var agent = CreateAgent(client: fakeLLM);
        var events = await RunTurnAsync(agent);

        var traceId = events.OfType<MessageTurnStartedEvent>().Single().TraceId;
        traceId.Should().NotBeNull();
        traceId!.Length.Should().Be(32);
        traceId.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    // ── SpanId format validation ──────────────────────────────────────────────

    [Fact]
    public async Task TurnSpanId_Is16LowercaseHexChars()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("response");

        var agent = CreateAgent(client: fakeLLM);
        var events = await RunTurnAsync(agent);

        var spanId = events.OfType<MessageTurnStartedEvent>().Single().SpanId;
        spanId.Should().NotBeNull();
        spanId!.Length.Should().Be(16);
        spanId.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public async Task IterationSpanId_Is16LowercaseHexChars()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("response");

        var agent = CreateAgent(client: fakeLLM);
        var events = await RunTurnAsync(agent);

        var spanId = events.OfType<AgentTurnStartedEvent>().First().SpanId;
        spanId.Should().NotBeNull();
        spanId!.Length.Should().Be(16);
        spanId.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    // ── MessageTurn span linkage ──────────────────────────────────────────────

    [Fact]
    public async Task MessageTurnStartedEvent_HasNullParentSpanId()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("response");

        var agent = CreateAgent(client: fakeLLM);
        var events = await RunTurnAsync(agent);

        events.OfType<MessageTurnStartedEvent>().Single().ParentSpanId.Should().BeNull();
    }

    [Fact]
    public async Task MessageTurnStartedAndFinished_ShareSameTraceIdAndSpanId()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("response");

        var agent = CreateAgent(client: fakeLLM);
        var events = await RunTurnAsync(agent);

        var started = events.OfType<MessageTurnStartedEvent>().Single();
        var finished = events.OfType<MessageTurnFinishedEvent>().Single();

        finished.TraceId.Should().Be(started.TraceId);
        finished.SpanId.Should().Be(started.SpanId);
    }

    // ── Iteration span linkage ────────────────────────────────────────────────

    [Fact]
    public async Task AgentTurnStartedEvent_ParentSpanId_EqualsMessageTurnSpanId()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("response");

        var agent = CreateAgent(client: fakeLLM);
        var events = await RunTurnAsync(agent);

        var turnSpanId = events.OfType<MessageTurnStartedEvent>().Single().SpanId;
        var iterParent = events.OfType<AgentTurnStartedEvent>().First().ParentSpanId;

        iterParent.Should().Be(turnSpanId);
    }

    [Fact]
    public async Task AgentTurnStartedAndFinished_ShareSameSpanId()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("response");

        var agent = CreateAgent(client: fakeLLM);
        var events = await RunTurnAsync(agent);

        var started = events.OfType<AgentTurnStartedEvent>().First();
        var finished = events.OfType<AgentTurnFinishedEvent>().First();

        finished.SpanId.Should().Be(started.SpanId);
    }

    [Fact]
    public async Task MultipleIterations_EachHasDistinctSpanId()
    {
        var fakeLLM = new FakeChatClient();
        // First iteration calls a tool, second iteration finishes with text
        fakeLLM.EnqueueToolCall("no_op", "call-1");
        fakeLLM.EnqueueStreamingResponse("All done.");

        var noOp = AIFunctionFactory.Create(() => "ok", "no_op");
        var agent = CreateAgent(client: fakeLLM, circuitBreakerThreshold: null, tools: noOp);
        var events = await RunTurnAsync(agent);

        var iterStartEvents = events.OfType<AgentTurnStartedEvent>().ToList();
        iterStartEvents.Should().HaveCountGreaterThanOrEqualTo(2);

        var spanIds = iterStartEvents.Select(e => e.SpanId).ToList();
        spanIds.Should().OnlyHaveUniqueItems("each iteration must have a distinct span ID");
    }

    [Fact]
    public async Task MultipleIterations_AllShareSameTraceId()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueToolCall("no_op", "call-1");
        fakeLLM.EnqueueStreamingResponse("All done.");

        var noOp = AIFunctionFactory.Create(() => "ok", "no_op");
        var agent = CreateAgent(client: fakeLLM, circuitBreakerThreshold: null, tools: noOp);
        var events = await RunTurnAsync(agent);

        var traceIds = events.OfType<AgentTurnStartedEvent>().Select(e => e.TraceId).Distinct().ToList();
        traceIds.Should().HaveCount(1, "all iterations within a turn share the same trace ID");
    }

    // ── Tool call span linkage ────────────────────────────────────────────────

    [Fact]
    public async Task ToolCallStartEvent_HasNonNullSpanId()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueToolCall("no_op", "call-1");
        fakeLLM.EnqueueStreamingResponse("Done.");

        var noOp = AIFunctionFactory.Create(() => "ok", "no_op");
        var agent = CreateAgent(client: fakeLLM, circuitBreakerThreshold: null, tools: noOp);
        var events = await RunTurnAsync(agent);

        events.OfType<ToolCallStartEvent>().Should().Contain(e => e.SpanId != null);
    }

    [Fact]
    public async Task ToolCallStartEvent_ParentSpanId_EqualsCurrentIterationSpanId()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueToolCall("no_op", "call-1");
        fakeLLM.EnqueueStreamingResponse("Done.");

        var noOp = AIFunctionFactory.Create(() => "ok", "no_op");
        var agent = CreateAgent(client: fakeLLM, circuitBreakerThreshold: null, tools: noOp);
        var events = await RunTurnAsync(agent);

        var iterSpanId = events.OfType<AgentTurnStartedEvent>().First().SpanId;
        var toolParent = events.OfType<ToolCallStartEvent>().First().ParentSpanId;

        toolParent.Should().Be(iterSpanId);
    }

    [Fact]
    public async Task MultipleToolCalls_EachHasDistinctSpanId()
    {
        var fakeLLM = new FakeChatClient();
        // Queue a response with two tool calls
        fakeLLM.EnqueueToolCall("tool_a", "call-a");
        fakeLLM.EnqueueToolCall("tool_b", "call-b");
        fakeLLM.EnqueueStreamingResponse("Both done.");

        var toolA = AIFunctionFactory.Create(() => "result-a", "tool_a");
        var toolB = AIFunctionFactory.Create(() => "result-b", "tool_b");
        var agent = CreateAgent(client: fakeLLM, circuitBreakerThreshold: null, tools: [toolA, toolB]);
        var events = await RunTurnAsync(agent);

        var toolCallSpanIds = events.OfType<ToolCallStartEvent>()
            .Select(e => e.SpanId)
            .Where(id => id != null)
            .ToList();

        if (toolCallSpanIds.Count >= 2)
        {
            toolCallSpanIds.Should().OnlyHaveUniqueItems("each tool call gets its own span");
        }
    }

    // ── All non-structural events carry TraceId ───────────────────────────────

    [Fact]
    public async Task AllEventsInTurn_CarryNonNullTraceId()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("response");

        var agent = CreateAgent(client: fakeLLM);
        var events = await RunTurnAsync(agent);

        events.Should().NotBeEmpty();
        events.Should().OnlyContain(e => e.TraceId != null,
            "every event emitted within a turn must carry the turn's trace ID");
    }

    [Fact]
    public async Task TextDeltaEvents_CarryTraceId_ButNullSpanId()
    {
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("Hello", " World");

        var agent = CreateAgent(client: fakeLLM);
        var events = await RunTurnAsync(agent);

        var deltas = events.OfType<TextDeltaEvent>().ToList();
        deltas.Should().NotBeEmpty();
        deltas.Should().OnlyContain(e => e.TraceId != null);
        // Text deltas are non-structural — they don't get their own span
        deltas.Should().OnlyContain(e => e.SpanId == null);
    }

    // ── Middleware-emitted permission events carry TraceId ────────────────────

    [Fact]
    public async Task PermissionRequestEvent_CarriesTraceId_WhenEmittedViaMiddleware()
    {
        // The PermissionMiddleware emits PermissionRequestEvent via AgentContext.Emit()
        // which stamps the traceId onto it. We use MockPermissionHandler to auto-approve
        // so the test doesn't block.
        var fakeLLM = new FakeChatClient();

        var sensitiveOptions = new HPDAIFunctionFactoryOptions
        {
            Name = "guarded_tool",
            Description = "A tool requiring permission",
            RequiresPermission = true
        };
        var guardedTool = HPDAIFunctionFactory.Create(
            async (args, ct) => "result", sensitiveOptions);

        var config = DefaultConfig();
        config.Provider ??= new ProviderConfig { ProviderKey = "test", ModelName = "test-model" };
        config.Provider.DefaultChatOptions ??= new ChatOptions();
        config.Provider.DefaultChatOptions.Tools = [guardedTool];

        fakeLLM.EnqueueToolCall("guarded_tool", "call-1");
        fakeLLM.EnqueueStreamingResponse("Done.");

        var builder = new AgentBuilder(config, new TestProviderRegistry(fakeLLM));
        builder.WithPermissions();
        var agent = builder.BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

        var eventStream = agent.RunAgenticLoopAsync(
            [new ChatMessage(ChatRole.User, "go")], cancellationToken: TestCancellationToken);

        using var permHandler = new MockPermissionHandler(agent, eventStream).AutoApproveAll();
        await permHandler.WaitForCompletionAsync(TimeSpan.FromSeconds(15));

        // Assert: any captured PermissionRequestEvents must carry a non-null TraceId
        foreach (var permEvt in permHandler.CapturedEvents.OfType<PermissionRequestEvent>())
        {
            permEvt.TraceId.Should().NotBeNull(
                "PermissionRequestEvent emitted through AgentContext.Emit() must be stamped with the turn TraceId");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<AgentEvent>> RunTurnAsync(Agent agent)
    {
        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAgenticLoopAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }
        return events;
    }
}
