// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

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
///   await using var agent = await new AgentBuilder()
///       .WithTracing()          // registers this observer
///       .BuildAsync();
///
/// The host application is responsible for wiring an OTLP exporter:
///   builder.Services.AddOpenTelemetry()
///       .WithTracing(t => t
///           .AddSource("HPD.Agent")
///           .AddOtlpExporter());
/// </summary>
public sealed class TracingObserver : IDisposable
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
    private readonly ConcurrentDictionary<string, Activity> _subAgentActivities = new();
    private readonly SpanPayloadSanitizer _sanitizer;

    public TracingObserver(string sourceName = "HPD.Agent", SpanSanitizerOptions? sanitizerOptions = null)
    {
        _source = new ActivitySource(sourceName);
        _sanitizer = new SpanPayloadSanitizer(sanitizerOptions);
    }

    public ValueTask HandleAsync(AgentEvent evt)
    {
        if (evt.TraceId is null)
            return ValueTask.CompletedTask;

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

            // ── Durable subagent span ─────────────────────────────────────────

            case SubAgentInvocationStartedEvent e:
                OnSubAgentStarted(e);
                break;

            case SubAgentInvocationCompletedEvent e:
                OnSubAgentFinished(e.InvocationId, e.Summary, "completed", null);
                break;

            case SubAgentInvocationFailedEvent e:
                OnSubAgentFinished(
                    e.InvocationId, e.Message, "failed", e.ErrorType);
                break;

            case SubAgentInvocationCancelledEvent e:
                OnSubAgentFinished(
                    e.InvocationId, e.Reason, "cancelled", "OperationCanceledException");
                break;

            // ── Events on the current iteration span ──────────────────────────

            case FunctionInvocationAuditedEvent e:
                OnFunctionInvocationAudited(e);
                break;

            case ProviderOperationUsageEvent e:
                AddProviderUsageEvent(e);
                break;

            case AgentDecisionEvent e:
                AddEventToIteration(e.TraceId!, "agent.decision",
                    ["decision_type", e.DecisionType, "iteration", e.Iteration.ToString()]);
                break;

            case PermissionRequestEvent e:
                AddEventToIteration(e.TraceId!, "permission.request",
                    ["permission_id", e.PermissionId, "authority", e.Authority ?? ""]);
                break;

            case PermissionResponseEvent e:
                AddEventToIteration(e.TraceId!, "permission.response",
                    [
                        "permission_id", e.PermissionId,
                        "choice_id", e.ChoiceId,
                        "feedback", e.Feedback ?? string.Empty
                    ]);
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

        return ValueTask.CompletedTask;
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

        activity.SetTag("agent.id", e.AgentId);
        activity.SetTag("agent.name", e.AgentName);
        activity.SetTag("agent.conversation_id", e.ConversationId);
        activity.SetTag("agent.message_turn_id", e.MessageTurnId);
        activity.SetTag("hpd.trace_id", e.TraceId);  // our ID, for cross-referencing events
        if (e.SpanId is not null)
            activity.SetTag("hpd.span_id", e.SpanId);
        if (e.SessionId is not null)
            activity.SetTag("hpd.session_id", e.SessionId);
        if (e.ThreadId is not null)
            activity.SetTag("hpd.thread_id", e.ThreadId);

        _turnActivities[e.TraceId!] = activity;
        if (e.SpanId is not null)
            _openSpans[e.SpanId] = activity;
    }

    private void OnTurnFinished(MessageTurnFinishedEvent e)
    {
        if (e.TraceId is null) return;

        foreach (var subAgent in _subAgentActivities
                     .Where(value =>
                         StringComparer.Ordinal.Equals(
                             value.Value.GetTagItem("hpd.trace_id") as string, e.TraceId))
                     .ToArray())
        {
            OnSubAgentFinished(
                subAgent.Key, "Parent turn finished before subagent terminal evidence.",
                "cancelled", "ParentTurnFinished");
        }

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
        activity.SetTag("hpd.span_id", e.SpanId);
        if (e.ParentSpanId is not null)
            activity.SetTag("hpd.parent_span_id", e.ParentSpanId);
        if (e.SessionId is not null)
            activity.SetTag("hpd.session_id", e.SessionId);
        if (e.ThreadId is not null)
            activity.SetTag("hpd.thread_id", e.ThreadId);

        _iterActivities[e.SpanId] = activity;
        _openSpans[e.SpanId] = activity;
    }

    private void OnIterationFinished(AgentTurnFinishedEvent e)
    {
        if (e.SpanId is null) return;

        if (_iterActivities.TryRemove(e.SpanId, out var activity))
        {
            if (e.ProviderKey is { Length: > 0 } provider)
                activity.SetTag("model.provider", provider);
            if (e.ModelId is { Length: > 0 } model)
                activity.SetTag("model.id", model);
            activity.SetTag("model.operation_id", e.OperationId);
            activity.SetTag("model.attempt", e.Attempt);
            activity.SetTag("model.outcome", e.Outcome.ToString());
            if (e.Usage is { } usage)
            {
                if (usage.InputTokenCount is { } input)
                    activity.SetTag("model.usage.input_tokens", input);
                if (usage.OutputTokenCount is { } output)
                    activity.SetTag("model.usage.output_tokens", output);
                if (usage.TotalTokenCount is { } total)
                    activity.SetTag("model.usage.total_tokens", total);
            }
            activity.SetStatus(e.Outcome == ProviderOperationOutcome.Succeeded
                ? ActivityStatusCode.Ok
                : ActivityStatusCode.Error);
            activity.Stop();
        }

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
        activity.SetTag("function.name", e.Name);
        activity.SetTag("tool.call_id", e.CallId);
        activity.SetTag("tool.toolharness", e.ToolHarnessName ?? "");
        if (e.ToolHarnessName is { Length: > 0 } harness)
            activity.SetTag("function.toolharness", harness);
        if (e.CallType is { } callType)
            activity.SetTag("tool.call_type", callType.ToString());
        activity.SetTag("hpd.trace_id", e.TraceId);
        activity.SetTag("hpd.span_id", e.SpanId);
        if (e.ParentSpanId is not null)
            activity.SetTag("hpd.parent_span_id", e.ParentSpanId);
        if (e.SessionId is not null)
            activity.SetTag("hpd.session_id", e.SessionId);
        if (e.ThreadId is not null)
            activity.SetTag("hpd.thread_id", e.ThreadId);

        // Key by CallId so ToolCallEndEvent (which has CallId, not SpanId) can close it.
        _openSpans[e.CallId] = activity;
    }

    private void OnFunctionInvocationAudited(FunctionInvocationAuditedEvent e)
    {
        if (!_openSpans.TryGetValue(e.Invocation.FunctionCallId, out Activity? activity))
            return;
        if (e.Invocation.Action is { Length: > 0 } action)
            activity.SetTag("function.action", action);
        activity.SetTag("function.invocation_mode", e.Invocation.ResolvedMode.ToString());
        if (e.Invocation.RequestedMode is { } requestedMode)
            activity.SetTag("function.requested_mode", requestedMode.ToString());
        activity.SetTag("function.invocation_policy", e.Invocation.Policy.ToString());
        activity.SetTag("function.invocation_handling", e.Invocation.Handling.ToString());
        activity.SetTag("function.argument_provenance", e.Invocation.IngressProvenance.ToString());
        activity.AddEvent(new ActivityEvent("function.invocation.audited"));
    }

    private void OnSubAgentStarted(SubAgentInvocationStartedEvent e)
    {
        if (e.TraceId is null) return;
        Activity? parent = null;
        if (e.ParentToolCallId.Length > 0)
            _openSpans.TryGetValue(e.ParentToolCallId, out parent);
        parent ??= _turnActivities.GetValueOrDefault(e.TraceId);

        Activity? activity = parent is null
            ? _source.StartActivity("agent.subagent", ActivityKind.Internal)
            : _source.StartActivity("agent.subagent", ActivityKind.Internal, parent.Context);
        if (activity is null) return;

        string spanId = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        activity.SetTag("hpd.trace_id", e.TraceId);
        activity.SetTag("hpd.span_id", spanId);
        if (parent?.GetTagItem("hpd.span_id") is string parentSpanId)
            activity.SetTag("hpd.parent_span_id", parentSpanId);
        if (e.SessionId is not null)
            activity.SetTag("hpd.session_id", e.SessionId);
        if (e.ThreadId is not null)
            activity.SetTag("hpd.thread_id", e.ThreadId);
        activity.SetTag("subagent.invocation_id", e.InvocationId);
        activity.SetTag("subagent.parent_tool_call_id", e.ParentToolCallId);
        activity.SetTag("subagent.agent_id", e.ChildAgentId);
        activity.SetTag("subagent.session_id", e.ChildSessionId);
        activity.SetTag("subagent.thread_id", e.ChildThreadId);
        activity.SetTag("subagent.role", e.RoleName);
        activity.SetTag("subagent.context_policy", e.ContextPolicy.ToString());
        activity.SetTag("subagent.invocation_mode", e.Mode.ToString());
        _subAgentActivities[e.InvocationId] = activity;
    }

    private void OnSubAgentFinished(
        string invocationId, string? summary, string status, string? errorType)
    {
        if (!_subAgentActivities.TryRemove(invocationId, out Activity? activity))
            return;
        activity.SetTag("subagent.status", status);
        if (summary is { Length: > 0 })
            activity.SetTag("subagent.summary", _sanitizer.Sanitize(summary));
        if (errorType is { Length: > 0 })
            activity.SetTag("subagent.error_type", errorType);
        activity.SetStatus(status == "completed"
            ? ActivityStatusCode.Ok
            : ActivityStatusCode.Error);
        activity.Stop();
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
            var sanitizedResult = _sanitizer.Sanitize(FormatToolResult(e.Result));

            activity.AddEvent(new ActivityEvent("tool.result",
                tags: new ActivityTagsCollection
                {
                    ["tool.call_id"] = e.CallId,
                    ["tool.toolharness"] = e.ToolHarnessName ?? "",
                    ["tool.result"] = sanitizedResult,
                }));
        }
    }

    private void AddProviderUsageEvent(ProviderOperationUsageEvent e)
    {
        if (e.TraceId is null)
            return;
        Activity? activity = null;
        if (e.SpanId is { Length: > 0 })
            _iterActivities.TryGetValue(e.SpanId, out activity);
        activity ??= _turnActivities.GetValueOrDefault(e.TraceId);
        if (activity is null) return;
        activity.AddEvent(new ActivityEvent("provider.operation.usage", tags: new ActivityTagsCollection
        {
            ["provider.operation.kind"] = e.OperationKind.ToString(),
            ["provider.operation.outcome"] = e.Outcome.ToString(),
            ["provider.operation.attempt"] = e.Attempt,
            ["provider.operation.id"] = e.OperationId,
            ["model.provider"] = e.ProviderKey ?? "",
            ["model.id"] = e.ModelId ?? "",
            ["model.usage.input_tokens"] = e.Usage?.InputTokenCount ?? 0,
            ["model.usage.output_tokens"] = e.Usage?.OutputTokenCount ?? 0,
            ["model.usage.total_tokens"] = e.Usage?.TotalTokenCount ?? 0
        }));
    }

    private static string FormatToolResult(ToolResultPayload result) =>
        result.Text ?? result.Json?.GetRawText() ?? string.Empty;

    private void MarkTurnError(MessageTurnErrorEvent e)
    {
        if (e.TraceId is null) return;
        if (!_turnActivities.TryGetValue(e.TraceId, out var activity)) return;

        // Sanitize error message — exception messages can contain sensitive data
        // (e.g. connection strings, API responses with credentials).
        var sanitizedMessage = _sanitizer.Sanitize(e.ErrorMessage);

        activity.SetStatus(ActivityStatusCode.Error, sanitizedMessage);
        activity.SetTag("error.message", sanitizedMessage);
        activity.SetTag("error.type", (e as IErrorEvent)?.Exception?.GetType().Name ?? "");
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
        foreach (var activity in _subAgentActivities.Values)
        {
            activity.SetStatus(ActivityStatusCode.Error, "Observer disposed before clean completion.");
            activity.Stop();
        }
        _subAgentActivities.Clear();
        _turnActivities.Clear();
        _iterActivities.Clear();
        _source.Dispose();
    }
}
