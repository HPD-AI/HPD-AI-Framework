// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Diagnostics;
using System.Security.Claims;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Agent.Studio.Tests;

public sealed class AgentStudioTelemetryTests
{
    [Fact]
    public async Task Collector_maps_correlated_activities_into_the_studio_store()
    {
        string sourceName = $"HPD.Agent.Studio.Tests.{Guid.NewGuid():N}";
        var options = Options.Create(new AgentStudioTelemetryOptions());
        options.Value.ActivitySources.Add(sourceName);
        var store = new InMemoryAgentStudioTelemetryStore(options);
        using var collector = new AgentStudioActivityTelemetryCollector(store, options);
        await collector.StartAsync(CancellationToken.None);
        using var source = new ActivitySource(sourceName);

        using (Activity? activity = source.StartActivity("agent.turn"))
        {
            Assert.NotNull(activity);
            activity.SetTag("hpd.trace_id", "0123456789abcdef0123456789abcdef");
            activity.SetTag("hpd.span_id", "0123456789abcdef");
            activity.SetTag("hpd.session_id", "session");
            activity.SetTag("hpd.thread_id", "thread");
            activity.AddEvent(new ActivityEvent("agent.decision"));
        }

        Assert.True(store.TryGetTrace("0123456789abcdef0123456789abcdef", out AgentStudioTelemetryTrace trace));
        AgentStudioTelemetrySpan span = Assert.Single(trace.Spans);
        Assert.Equal("0123456789abcdef", span.SpanId);
        Assert.Equal("agent.turn", span.Name);
        Assert.Equal("agent.decision", Assert.Single(span.Events).Name);
        Assert.Equal("session", span.Attributes["hpd.session_id"]);
        Assert.Equal("thread", span.Attributes["hpd.thread_id"]);
    }

    [Fact]
    public void File_store_reloads_only_the_bounded_retained_projection()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"hpd-studio-telemetry-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "spans.jsonl");
        try
        {
            var options = Options.Create(new AgentStudioTelemetryOptions
            {
                MaximumRetainedSpans = 2,
                SpanStorePath = path
            });
            var first = new FileAgentStudioTelemetryStore(options);
            for (int index = 0; index < 70; index++)
            {
                first.Record(Span(
                    $"trace-{index:D3}",
                    $"span-{index:D3}",
                    DateTimeOffset.UtcNow.AddSeconds(index - 70)));
            }

            var reloaded = new FileAgentStudioTelemetryStore(options);

            Assert.False(reloaded.TryGetTrace("trace-000", out _));
            Assert.True(reloaded.TryGetTrace("trace-068", out _));
            Assert.True(reloaded.TryGetTrace("trace-069", out AgentStudioTelemetryTrace latest));
            Assert.Equal("span-069", Assert.Single(latest.Spans).SpanId);
            Assert.True(File.ReadLines(path).Count() < 20, "compaction should bound the durable file");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Tracing_observer_emits_model_function_action_and_subagent_spans()
    {
        string sourceName = $"HPD.Agent.Studio.FeatureTests.{Guid.NewGuid():N}";
        var options = Options.Create(new AgentStudioTelemetryOptions());
        options.Value.ActivitySources.Add(sourceName);
        var store = new InMemoryAgentStudioTelemetryStore(options);
        using var collector = new AgentStudioActivityTelemetryCollector(store, options);
        await collector.StartAsync(CancellationToken.None);
        using var observer = new TracingObserver(sourceName);

        const string traceId = "33333333333333333333333333333333";
        const string rootSpan = "aaaaaaaaaaaaaaaa";
        const string iterationSpan = "bbbbbbbbbbbbbbbb";
        const string toolSpan = "cccccccccccccccc";
        const string messageTurnId = "message-turn";
        const string callId = "call-1";
        await observer.HandleAsync(new MessageTurnStartedEvent(
            messageTurnId, "session", "agent", "Agent")
        {
            TraceId = traceId,
            SpanId = rootSpan,
            SessionId = "session",
            ThreadId = "thread"
        });
        await observer.HandleAsync(new AgentTurnStartedEvent(0)
        {
            TraceId = traceId,
            SpanId = iterationSpan,
            ParentSpanId = rootSpan,
            SessionId = "session",
            ThreadId = "thread"
        });
        await observer.HandleAsync(new ToolCallStartEvent(
            callId, "searchKnowledge", "assistant", "knowledge-harness")
        {
            TraceId = traceId,
            SpanId = toolSpan,
            ParentSpanId = iterationSpan,
            SessionId = "session",
            ThreadId = "thread"
        });
        await observer.HandleAsync(new FunctionInvocationAuditedEvent
        {
            TraceId = traceId,
            SpanId = toolSpan,
            Invocation = new FunctionInvocationAuditProjection
            {
                FunctionName = "searchKnowledge",
                FunctionCallId = callId,
                Action = "search",
                ResolvedMode = AgentInvocationMode.Synchronous,
                Policy = AgentInvocationModePolicy.SynchronousOnly,
                Handling = AgentInvocationModeHandling.Runtime,
                IngressProvenance = FunctionArgumentIngressProvenance.Original
            }
        });
        await observer.HandleAsync(new SubAgentInvocationStartedEvent(
            "sub-invocation", callId, "research-agent", "session", "child-thread",
            "researcher", SubAgentContextPolicy.Fresh, AgentInvocationMode.Synchronous)
        {
            TraceId = traceId,
            SessionId = "session",
            ThreadId = "thread"
        });
        await observer.HandleAsync(new SubAgentInvocationCompletedEvent(
            "sub-invocation", "Child report") { TraceId = traceId });
        await observer.HandleAsync(new ToolCallEndEvent(
            callId, "assistant", "searchKnowledge", "{}") { TraceId = traceId });
        await observer.HandleAsync(new ProviderOperationUsageEvent(
            messageTurnId, "operation", null, 1, ProviderOperationKind.ChatModelResponse,
            ProviderClientFamily.Chat, ProviderOperationOutcome.Succeeded,
            new UsageDetails { InputTokenCount = 7, OutputTokenCount = 5, TotalTokenCount = 12 },
            "demo-provider", "demo-model", "response") { TraceId = traceId });
        await observer.HandleAsync(new AgentTurnFinishedEvent(
            messageTurnId, 0, "operation", null, 1, ProviderClientFamily.Chat,
            ProviderOperationOutcome.Succeeded,
            new UsageDetails { InputTokenCount = 7, OutputTokenCount = 5, TotalTokenCount = 12 },
            "demo-provider", "demo-model", "response")
        {
            TraceId = traceId,
            SpanId = iterationSpan,
            ParentSpanId = rootSpan,
            SessionId = "session",
            ThreadId = "thread"
        });
        await observer.HandleAsync(new MessageTurnFinishedEvent(
            messageTurnId, "session", "agent", "Agent", TimeSpan.FromMilliseconds(20),
            MessageTurnUsageSummary.Empty)
        {
            TraceId = traceId,
            SpanId = rootSpan,
            SessionId = "session",
            ThreadId = "thread"
        });

        Assert.True(store.TryGetTrace(traceId, out AgentStudioTelemetryTrace trace));
        AgentStudioTelemetrySpan tool = Assert.Single(trace.Spans, span => span.Name == "agent.tool_call");
        Assert.Equal("search", tool.Attributes["function.action"]);
        Assert.Equal("Synchronous", tool.Attributes["function.invocation_mode"]);
        AgentStudioTelemetrySpan model = Assert.Single(trace.Spans, span => span.Name == "agent.iteration");
        Assert.Equal("demo-provider", model.Attributes["model.provider"]);
        Assert.Equal("demo-model", model.Attributes["model.id"]);
        Assert.Equal("12", model.Attributes["model.usage.total_tokens"]);
        AgentStudioTelemetrySpan subagent = Assert.Single(trace.Spans, span => span.Name == "agent.subagent");
        Assert.Equal("child-thread", subagent.Attributes["subagent.thread_id"]);
        Assert.Equal(toolSpan, subagent.Attributes["hpd.parent_span_id"]);
        Assert.Equal("completed", subagent.Status);
        AgentStudioTelemetrySpan turn = Assert.Single(trace.Spans, span => span.Name == "agent.turn");
        Assert.Contains(turn.Events, spanEvent => spanEvent.Name == "provider.operation.usage");
    }

    [Fact]
    public async Task Default_event_payload_authority_is_fail_closed()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "operator")], "test"));
        var eventValue = new MessageTurnStartedEvent(
            "message", "session", "agent", "Agent");
        var denied = new DefaultAgentStudioEventPayloadAuthority(
            Options.Create(new AgentStudioPayloadOptions()));
        Assert.False(await denied.CanInspectAsync(
            principal, eventValue, CancellationToken.None));

        var enabled = new DefaultAgentStudioEventPayloadAuthority(
            Options.Create(new AgentStudioPayloadOptions { Enabled = true }));
        Assert.True(await enabled.CanInspectAsync(
            principal, eventValue, CancellationToken.None));
    }

    private static AgentStudioTelemetrySpan Span(string traceId, string spanId, DateTimeOffset startedAt) =>
        new(
            traceId,
            spanId,
            null,
            traceId,
            spanId,
            "otel.agent.turn",
            "Internal",
            "Unset",
            startedAt,
            10,
            "HPD.Agent",
            new Dictionary<string, string>(StringComparer.Ordinal),
            []);
}
