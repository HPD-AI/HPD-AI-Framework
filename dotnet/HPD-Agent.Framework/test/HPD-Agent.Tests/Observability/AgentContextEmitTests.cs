// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Events;
using HPD.Events.Core;
using Xunit;

namespace HPD.Agent.Tests.Observability;

/// <summary>
/// Unit tests for <see cref="AgentContext.PublishAsync"/> TraceId stamping behaviour.
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
            thread: null,
            cancellationToken: CancellationToken.None,
            traceId: traceId);
        return (context, coordinator);
    }

    // ── Stamping ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_EventWithNullTraceId_GetsStampedWithContextTraceId()
    {
        var (ctx, coordinator) = BuildContext("aaaa0000bbbb1111cccc2222dddd3333");

        var evt = new TextDeltaEvent("hello", "msg-1");
        evt.TraceId.Should().BeNull();

        await ctx.PublishAsync(evt);

        coordinator.Captured.Should().ContainSingle();
        var captured = (TextDeltaEvent)coordinator.Captured[0];
        captured.TraceId.Should().Be("aaaa0000bbbb1111cccc2222dddd3333");
    }

    [Fact]
    public async Task PublishAsync_EventAlreadyHasTraceId_OriginalValuePreserved()
    {
        var (ctx, coordinator) = BuildContext("context-trace-id-here-aaaabbbbcccc");

        var evt = new TextDeltaEvent("hello", "msg-1") { TraceId = "event-trace-id-here-11112222333" };

        await ctx.PublishAsync(evt);

        coordinator.Captured.Should().ContainSingle();
        var captured = (TextDeltaEvent)coordinator.Captured[0];
        captured.TraceId.Should().Be("event-trace-id-here-11112222333");
    }

    [Fact]
    public async Task PublishAsync_ContextTraceIdIsNull_EventPassesThroughUnchanged()
    {
        var (ctx, coordinator) = BuildContext(null);

        var evt = new TextDeltaEvent("hello", "msg-1");

        await ctx.PublishAsync(evt);

        coordinator.Captured.Should().ContainSingle();
        var captured = (TextDeltaEvent)coordinator.Captured[0];
        captured.TraceId.Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_ContextTraceIdIsNull_EventWithExistingTraceIdPreserved()
    {
        var (ctx, coordinator) = BuildContext(null);

        var evt = new TextDeltaEvent("hello", "msg-1") { TraceId = "some-trace-id-111122223333aaaa" };

        await ctx.PublishAsync(evt);

        var captured = (TextDeltaEvent)coordinator.Captured[0];
        captured.TraceId.Should().Be("some-trace-id-111122223333aaaa");
    }

    // ── Null guard ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_NullEvent_ThrowsArgumentNullException()
    {
        var (ctx, _) = BuildContext("anytraceaaaa0000bbbb1111cccc2222");
        await ctx.Awaiting(c => c.PublishAsync(null!).AsTask())
            .Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Multiple events ───────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_MultipleEvents_AllStampedWithSameTraceId()
    {
        var traceId = "fixed-trace-00001111222233334444";
        var (ctx, coordinator) = BuildContext(traceId);

        await ctx.PublishAsync(new TextDeltaEvent("a", "msg-1"));
        await ctx.PublishAsync(new TextDeltaEvent("b", "msg-1"));
        await ctx.PublishAsync(new TextDeltaEvent("c", "msg-1"));

        coordinator.Captured.Should().HaveCount(3);
        coordinator.Captured.Cast<AgentEvent>()
            .Should().OnlyContain(e => e.TraceId == traceId);
    }

    [Fact]
    public async Task PublishAsync_MixedEvents_OnlyNullTraceIdEventsAreStamped()
    {
        var traceId = "contexttracexxxxyyyyzzzz00001111";
        var (ctx, coordinator) = BuildContext(traceId);

        await ctx.PublishAsync(new TextDeltaEvent("no-trace", "msg-1"));
        await ctx.PublishAsync(new TextDeltaEvent("has-trace", "msg-2") { TraceId = "override-trace-xxxxxxxxxxaaaaaa" });

        coordinator.Captured.Should().HaveCount(2);
        ((AgentEvent)coordinator.Captured[0]).TraceId.Should().Be(traceId);
        ((AgentEvent)coordinator.Captured[1]).TraceId.Should().Be("override-trace-xxxxxxxxxxaaaaaa");
    }

    // ── IEventFlowRegistry-aware coordinator ─────────────────────────────────────

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

        public void Emit(Event evt, EventRouteDescriptor? route) => Emit(evt);

        public ValueTask EmitAsync(Event evt, CancellationToken ct = default)
        {
            Emit(evt);
            return ValueTask.CompletedTask;
        }

        public ValueTask EmitAsync(Event evt, EventRouteDescriptor? route, CancellationToken ct = default) =>
            EmitAsync(evt, ct);

        public IDisposable Subscribe<TEvent>(Func<TEvent, ValueTask> handler, EventSubscriptionOptions? options = null) where TEvent : Event => _inner.Subscribe(handler, options);
        public IDisposable SubscribeAny(Func<Event, ValueTask> handler, EventSubscriptionOptions? options = null) => _inner.SubscribeAny(handler, options);
        public EventInbox<TEvent> CreateInbox<TEvent>(EventInboxOptions? options = null) where TEvent : Event => _inner.CreateInbox<TEvent>(options);
        public EventInbox<Event> CreateChannelInbox(EventChannel channel, EventInboxOptions? options = null) => _inner.CreateChannelInbox(channel, options);
        public void SetParent(IEventCoordinator parent) => _inner.SetParent(parent);
        public IEventCoordinator CreateChild(EventChildOwnership ownership) => _inner.CreateChild(ownership);
        public IDisposable ForwardTo(IEventCoordinator destination, EventForwardingOptions? options = null) =>
            _inner.ForwardTo(destination, options);
        public RequestHandle StartRequest<TRequest, TResponse>(TRequest request, RequestOptions? options = null)
            where TRequest : Event, IRequestEvent
            where TResponse : Event, IResponseEvent => _inner.StartRequest<TRequest, TResponse>(request, options);
        public RequestHandle StartRequest<TRequest, TResponse>(TRequest request, EventRouteDescriptor? route, RequestOptions? options = null)
            where TRequest : Event, IRequestEvent
            where TResponse : Event, IResponseEvent => _inner.StartRequest<TRequest, TResponse>(request, route, options);
        public RequestHandle RegisterRequest<TRequest, TResponse>(TRequest request, RequestOptions? options = null)
            where TRequest : Event, IRequestEvent
            where TResponse : Event, IResponseEvent => _inner.RegisterRequest<TRequest, TResponse>(request, options);
        public RequestHandle RegisterRequest<TRequest, TResponse>(TRequest request, EventRouteDescriptor? route, RequestOptions? options = null)
            where TRequest : Event, IRequestEvent
            where TResponse : Event, IResponseEvent => _inner.RegisterRequest<TRequest, TResponse>(request, route, options);
        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, TimeSpan timeout, CancellationToken ct = default)
            where TRequest : Event, IRequestEvent
            where TResponse : Event, IResponseEvent => _inner.RequestAsync<TRequest, TResponse>(request, timeout, ct);
        public RespondResult Respond(Event response) => _inner.Respond(response);
        public RespondResult Respond(string requestId, Event response) => _inner.Respond(requestId, response);
        public ValueTask<RespondResult> RespondAsync(string requestId, Event response, Func<Event, CancellationToken, ValueTask<Event>> beforeCompletion, CancellationToken cancellationToken = default)
            => _inner.RespondAsync(requestId, response, beforeCompletion, cancellationToken);
        public IEventFlowRegistry EventFlows => _inner.EventFlows;
        public IReadOnlyList<PendingRequestSnapshot> GetPendingRequests() => _inner.GetPendingRequests();
        public EventCoordinatorStats GetStats() => _inner.GetStats();
    }
}
