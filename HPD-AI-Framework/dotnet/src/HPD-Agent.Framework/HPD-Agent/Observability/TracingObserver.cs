// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace HPD.Agent;

/// <summary>
/// Converts the HPD-Agent event stream into OpenTelemetry Activity spans.
///
/// Each structural event pair (Started/Finished, Start/End) maps to one Activity:
///   MessageTurnStarted → MessageTurnFinished   = root span    ("agent.turn")
///   AgentTurnStarted   → AgentTurnFinished     = child span   ("agent.iteration")
///   ToolCallStart      → ToolCallEnd            = grandchild   ("agent.tool_call")
///
/// Non-structural events (deltas, snapshots, middleware diagnostics) are attached
/// as events on the currently-open Activity rather than becoming their own spans.
///
/// Usage:
///   var agent = new AgentBuilder()
///       .WithTracing()          // registers this observer
///       .Build();
///
/// The host application is responsible for wiring an OTLP exporter:
///   builder.Services.AddOpenTelemetry()
///       .WithTracing(t => t
///           .AddSource("HPD.Agent")
///           .AddOtlpExporter());
/// </summary>
public sealed class TracingObserver : IAgentEventObserver, IDisposable
{
    private readonly ActivitySource _source;

    // Open spans indexed by SpanId — allows ToolCallEnd to close the right span
    // even if tool calls overlap or arrive out of order.
    private readonly ConcurrentDictionary<string, Activity> _openSpans = new();

    // Per-trace: the root turn activity, looked up by TraceId so iteration
    // spans can set the correct parent even if events arrive concurrently.
    private readonly ConcurrentDictionary<string, Activity> _turnActivities = new();

    // Per-iteration: the iteration activity, looked up by iterSpanId so tool
    // call spans can parent themselves correctly.
    private readonly ConcurrentDictionary<string, Activity> _iterActivities = new();
    private readonly SpanPayloadSanitizer _sanitizer;

    public TracingObserver(string sourceName = "HPD.Agent", SpanSanitizerOptions? sanitizerOptions = null)
    {
        _source = new ActivitySource(sourceName);
        _sanitizer = new SpanPayloadSanitizer(sanitizerOptions);
    }

    // ── IAgentEventObserver ───────────────────────────────────────────────────

    public bool ShouldProcess(AgentEvent evt) => evt.TraceId is not null;

    public Task OnEventAsync(AgentEvent evt, CancellationToken cancellationToken)
    {
        switch (evt)
        {
            // ── Root span ─────────────────────────────────────────────────────

            case MessageTurnStartedEvent e:
                OnTurnStarted(e);
                break;

            case MessageTurnFinishedEvent e:
                OnTurnFinished(e);
                break;

            // ── Iteration span ────────────────────────────────────────────────

            case AgentTurnStartedEvent e:
                OnIterationStarted(e);
                break;

            case AgentTurnFinishedEvent e:
                OnIterationFinished(e);
                break;

            // ── Tool call span ────────────────────────────────────────────────

            case ToolCallStartEvent e:
                OnToolCallStarted(e);
                break;

            case ToolCallEndEvent e:
                OnToolCallEnded(e);
                break;

            case ToolCallResultEvent e:
                OnToolCallResult(e);
                break;

            // ── Events on the current iteration span ──────────────────────────

            case AgentDecisionEvent e:
                AddEventToIteration(e.TraceId!, "agent.decision",
                    ["decision_type", e.DecisionType, "iteration", e.Iteration.ToString()]);
                break;

            case PermissionRequestEvent e:
                AddEventToIteration(e.TraceId!, "permission.request",
                    ["permission_id", e.PermissionId, "function", e.FunctionName]);
                break;

            case PermissionApprovedEvent e:
                AddEventToIteration(e.TraceId!, "permission.approved",
                    ["permission_id", e.PermissionId]);
                break;

            case PermissionDeniedEvent e:
                AddEventToIteration(e.TraceId!, "permission.denied",
                    ["permission_id", e.PermissionId, "reason", e.Reason]);
                break;

            case CircuitBreakerTriggeredEvent e:
                AddEventToIteration(e.TraceId!, "circuit_breaker.triggered",
                    ["function", e.FunctionName, "consecutive_count", e.ConsecutiveCount.ToString()]);
                break;

            case FunctionRetryEvent e:
                AddEventToIteration(e.TraceId!, "tool.retry",
                    ["function", e.FunctionName, "attempt", e.Attempt.ToString()]);
                break;

            case ModelCallRetryEvent e:
                AddEventToIteration(e.TraceId!, "model.retry",
                    ["attempt", e.Attempt.ToString()]);
                break;

            case MessageTurnErrorEvent e:
                MarkTurnError(e);
                break;
        }

        return Task.CompletedTask;
    }

    // ── Span lifecycle ────────────────────────────────────────────────────────

    private void OnTurnStarted(MessageTurnStartedEvent e)
    {
        // Use W3C ActivityContext so downstream OTel exporters get the correct
        // traceId without us having to set it manually. ActivitySource.StartActivity
        // picks up the ambient context; we set the traceId as a tag for correlation.
        var activity = _source.StartActivity(
            "agent.turn",
            ActivityKind.Internal);

        if (activity is null) return;  // No listener — OTel not configured, skip.

        activity.SetTag("agent.name", e.AgentName);
        activity.SetTag("agent.conversation_id", e.ConversationId);
        activity.SetTag("agent.message_turn_id", e.MessageTurnId);
        activity.SetTag("hpd.trace_id", e.TraceId);  // our ID, for cross-referencing events

        _turnActivities[e.TraceId!] = activity;
        if (e.SpanId is not null)
            _openSpans[e.SpanId] = activity;
    }

    private void OnTurnFinished(MessageTurnFinishedEvent e)
    {
        if (e.TraceId is null) return;

        if (_turnActivities.TryRemove(e.TraceId, out var activity))
        {
            activity.SetTag("agent.turn_duration_ms", e.Duration.TotalMilliseconds);
            activity.Stop();
        }

        if (e.SpanId is not null)
            _openSpans.TryRemove(e.SpanId, out _);
    }

    private void OnIterationStarted(AgentTurnStartedEvent e)
    {
        if (e.TraceId is null || e.SpanId is null) return;

        // Parent the iteration under the turn activity.
        _turnActivities.TryGetValue(e.TraceId, out var parent);

        // Start as a child of the turn span when available, otherwise root.
        // Do NOT use `using var` — the activity must remain open until OnIterationFinished.
        var activity = parent is not null
            ? _source.StartActivity("agent.iteration", ActivityKind.Internal, parent.Context)
            : _source.StartActivity("agent.iteration", ActivityKind.Internal);

        if (activity is null) return;

        activity.SetTag("agent.iteration", e.Iteration);
        activity.SetTag("hpd.trace_id", e.TraceId);

        _iterActivities[e.SpanId] = activity;
        _openSpans[e.SpanId] = activity;
    }

    private void OnIterationFinished(AgentTurnFinishedEvent e)
    {
        if (e.SpanId is null) return;

        if (_iterActivities.TryRemove(e.SpanId, out var activity))
            activity.Stop();

        _openSpans.TryRemove(e.SpanId, out _);
    }

    private void OnToolCallStarted(ToolCallStartEvent e)
    {
        if (e.TraceId is null || e.SpanId is null) return;

        // Parent under the iteration span via ParentSpanId.
        Activity? parent = null;
        if (e.ParentSpanId is not null)
            _iterActivities.TryGetValue(e.ParentSpanId, out parent);

        Activity? activity;
        if (parent is not null)
        {
            activity = _source.StartActivity(
                "agent.tool_call",
                ActivityKind.Internal,
                parent.Context);
        }
        else
        {
            activity = _source.StartActivity("agent.tool_call", ActivityKind.Internal);
        }

        if (activity is null) return;

        activity.SetTag("tool.name", e.Name);
        activity.SetTag("tool.call_id", e.CallId);
        activity.SetTag("tool.toolkit", e.ToolkitName ?? "");
        activity.SetTag("hpd.trace_id", e.TraceId);

        // Key by CallId so ToolCallEndEvent (which has CallId, not SpanId) can close it.
        _openSpans[e.CallId] = activity;
    }

    private void OnToolCallEnded(ToolCallEndEvent e)
    {
        if (_openSpans.TryRemove(e.CallId, out var activity))
            activity.Stop();
    }

    private void OnToolCallResult(ToolCallResultEvent e)
    {
        // Attach result to the already-closed (or still-open) tool span via AddEvent.
        // Tool result arrives after ToolCallEnd so the span may already be stopped —
        // that's fine, ActivityEvent can still be recorded.
        if (_openSpans.TryGetValue(e.CallId, out var activity))
        {
            // Sanitize: redact sensitive JSON fields and cap length before shipping
            // the result payload to the tracing backend.
            var sanitizedResult = _sanitizer.Sanitize(e.Result);

            activity.AddEvent(new ActivityEvent("tool.result",
                tags: new ActivityTagsCollection
                {
                    ["tool.call_id"] = e.CallId,
                    ["tool.toolkit"] = e.ToolkitName ?? "",
                    ["tool.result"] = sanitizedResult,
                }));
        }
    }

    private void MarkTurnError(MessageTurnErrorEvent e)
    {
        if (e.TraceId is null) return;
        if (!_turnActivities.TryGetValue(e.TraceId, out var activity)) return;

        // Sanitize error message — exception messages can contain sensitive data
        // (e.g. connection strings, API responses with credentials).
        var sanitizedMessage = _sanitizer.Sanitize(e.Message);

        activity.SetStatus(ActivityStatusCode.Error, sanitizedMessage);
        activity.SetTag("error.message", sanitizedMessage);
        activity.SetTag("error.type", e.Exception?.GetType().Name ?? "");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the most-recently-opened iteration span for this trace and adds
    /// an ActivityEvent to it. Used for non-span events (decisions, permissions, retries).
    /// Tags are passed as alternating name/value pairs.
    /// </summary>
    private void AddEventToIteration(string traceId, string eventName, string[] tags)
    {
        // Find an open iteration span for this trace.
        var iterActivity = _iterActivities.Values
            .FirstOrDefault(a => a.GetTagItem("hpd.trace_id") as string == traceId);

        if (iterActivity is null) return;

        var tagCollection = new ActivityTagsCollection();
        for (int i = 0; i + 1 < tags.Length; i += 2)
            tagCollection[tags[i]] = tags[i + 1];

        iterActivity.AddEvent(new ActivityEvent(eventName, tags: tagCollection));
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        // Stop any spans that weren't cleanly closed (e.g. agent crashed mid-turn).
        foreach (var activity in _openSpans.Values)
        {
            activity.SetStatus(ActivityStatusCode.Error, "Turn ended without clean completion.");
            activity.Stop();
        }
        _openSpans.Clear();
        _turnActivities.Clear();
        _iterActivities.Clear();
        _source.Dispose();
    }
}
