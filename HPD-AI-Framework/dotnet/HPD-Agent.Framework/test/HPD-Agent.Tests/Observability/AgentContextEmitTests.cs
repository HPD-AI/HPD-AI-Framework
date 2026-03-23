// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Events;
using HPD.Events.Core;
using Xunit;

namespace HPD.Agent.Tests.Observability;

/// <summary>
/// Unit tests for <see cref="AgentContext.Emit"/> TraceId stamping behaviour.
///
/// Rules verified:
///   1. An event emitted with TraceId = null is stamped with the context's TraceId.
///   2. An event that already carries a TraceId is NOT overwritten.
///   3. When AgentContext.TraceId is null the event passes through unchanged.
///   4. Null event argument throws ArgumentNullException.
/// </summary>
public class AgentContextEmitTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (AgentContext context, CapturingEventCoordinator coordinator) BuildContext(string? traceId)
    {
        var coordinator = new CapturingEventCoordinator();
        var initialState = AgentLoopState.InitialSafe(
            messages: [],
            runId: "test-run",
            conversationId: "test-conv",
            agentName: "TestAgent");
        var context = new AgentContext(
            agentName: "TestAgent",
            conversationId: null,
            initialState: initialState,
            eventCoordinator: coordinator,
            session: null,
            branch: null,
            cancellationToken: CancellationToken.None,
            traceId: traceId);
        return (context, coordinator);
    }

    // ── Stamping ──────────────────────────────────────────────────────────────

    [Fact]
    public void Emit_EventWithNullTraceId_GetsStampedWithContextTraceId()
    {
        var (ctx, coordinator) = BuildContext("aaaa0000bbbb1111cccc2222dddd3333");

        var evt = new TextDeltaEvent("hello", "msg-1");
        evt.TraceId.Should().BeNull();

        ctx.Emit(evt);

        coordinator.Captured.Should().ContainSingle();
        var captured = (TextDeltaEvent)coordinator.Captured[0];
        captured.TraceId.Should().Be("aaaa0000bbbb1111cccc2222dddd3333");
    }

    [Fact]
    public void Emit_EventAlreadyHasTraceId_OriginalValuePreserved()
    {
        var (ctx, coordinator) = BuildContext("context-trace-id-here-aaaabbbbcccc");

        var evt = new TextDeltaEvent("hello", "msg-1") { TraceId = "event-trace-id-here-11112222333" };

        ctx.Emit(evt);

        coordinator.Captured.Should().ContainSingle();
        var captured = (TextDeltaEvent)coordinator.Captured[0];
        captured.TraceId.Should().Be("event-trace-id-here-11112222333");
    }

    [Fact]
    public void Emit_ContextTraceIdIsNull_EventPassesThroughUnchanged()
    {
        var (ctx, coordinator) = BuildContext(null);

        var evt = new TextDeltaEvent("hello", "msg-1");

        ctx.Emit(evt);

        coordinator.Captured.Should().ContainSingle();
        var captured = (TextDeltaEvent)coordinator.Captured[0];
        captured.TraceId.Should().BeNull();
    }

    [Fact]
    public void Emit_ContextTraceIdIsNull_EventWithExistingTraceIdPreserved()
    {
        var (ctx, coordinator) = BuildContext(null);

        var evt = new TextDeltaEvent("hello", "msg-1") { TraceId = "some-trace-id-111122223333aaaa" };

        ctx.Emit(evt);

        var captured = (TextDeltaEvent)coordinator.Captured[0];
        captured.TraceId.Should().Be("some-trace-id-111122223333aaaa");
    }

    // ── Null guard ────────────────────────────────────────────────────────────

    [Fact]
    public void Emit_NullEvent_ThrowsArgumentNullException()
    {
        var (ctx, _) = BuildContext("anytraceaaaa0000bbbb1111cccc2222");
        ctx.Invoking(c => c.Emit(null!))
           .Should().Throw<ArgumentNullException>();
    }

    // ── Multiple events ───────────────────────────────────────────────────────

    [Fact]
    public void Emit_MultipleEvents_AllStampedWithSameTraceId()
    {
        var traceId = "fixed-trace-00001111222233334444";
        var (ctx, coordinator) = BuildContext(traceId);

        ctx.Emit(new TextDeltaEvent("a", "msg-1"));
        ctx.Emit(new TextDeltaEvent("b", "msg-1"));
        ctx.Emit(new TextDeltaEvent("c", "msg-1"));

        coordinator.Captured.Should().HaveCount(3);
        coordinator.Captured.Cast<AgentEvent>()
            .Should().OnlyContain(e => e.TraceId == traceId);
    }

    [Fact]
    public void Emit_MixedEvents_OnlyNullTraceIdEventsAreStamped()
    {
        var traceId = "contexttracexxxxyyyyzzzz00001111";
        var (ctx, coordinator) = BuildContext(traceId);

        ctx.Emit(new TextDeltaEvent("no-trace", "msg-1"));                  // null → stamped
        ctx.Emit(new TextDeltaEvent("has-trace", "msg-2") { TraceId = "override-trace-xxxxxxxxxxaaaaaa" }); // existing → preserved

        coordinator.Captured.Should().HaveCount(2);
        ((AgentEvent)coordinator.Captured[0]).TraceId.Should().Be(traceId);
        ((AgentEvent)coordinator.Captured[1]).TraceId.Should().Be("override-trace-xxxxxxxxxxaaaaaa");
    }

    // ── IStreamRegistry-aware coordinator ─────────────────────────────────────

    /// <summary>
    /// A minimal IEventCoordinator that captures every emitted event for assertion.
    /// Uses a real HPD.Events.Core.EventCoordinator for the stream registry plumbing.
    /// </summary>
    private sealed class CapturingEventCoordinator : IEventCoordinator
    {
        private readonly EventCoordinator _inner = new();
        public List<Event> Captured { get; } = new();

        public void Emit(Event evt)
        {
            Captured.Add(evt);
        }

        public void EmitUpstream(Event evt) => _inner.EmitUpstream(evt);
        public bool TryRead(out Event? evt) => _inner.TryRead(out evt);
        public IAsyncEnumerable<Event> ReadAllAsync(CancellationToken ct) => _inner.ReadAllAsync(ct);
        public void SetParent(IEventCoordinator parent) => _inner.SetParent(parent);
        public Task<TResponse> WaitForResponseAsync<TResponse>(string requestId, TimeSpan timeout, CancellationToken ct)
            where TResponse : Event => _inner.WaitForResponseAsync<TResponse>(requestId, timeout, ct);
        public void SendResponse(string requestId, Event response) => _inner.SendResponse(requestId, response);
        public IStreamRegistry Streams => _inner.Streams;
    }
}
