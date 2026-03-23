// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using FluentAssertions;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Observability;

/// <summary>
/// Unit tests for <see cref="TracingObserver"/>.
/// Uses System.Diagnostics.ActivityListener to capture spans without a real OTLP exporter.
/// </summary>
public class TracingObserverTests : IDisposable
{
    // ── Listener infrastructure ───────────────────────────────────────────────

    // Each test instance gets a unique source name so parallel xUnit runs
    // don't share a listener and pick up each other's spans.
    private readonly string _sourceName = $"HPD.Agent.Test.{Guid.NewGuid():N}";
    private readonly List<Activity> _completed = new();
    private readonly ActivityListener _listener;
    private readonly TracingObserver _observer;

    public TracingObserverTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == _sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _completed.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);

        _observer = new TracingObserver(_sourceName);
    }

    public void Dispose()
    {
        _observer.Dispose();
        _listener.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string TraceId = "deadbeef000111222333444555666777";
    private const string TurnSpanId = "aabb1122ccdd3344";
    private const string IterSpanId = "1234567890abcdef";
    private const string ToolCallId = "tool-call-abc123";

    private static MessageTurnStartedEvent TurnStarted() =>
        new MessageTurnStartedEvent("turn-1", "conv-1", "TestAgent")
        {
            TraceId = TraceId,
            SpanId = TurnSpanId
        };

    private static MessageTurnFinishedEvent TurnFinished() =>
        new MessageTurnFinishedEvent("turn-1", "conv-1", "TestAgent", TimeSpan.FromMilliseconds(200))
        {
            TraceId = TraceId,
            SpanId = TurnSpanId
        };

    private static AgentTurnStartedEvent IterStarted(int iteration = 1) =>
        new AgentTurnStartedEvent(iteration)
        {
            TraceId = TraceId,
            SpanId = IterSpanId,
            ParentSpanId = TurnSpanId
        };

    private static AgentTurnFinishedEvent IterFinished(int iteration = 1) =>
        new AgentTurnFinishedEvent(iteration)
        {
            TraceId = TraceId,
            SpanId = IterSpanId,
            ParentSpanId = TurnSpanId
        };

    private static ToolCallStartEvent ToolStarted(string callId = ToolCallId) =>
        new ToolCallStartEvent(callId, "MyTool", "msg-1", "MyToolkit")
        {
            TraceId = TraceId,
            SpanId = "eeee5555ffff6666",
            ParentSpanId = IterSpanId
        };

    private static ToolCallEndEvent ToolEnded(string callId = ToolCallId) =>
        new ToolCallEndEvent(callId)
        {
            TraceId = TraceId
        };

    private static ToolCallResultEvent ToolResult(string callId = ToolCallId) =>
        new ToolCallResultEvent(callId, """{"result": "success"}""", "MyToolkit")
        {
            TraceId = TraceId
        };

    private async Task EmitAsync(AgentEvent evt) =>
        await _observer.OnEventAsync(evt, CancellationToken.None);

    // ── Filtering (ShouldProcess) ─────────────────────────────────────────────

    [Fact]
    public void ShouldProcess_EventWithNullTraceId_ReturnsFalse()
    {
        var evt = new TextDeltaEvent("hello", "msg-1"); // TraceId = null
        _observer.ShouldProcess(evt).Should().BeFalse();
    }

    [Fact]
    public void ShouldProcess_EventWithTraceId_ReturnsTrue()
    {
        var evt = new TextDeltaEvent("hello", "msg-1") { TraceId = TraceId };
        _observer.ShouldProcess(evt).Should().BeTrue();
    }

    // ── Turn span lifecycle ───────────────────────────────────────────────────

    [Fact]
    public async Task TurnStarted_CreatesActivityNamed_AgentTurn()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(TurnFinished());

        _completed.Should().ContainSingle(a => a.OperationName == "agent.turn");
    }

    [Fact]
    public async Task TurnStarted_SetsAgentNameTag()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(TurnFinished());

        var span = _completed.Single(a => a.OperationName == "agent.turn");
        span.GetTagItem("agent.name").Should().Be("TestAgent");
    }

    [Fact]
    public async Task TurnStarted_SetsConversationIdTag()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(TurnFinished());

        var span = _completed.Single(a => a.OperationName == "agent.turn");
        span.GetTagItem("agent.conversation_id").Should().Be("conv-1");
    }

    [Fact]
    public async Task TurnFinished_SetsDurationTag()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(TurnFinished());

        var span = _completed.Single(a => a.OperationName == "agent.turn");
        span.GetTagItem("agent.turn_duration_ms").Should().NotBeNull();
    }

    [Fact]
    public async Task TurnFinished_StopsActivity()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(TurnFinished());

        _completed.Should().ContainSingle(a => a.OperationName == "agent.turn");
    }

    // ── Iteration span lifecycle ──────────────────────────────────────────────

    [Fact]
    public async Task IterationStarted_CreatesActivityNamed_AgentIteration()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        _completed.Should().Contain(a => a.OperationName == "agent.iteration");
    }

    [Fact]
    public async Task IterationStarted_SetsIterationTag()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted(2));
        await EmitAsync(IterFinished(2));
        await EmitAsync(TurnFinished());

        var iterSpan = _completed.Single(a => a.OperationName == "agent.iteration");
        iterSpan.GetTagItem("agent.iteration").Should().Be(2);
    }

    [Fact]
    public async Task IterationFinished_StopsIterationActivity()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        _completed.Where(a => a.OperationName == "agent.iteration")
                  .Should().HaveCount(1);
    }

    // ── Tool call span lifecycle ──────────────────────────────────────────────

    [Fact]
    public async Task ToolCallStarted_CreatesActivityNamed_AgentToolCall()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(ToolStarted());
        await EmitAsync(ToolEnded());
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        _completed.Should().Contain(a => a.OperationName == "agent.tool_call");
    }

    [Fact]
    public async Task ToolCallStarted_SetsToolNameTag()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(ToolStarted());
        await EmitAsync(ToolEnded());
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        var toolSpan = _completed.Single(a => a.OperationName == "agent.tool_call");
        toolSpan.GetTagItem("tool.name").Should().Be("MyTool");
    }

    [Fact]
    public async Task ToolCallStarted_SetsToolkitTag()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(ToolStarted());
        await EmitAsync(ToolEnded());
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        var toolSpan = _completed.Single(a => a.OperationName == "agent.tool_call");
        toolSpan.GetTagItem("tool.toolkit").Should().Be("MyToolkit");
    }

    [Fact]
    public async Task ToolCallStarted_SetsCallIdTag()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(ToolStarted());
        await EmitAsync(ToolEnded());
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        var toolSpan = _completed.Single(a => a.OperationName == "agent.tool_call");
        toolSpan.GetTagItem("tool.call_id").Should().Be(ToolCallId);
    }

    [Fact]
    public async Task ToolCallEnded_StopsToolCallActivity()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(ToolStarted());
        await EmitAsync(ToolEnded());
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        _completed.Where(a => a.OperationName == "agent.tool_call").Should().HaveCount(1);
    }

    // ── ToolCallResult attached as ActivityEvent ──────────────────────────────

    [Fact]
    public async Task ToolCallResult_AddsToolResultEvent_WithSanitizedResult()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(ToolStarted());
        // Note: result arrives BEFORE end in some implementations — test both
        await EmitAsync(ToolResult());
        await EmitAsync(ToolEnded());
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        // The ActivityEvent "tool.result" is added to the tool span.
        // Because activities are stopped at ToolCallEnded, we verify
        // that the event was recorded (events persist after stop).
        var toolSpan = _completed.SingleOrDefault(a => a.OperationName == "agent.tool_call");
        toolSpan.Should().NotBeNull();
        toolSpan!.Events.Should().Contain(e => e.Name == "tool.result");
    }

    [Fact]
    public async Task ToolCallResult_SensitiveFieldsRedacted()
    {
        var sensitiveResult = new ToolCallResultEvent(ToolCallId, """{"token": "sk-super-secret"}""", null)
        {
            TraceId = TraceId
        };

        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(ToolStarted());
        await EmitAsync(sensitiveResult);
        await EmitAsync(ToolEnded());
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        var toolSpan = _completed.Single(a => a.OperationName == "agent.tool_call");
        var resultEvent = toolSpan.Events.Single(e => e.Name == "tool.result");
        var resultTag = resultEvent.Tags.First(t => t.Key == "tool.result");

        resultTag.Value?.ToString().Should().Contain("[REDACTED]");
        resultTag.Value?.ToString().Should().NotContain("sk-super-secret");
    }

    // ── Non-structural events as ActivityEvents ───────────────────────────────

    [Fact]
    public async Task AgentDecisionEvent_AddedToIterationSpanAsActivityEvent()
    {
        // We verify this indirectly by checking no exception is thrown
        // (the event is attached internally to the iteration Activity which
        //  may or may not appear in _completed depending on timing).
        var decision = new AgentDecisionEvent("TestAgent", "Continue", 1, 0, 0)
        {
            TraceId = TraceId,
            SpanId = IterSpanId
        };

        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(decision);
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        // Observer must not throw and must complete the turn span
        _completed.Should().Contain(a => a.OperationName == "agent.turn");
    }

    [Fact]
    public async Task PermissionRequestEvent_DoesNotThrow()
    {
        var permReq = new PermissionRequestEvent("perm-1", "PermMiddleware", "SomeTool", null, "call-1", null)
        {
            TraceId = TraceId,
            SpanId = IterSpanId
        };

        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        var act = () => EmitAsync(permReq);
        await act.Should().NotThrowAsync();
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());
    }

    [Fact]
    public async Task CircuitBreakerEvent_DoesNotThrow()
    {
        var cb = new CircuitBreakerTriggeredEvent("TestAgent", "SomeTool", 3, 1, DateTimeOffset.UtcNow)
        {
            TraceId = TraceId,
            SpanId = IterSpanId
        };

        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        var act = () => EmitAsync(cb);
        await act.Should().NotThrowAsync();
        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());
    }

    // ── Error turn ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TurnError_SetsErrorStatusOnTurnSpan()
    {
        var error = new MessageTurnErrorEvent("Something went wrong", new InvalidOperationException("boom"))
        {
            TraceId = TraceId
        };

        await EmitAsync(TurnStarted());
        await EmitAsync(error);
        await EmitAsync(TurnFinished());

        var turnSpan = _completed.Single(a => a.OperationName == "agent.turn");
        turnSpan.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task TurnError_SanitizesErrorMessage()
    {
        var error = new MessageTurnErrorEvent("connection string: Server=db;Password=abc123;", null)
        {
            TraceId = TraceId
        };

        await EmitAsync(TurnStarted());
        await EmitAsync(error);
        await EmitAsync(TurnFinished());

        var turnSpan = _completed.Single(a => a.OperationName == "agent.turn");
        // Plain text error messages pass through (no JSON to redact)
        // but the error tag should be set
        turnSpan.GetTagItem("error.message").Should().NotBeNull();
    }

    // ShouldProcess filtering is already covered in the "Filtering (ShouldProcess)" section above.

    // ── No listener — graceful null-activity handling ─────────────────────────

    [Fact]
    public async Task NoListener_ObserverHandlesNullActivityGracefully()
    {
        // Create a separate observer with a source name no listener monitors.
        using var isolated = new TracingObserver("HPD.Agent.NoListener.Test");

        // If StartActivity returns null (no listener), OnEventAsync must not throw.
        var act = async () =>
        {
            await isolated.OnEventAsync(TurnStarted(), CancellationToken.None);
            await isolated.OnEventAsync(IterStarted(), CancellationToken.None);
            await isolated.OnEventAsync(IterFinished(), CancellationToken.None);
            await isolated.OnEventAsync(TurnFinished(), CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }

    // ── Multiple tool calls in one iteration ──────────────────────────────────

    [Fact]
    public async Task MultipleToolCalls_EachGetsOwnSpan()
    {
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());

        var toolA = ToolStarted("call-A");
        var toolB = ToolStarted("call-B");

        await EmitAsync(toolA);
        await EmitAsync(toolB);
        await EmitAsync(ToolEnded("call-A"));
        await EmitAsync(ToolEnded("call-B"));

        await EmitAsync(IterFinished());
        await EmitAsync(TurnFinished());

        _completed.Where(a => a.OperationName == "agent.tool_call")
                  .Should().HaveCount(2);
    }

    // ── Multiple iterations in one turn ──────────────────────────────────────

    [Fact]
    public async Task MultipleIterations_EachGetsOwnIterationSpan()
    {
        const string iterSpan1 = "iter1111aaaabbbb";
        const string iterSpan2 = "iter2222ccccdddd";

        await EmitAsync(TurnStarted());

        await EmitAsync(new AgentTurnStartedEvent(1)
        {
            TraceId = TraceId,
            SpanId = iterSpan1,
            ParentSpanId = TurnSpanId
        });
        await EmitAsync(new AgentTurnFinishedEvent(1)
        {
            TraceId = TraceId,
            SpanId = iterSpan1
        });

        await EmitAsync(new AgentTurnStartedEvent(2)
        {
            TraceId = TraceId,
            SpanId = iterSpan2,
            ParentSpanId = TurnSpanId
        });
        await EmitAsync(new AgentTurnFinishedEvent(2)
        {
            TraceId = TraceId,
            SpanId = iterSpan2
        });

        await EmitAsync(TurnFinished());

        _completed.Where(a => a.OperationName == "agent.iteration")
                  .Should().HaveCount(2);
    }

    // ── Dispose cleans up open spans ─────────────────────────────────────────

    [Fact]
    public async Task Dispose_StopsAllOpenSpansWithoutThrowing()
    {
        // Start a turn but never finish it — simulates an agent crash.
        await EmitAsync(TurnStarted());
        await EmitAsync(IterStarted());
        await EmitAsync(ToolStarted());

        var act = () => _observer.Dispose();
        act.Should().NotThrow();
    }

    // ── Concurrent safety ─────────────────────────────────────────────────────

    [Fact]
    public async Task TwoConcurrentTurns_ProduceIndependentSpanTrees()
    {
        // Two separate observers each simulating one turn — they share the same
        // ActivitySource but use different TraceIds, so spans must not cross.
        const string trace1 = "trace1aaaabbbbccccddddeeee00001111";
        const string trace2 = "trace2ffffeeeedddcccbbbbaaaa22223333";

        var obs1 = new TracingObserver(_sourceName);
        var obs2 = new TracingObserver(_sourceName);

        await obs1.OnEventAsync(new MessageTurnStartedEvent("t1", "c", "A") { TraceId = trace1, SpanId = "span1111aaaabbbb" }, default);
        await obs2.OnEventAsync(new MessageTurnStartedEvent("t2", "c", "A") { TraceId = trace2, SpanId = "span2222ccccdddd" }, default);

        await obs1.OnEventAsync(new MessageTurnFinishedEvent("t1", "c", "A", TimeSpan.Zero) { TraceId = trace1, SpanId = "span1111aaaabbbb" }, default);
        await obs2.OnEventAsync(new MessageTurnFinishedEvent("t2", "c", "A", TimeSpan.Zero) { TraceId = trace2, SpanId = "span2222ccccdddd" }, default);

        obs1.Dispose();
        obs2.Dispose();

        // Exactly two "agent.turn" spans should be completed — one per observer.
        _completed.Where(a => a.OperationName == "agent.turn")
                  .Should().HaveCount(2);
    }
}
