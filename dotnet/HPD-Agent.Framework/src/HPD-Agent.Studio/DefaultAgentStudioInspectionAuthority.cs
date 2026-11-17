using System.Buffers;
using System.Security.Claims;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using HPD.Agent;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Permissions;
using HPD.Agent.Serialization;
using HPD.Agent.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Studio;

/// <summary>Reads current Agent hosting authority without introducing a second Agent, session, or thread store.</summary>
public sealed class DefaultAgentStudioInspectionAuthority(IServiceProvider services) : IAgentStudioInspectionAuthority
{
    private const int MaximumBytes = 8_388_608;

    public async ValueTask<StudioFrameworkSurfaceResponse?> ObserveAsync(
        string operationId, string relativePath, string query, string applicationId,
        ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] segments = relativePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToArray();
        return operationId switch
        {
            "agent.list" when segments is ["agents"] => await AgentsAsync(applicationId, cancellationToken).ConfigureAwait(false),
            "agent.get" when segments is ["agents", var agentId] => await AgentAsync(applicationId, agentId, cancellationToken).ConfigureAwait(false),
            "agent.sessions.list" when segments is ["agents", var agentId, "sessions"] =>
                await AgentSessionsAsync(applicationId, agentId, cancellationToken).ConfigureAwait(false),
            "agent.thread.activity" when segments is ["agents", var agentId, "sessions", var sessionId, "threads", var threadId, "activity"] =>
                await ThreadActivityAsync(agentId, sessionId, threadId, cancellationToken).ConfigureAwait(false),
            "agent.configuration.get" when segments is ["agents", var agentId, "configuration"] =>
                await ConfigurationAsync(applicationId, agentId, cancellationToken).ConfigureAwait(false),
            "session.list" when segments is ["sessions"] => await SessionsAsync(applicationId, cancellationToken).ConfigureAwait(false),
            "session.get" when segments is ["sessions", var sessionId] => await SessionAsync(applicationId, sessionId, cancellationToken).ConfigureAwait(false),
            "thread.list" when segments is ["sessions", var sessionId, "threads"] =>
                await ThreadsAsync(applicationId, sessionId, cancellationToken).ConfigureAwait(false),
            "thread.get" when segments is ["sessions", var sessionId, "threads", var threadId] =>
                await ThreadAsync(applicationId, sessionId, threadId, cancellationToken).ConfigureAwait(false),
            "execution.list" when segments is ["agents", var agentId, "sessions", var sessionId, "threads", var threadId, "executions"] =>
                await ExecutionsAsync(applicationId, agentId, sessionId, threadId, cancellationToken).ConfigureAwait(false),
            "execution.get" when segments is ["agents", var agentId, "sessions", var sessionId, "threads", var threadId, "executions", var executionId] =>
                await ExecutionAsync(applicationId, agentId, sessionId, threadId, executionId, cancellationToken).ConfigureAwait(false),
            "harness.list" when segments is ["harnesses"] => await HarnessesAsync(applicationId, null, cancellationToken).ConfigureAwait(false),
            "harness.get" when segments is ["harnesses", var harnessId] => await HarnessesAsync(applicationId, harnessId, cancellationToken).ConfigureAwait(false),
            "function.get" when segments is ["harnesses", var harnessId, "functions", var functionId] =>
                await FunctionAsync(applicationId, harnessId, functionId, cancellationToken).ConfigureAwait(false),
            "function.action.get" when segments is ["harnesses", var harnessId, "functions", var functionId, "actions", var actionId] =>
                await FunctionActionAsync(applicationId, harnessId, functionId, actionId, cancellationToken).ConfigureAwait(false),
            "observability.summary" when segments is ["observability", "summary"] =>
                await ObservabilityAsync(applicationId, cancellationToken).ConfigureAwait(false),
            "metrics.query" when segments is ["observability", "metrics"] =>
                await MetricsAsync(ParseMetricsFilter(query), cancellationToken).ConfigureAwait(false),
            "events.list" when segments is ["events"] =>
                await EventsAsync(ParseEventFilter(query), cancellationToken).ConfigureAwait(false),
            "event.get" when segments is ["events", var eventId] =>
                await EventPayloadAsync(eventId, principal, cancellationToken).ConfigureAwait(false),
            "trace.list" when segments is ["traces"] =>
                await TracesAsync(applicationId, null, ParseTraceFilter(query), cancellationToken).ConfigureAwait(false),
            "trace.get" when segments is ["traces", var traceId] =>
                await TracesAsync(applicationId, traceId, null, cancellationToken).ConfigureAwait(false),
            _ => null,
        };
    }

    public ValueTask<StudioFrameworkSurfaceResponse?> PreflightAsync(
        string operationId, string relativePath, ReadOnlyMemory<byte> requestBody,
        string applicationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] segments = relativePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToArray();
        if (operationId != "agent.configuration.preflight" || segments is not ["agents", _, "configuration:preflight"])
            return ValueTask.FromResult<StudioFrameworkSurfaceResponse?>(null);
        var errors = new List<string>();
        AgentConfig? config = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(requestBody);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.EnumerateObject().Select(static value => value.Name).SequenceEqual(["config"]))
                errors.Add("request: an exact config object is required.");
            else
            {
                config = HpdAgentConfigSerializer.Deserialize(root.GetProperty("config").GetRawText());
                if (config is null) errors.Add("config: the AgentConfig document is null.");
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            errors.Add("config: strict AgentConfig binding failed.");
        }
        if (config is not null)
        {
            HPDAgentHostOptions? host = services.GetService<IOptions<HPDAgentHostOptions>>()?.Value;
            errors.AddRange(AgentConfigValidator.Validate(config, host?.ValidationLimits));
        }
        bool valid = config is not null && errors.Count == 0;
        string? normalized = valid ? HpdAgentConfigSerializer.Serialize(config!) : null;
        return ValueTask.FromResult<StudioFrameworkSurfaceResponse?>(Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", valid ? "valid" : "invalid");
            writer.WriteNumber("toolHarnessCount", config?.ToolHarnesses.Count ?? 0);
            writer.WriteStartArray("checks");
            WriteCheck(writer, "strict-binding", "Strict configuration binding", config is null ? "fail" : "pass",
                config is null ? "The submitted JSON could not be bound to AgentConfig." : "The submitted document bound through the canonical AgentConfig serializer.");
            WriteCheck(writer, "agent-config", "AgentConfig validation", errors.Count == 0 ? "pass" : "fail",
                errors.Count == 0 ? "Canonical AgentConfig validation passed." : $"{errors.Count} validation error(s) require correction.");
            WriteCheck(writer, "toolharnesses", "ToolHarness declarations",
                config?.ToolHarnesses.All(static item => !string.IsNullOrWhiteSpace(item.Name)) == true ? "pass" : "fail",
                config is null ? "No configuration was bound." : $"{config.ToolHarnesses.Count} ToolHarness reference(s) were checked.");
            WriteCheck(writer, "provider-composition", "Provider composition",
                config?.Clients is null ? "unavailable" : "pass",
                config?.Clients is null ? "No provider client declaration is present." : "Portable provider declarations bound successfully.");
            WriteCheck(writer, "run-overrides", "Portable run override eligibility",
                valid ? "pass" : "warn", "Run overrides remain invocation-scoped and are not persisted by preflight.");
            writer.WriteEndArray();
            writer.WriteStartArray("errors");
            foreach (string error in errors.Distinct(StringComparer.Ordinal).Take(128)) writer.WriteStringValue(error);
            writer.WriteEndArray();
            if (normalized is not null)
            {
                writer.WritePropertyName("normalizedConfig");
                writer.WriteRawValue(normalized, skipInputValidation: false);
            }
            writer.WriteEndObject();
        }));
    }

    public async ValueTask<bool> ExistsAsync(StudioResourceIdentity resource, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return resource switch
            {
                StudioAgentResource agent => await AgentExistsAsync(agent.AgentId, cancellationToken).ConfigureAwait(false),
                StudioAgentSessionResource session => await SessionExistsAsync(session.SessionId, cancellationToken).ConfigureAwait(false),
                StudioAgentThreadResource thread => await ThreadExistsAsync(thread.SessionId, thread.ThreadId, cancellationToken).ConfigureAwait(false),
                StudioAgentExecutionResource execution => await ExecutionExistsAsync(execution, cancellationToken).ConfigureAwait(false),
                StudioToolHarnessResource harness => await HarnessExistsAsync(harness.HarnessId, cancellationToken).ConfigureAwait(false),
                StudioAIFunctionResource function => await FunctionExistsAsync(function, cancellationToken).ConfigureAwait(false),
                StudioAIFunctionActionResource action => await FunctionActionExistsAsync(action, cancellationToken).ConfigureAwait(false),
                StudioAgentTraceResource trace => await TraceExistsAsync(trace.TraceId, cancellationToken).ConfigureAwait(false),
                _ => false,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> AgentsAsync(string applicationId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentDefinitionService>() is not { } agents) return null;
        IReadOnlyList<AgentSummaryDto> values = await agents.ListAgentsAsync(cancellationToken).ConfigureAwait(false);
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteStartArray("items");
            foreach (AgentSummaryDto value in values.OrderBy(static item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", value.Id);
                writer.WriteString("name", value.Name);
                writer.WriteString("createdAt", value.CreatedAt);
                writer.WriteString("updatedAt", value.UpdatedAt);
                writer.WriteString("resourceToken", StudioResourceRouteToken.Encode(new StudioAgentResource(applicationId, value.Id)));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> AgentAsync(
        string applicationId, string agentId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentDefinitionService>() is not { } agents) return null;
        StoredAgentDto? value = await agents.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (value is null) return null;
        string config = HpdAgentConfigSerializer.Serialize(value.Config);
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteString("id", value.Id);
            writer.WriteString("name", value.Name);
            writer.WriteString("createdAt", value.CreatedAt);
            writer.WriteString("updatedAt", value.UpdatedAt);
            writer.WriteString("resourceToken", StudioResourceRouteToken.Encode(new StudioAgentResource(applicationId, value.Id)));
            writer.WritePropertyName("config");
            writer.WriteRawValue(config, skipInputValidation: false);
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> SessionsAsync(
        string applicationId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentSessionService>() is not { } sessions) return null;
        IReadOnlyList<SessionDto> values = await sessions.SearchSessionsAsync(null, cancellationToken).ConfigureAwait(false);
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteStartArray("items");
            foreach (SessionDto value in values.OrderByDescending(static item => item.LastActivity).ThenBy(static item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", value.Id);
                writer.WriteString("createdAt", value.CreatedAt);
                writer.WriteString("lastActivity", value.LastActivity);
                writer.WriteString("resourceToken", StudioResourceRouteToken.Encode(new StudioAgentSessionResource(applicationId, value.Id)));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> AgentSessionsAsync(
        string applicationId, string agentId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentSessionService>() is not { } sessions ||
            services.GetService<IAgentThreadService>() is not { } threads)
            return null;
        IReadOnlyList<SessionDto> values = await sessions.SearchSessionsAsync(null, cancellationToken).ConfigureAwait(false);
        var scoped = new List<(SessionDto Session, ThreadDto[] Threads)>();
        foreach (SessionDto session in values)
        {
            AgentServiceResult<IReadOnlyList<ThreadDto>> result =
                await threads.ListThreadsAsync(session.Id, cancellationToken).ConfigureAwait(false);
            if (result.Status != AgentServiceStatus.Success || result.Value is null) continue;
            ThreadDto[] owned = result.Value
                .Where(thread => StringComparer.Ordinal.Equals(thread.DefaultAgentId, agentId))
                .OrderBy(thread => thread.CreatedAt)
                .ThenBy(thread => thread.Id, StringComparer.Ordinal)
                .ToArray();
            if (owned.Length > 0) scoped.Add((session, owned));
        }
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteString("agentId", agentId);
            writer.WriteStartArray("items");
            foreach ((SessionDto session, ThreadDto[] owned) in scoped
                         .OrderByDescending(value => value.Session.LastActivity)
                         .ThenBy(value => value.Session.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", session.Id);
                writer.WriteString("createdAt", session.CreatedAt);
                writer.WriteString("lastActivity", session.LastActivity);
                writer.WriteNumber("threadCount", owned.Length);
                writer.WriteString("resourceToken",
                    StudioResourceRouteToken.Encode(new StudioAgentSessionResource(applicationId, session.Id)));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> ThreadActivityAsync(
        string agentId, string sessionId, string threadId, CancellationToken cancellationToken)
    {
        if (services.GetService<SessionManager>() is not { Store: var store } ||
            services.GetService<IAgentThreadService>() is not { } threadService ||
            services.GetService<IAgentThreadExecutionService>() is not { } executionService)
            return null;

        AgentServiceResult<ThreadDto> descriptor =
            await threadService.GetThreadAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false);
        if (descriptor.Status != AgentServiceStatus.Success || descriptor.Value is null)
            return null;
        IReadOnlyList<AgentEvent>? events =
            await store.CollectThreadEventsAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false);
        if (events is null) return null;
        bool owned = StringComparer.Ordinal.Equals(descriptor.Value.DefaultAgentId, agentId);
        if (!owned && descriptor.Value.ForkedFrom is { } sourceThreadId)
        {
            AgentServiceResult<ThreadDto> source =
                await threadService.GetThreadAsync(sessionId, sourceThreadId, cancellationToken).ConfigureAwait(false);
            owned = source.Status == AgentServiceStatus.Success && source.Value is not null &&
                StringComparer.Ordinal.Equals(source.Value.DefaultAgentId, agentId);
        }
        if (!owned)
        {
            owned = events.OfType<ThreadCreatedEvent>().Any(value =>
                    StringComparer.Ordinal.Equals(value.DefaultAgentId, agentId)) ||
                events.OfType<ThreadExecutionStartedEvent>().Any(value =>
                    StringComparer.Ordinal.Equals(value.AgentId, agentId));
        }
        if (!owned) return null;
        ThreadEventHead? head = await store.GetThreadEventHeadAsync(
            new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false);
        if (events is null || head is null)
            return null;
        HPD.Agent.Thread? projection;
        try
        {
            projection = await store.ProjectThreadAsync(
                sessionId, threadId, ThreadProjectionPurpose.ThreadHistory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            projection = null;
        }
        AgentServiceResult<ThreadRuntimeStateDto> state =
            await services.GetRequiredService<IAgentStreamingService>()
                .GetThreadStateAsync(agentId, sessionId, threadId, cancellationToken).ConfigureAwait(false);
        AgentServiceResult<IReadOnlyList<ThreadExecutionDto>> executionResult =
            await executionService.ListExecutionsAsync(agentId, sessionId, threadId, cancellationToken)
                .ConfigureAwait(false);
        ThreadExecutionDto? activeExecution = state.Status == AgentServiceStatus.Success
            ? state.Value?.ActiveExecution
            : executionResult.Status == AgentServiceStatus.Success
                ? executionResult.Value?.FirstOrDefault(static value =>
                    StringComparer.Ordinal.Equals(value.Status, ThreadExecutionStatus.Active))
                : null;
        ThreadJournalCursor cursor = state.Status == AgentServiceStatus.Success && state.Value is not null
            ? state.Value.ObservedCursor
            : head.Cursor;
        int pendingRequestCount = state.Status == AgentServiceStatus.Success
            ? state.Value?.PendingRequests.Count ?? 0
            : 0;

        AgentEvent[] recentEvents = events
            .OrderBy(static value => value.ThreadSequenceNumber)
            .TakeLast(128)
            .ToArray();
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteString("agentId", agentId);
            writer.WriteString("sessionId", sessionId);
            writer.WriteString("threadId", threadId);
            writer.WriteNumber("generation", cursor.Generation);
            writer.WriteNumber("sequenceNumber", cursor.SequenceNumber);
            writer.WriteString("lastActivity", descriptor.Value.LastActivity);
            writer.WriteStartArray("messages");
            foreach (Microsoft.Extensions.AI.ChatMessage message in
                     (projection?.Messages ?? []).TakeLast(256))
            {
                string text = string.Concat(message.Contents
                    .OfType<Microsoft.Extensions.AI.TextContent>()
                    .Select(static content => content.Text));
                writer.WriteStartObject();
                writer.WriteString("id", message.MessageId ?? "");
                writer.WriteString("role", message.Role.Value);
                writer.WriteString("authorName", message.AuthorName ?? "");
                writer.WritePropertyName("createdAt");
                if (message.CreatedAt is { } createdAt) writer.WriteStringValue(createdAt);
                else writer.WriteNullValue();
                writer.WriteString("text", text.Length <= 32_768 ? text : text[..32_768]);
                writer.WriteNumber("contentCount", message.Contents.Count);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("events");
            foreach (AgentEvent item in recentEvents)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.EventId);
                writer.WriteString("type", item.GetType().Name);
                writer.WriteString("kind", item.Kind.ToString());
                writer.WriteString("channel", item.Channel.ToString());
                writer.WriteNumber("sequenceNumber", item.ThreadSequenceNumber);
                writer.WriteString("occurredAt", item.Timestamp);
                writer.WriteString("threadExecutionId", item.ThreadExecutionId ?? "");
                writer.WriteString("traceId", item.TraceId ?? "");
                writer.WriteString("spanId", item.SpanId ?? "");
                writer.WriteString("parentSpanId", item.ParentSpanId ?? "");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("pendingRequests");
            if (state.Status == AgentServiceStatus.Success && state.Value is not null)
                foreach (PendingAgentRequestDto pending in state.Value.PendingRequests)
                    WritePendingRequest(writer, pending.Request);
            writer.WriteEndArray();
            writer.WritePropertyName("activeExecutionId");
            if (activeExecution is { } execution) writer.WriteStringValue(execution.ThreadExecutionId);
            else writer.WriteNullValue();
            writer.WriteNumber("pendingRequestCount", pendingRequestCount);
            writer.WriteEndObject();
        });
    }

    private static void WritePendingRequest(Utf8JsonWriter writer, AgentEvent request)
    {
        writer.WriteStartObject();
        writer.WriteString("eventType", request.GetType().Name);
        switch (request)
        {
            case PermissionRequestEvent permission:
                writer.WriteString("kind", "permission");
                writer.WriteString("requestId", permission.RequestId);
                writer.WriteString("sourceName", permission.SourceName);
                writer.WriteString("functionName", permission.FunctionName ?? "");
                writer.WriteString("action", permission.Action ?? "");
                writer.WriteString("authority", permission.Authority ?? "");
                writer.WriteString("reason", permission.Reason ?? "");
                writer.WriteString("risk", permission.Risk?.ToString() ?? "");
                writer.WriteString("resource", permission.PermissionResource?.DisplayName ?? "");
                writer.WriteStartArray("choices");
                foreach (PermissionChoice choice in permission.Choices)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", choice.Id);
                    writer.WriteString("label", choice.Label);
                    writer.WriteString("kind", choice.Kind.ToString());
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                break;
            case UserQuestionRequestEvent question:
                writer.WriteString("kind", "question");
                writer.WriteString("requestId", question.RequestId);
                writer.WriteString("sourceName", question.SourceName);
                writer.WriteStartArray("questions");
                foreach (AgentQuestion item in question.Questions)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", item.Id);
                    writer.WriteString("text", item.Text);
                    writer.WriteString("header", item.Header ?? "");
                    writer.WriteBoolean("multiple", item.Multiple);
                    writer.WriteString("recommendedOptionId", item.RecommendedOptionId ?? "");
                    writer.WriteStartArray("options");
                    foreach (QuestionOption option in item.Options ?? [])
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", option.Id);
                        writer.WriteString("label", option.Label);
                        writer.WriteString("description", option.Description ?? "");
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                break;
            case ContinuationRequestEvent continuation:
                writer.WriteString("kind", "continuation");
                writer.WriteString("requestId", continuation.RequestId);
                writer.WriteString("sourceName", continuation.SourceName);
                writer.WriteNumber("currentIteration", continuation.CurrentIteration);
                writer.WriteNumber("maxIterations", continuation.MaxIterations);
                break;
            case ClientToolInvokeRequestEvent clientTool:
                writer.WriteString("kind", "clientTool");
                writer.WriteString("requestId", clientTool.RequestId);
                writer.WriteString("sourceName", clientTool.SourceName);
                writer.WriteString("toolName", clientTool.ToolName);
                writer.WriteString("callId", clientTool.CallId);
                writer.WriteString("description", clientTool.Description ?? "");
                writer.WriteString("argumentsJson", SerializeArguments(clientTool.Arguments));
                break;
            default:
                writer.WriteString("kind", "request");
                writer.WriteString("requestId", request is IAgentRequestEvent pending ? pending.RequestId : "");
                writer.WriteString("sourceName", "");
                break;
        }
        writer.WriteEndObject();
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> ConfigurationAsync(
        string applicationId, string agentId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentDefinitionService>() is not { } agents) return null;
        StoredAgentDto? value = await agents.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (value is null) return null;
        HPDAgentHostOptions? host = services.GetService<IOptions<HPDAgentHostOptions>>()?.Value;
        IReadOnlyList<string> errors = AgentConfigValidator.Validate(value.Config, host?.ValidationLimits);
        string config = HpdAgentConfigSerializer.Serialize(value.Config);
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteString("agentId", value.Id);
            writer.WriteStartArray("layers");
            WriteLayer(writer, "library", "Library and service defaults", "inherited",
                "Portable fields without a stored value inherit the host's construction defaults.");
            WriteLayer(writer, "stored", "Stored AgentConfig", "authoritative",
                "Immutable snapshot currently resolved from the Agent definition store.");
            WriteLayer(writer, "host", "Host construction customization", host?.ConfigureEachAgentBuilder is null ? "none" : "configured",
                host?.ConfigureEachAgentBuilder is null
                    ? "This host does not register a per-agent construction customization callback."
                    : "The host applies runtime-only construction customization after the stored definition.");
            WriteLayer(writer, "runOverride", "AgentRunConfig override", "none",
                "No invocation override is active in this configuration inspection.");
            writer.WriteEndArray();
            writer.WriteStartObject("effective");
            writer.WriteString("name", value.Config.Name);
            writer.WriteString("systemInstructions", value.Config.SystemInstructions);
            writer.WriteNumber("maxAgenticIterations", value.Config.MaxAgenticIterations);
            writer.WriteNumber("maxSubAgentDepth", value.Config.MaxSubAgentDepth);
            writer.WriteNumber("toolHarnessCount", value.Config.ToolHarnesses.Count);
            writer.WriteNumber("middlewareCount", value.Config.Middlewares.Count);
            writer.WriteStartArray("toolHarnesses");
            foreach (ToolHarnessReference reference in value.Config.ToolHarnesses.OrderBy(static item => item.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", reference.Name);
                writer.WriteBoolean("allFunctions", reference.Functions is null);
                writer.WriteStartArray("functions");
                foreach (string function in (reference.Functions ?? []).Order(StringComparer.Ordinal)) writer.WriteStringValue(function);
                writer.WriteEndArray();
                writer.WriteBoolean("hasConfiguration", reference.Config is not null);
                writer.WriteBoolean("hasMetadata", reference.Metadata is not null);
                writer.WriteNumber("middlewareConfigurationCount", reference.MiddlewareConfigs?.Count ?? 0);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteStartObject("preflight");
            writer.WriteString("state", errors.Count == 0 ? "valid" : "invalid");
            writer.WriteStartArray("checks");
            WriteCheck(writer, "agent-config", "AgentConfig validation", errors.Count == 0 ? "pass" : "fail",
                errors.Count == 0 ? "Canonical AgentConfig validation passed." : $"{errors.Count} validation error(s) must be resolved.");
            WriteCheck(writer, "toolharnesses", "ToolHarness declarations", value.Config.ToolHarnesses.All(static item => !string.IsNullOrWhiteSpace(item.Name)) ? "pass" : "fail",
                $"{value.Config.ToolHarnesses.Count} ToolHarness reference(s) declared.");
            WriteCheck(writer, "providers", "Provider declaration", value.Config.Clients is null ? "unavailable" : "pass",
                value.Config.Clients is null ? "No provider client declaration is present." : "Provider client configuration is declared for runtime composition.");
            WriteCheck(writer, "mcp", "MCP declarations", value.Config.Mcp is null ? "pass" : "pass",
                value.Config.Mcp is null ? "No MCP declaration requires construction." : "MCP declarations passed canonical configuration validation.");
            WriteCheck(writer, "run-overrides", "Portable run override eligibility", errors.Count == 0 ? "pass" : "warn",
                "Run overrides remain invocation-scoped and are not persisted by this inspection.");
            writer.WriteEndArray();
            writer.WriteStartArray("errors");
            foreach (string error in errors.Take(128)) writer.WriteStringValue(error);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WritePropertyName("canonicalConfig");
            writer.WriteRawValue(config, skipInputValidation: false);
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> SessionAsync(
        string applicationId, string sessionId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentSessionService>() is not { } sessions) return null;
        SessionDto? value = await sessions.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (value is null) return null;
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteString("id", value.Id);
            writer.WriteString("createdAt", value.CreatedAt);
            writer.WriteString("lastActivity", value.LastActivity);
            writer.WriteString("resourceToken", StudioResourceRouteToken.Encode(new StudioAgentSessionResource(applicationId, value.Id)));
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> ThreadsAsync(
        string applicationId, string sessionId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentThreadService>() is not { } threads) return null;
        AgentServiceResult<IReadOnlyList<ThreadDto>> result = await threads.ListThreadsAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (result.Status != AgentServiceStatus.Success || result.Value is null) return null;
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteStartArray("items");
            foreach (ThreadDto value in result.Value.OrderByDescending(static item => item.LastActivity).ThenBy(static item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                WriteThread(writer, applicationId, value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> ThreadAsync(
        string applicationId, string sessionId, string threadId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentThreadService>() is not { } threads) return null;
        AgentServiceResult<ThreadDto> result = await threads.GetThreadAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false);
        if (result.Status != AgentServiceStatus.Success || result.Value is null) return null;
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            WriteThread(writer, applicationId, result.Value);
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> ExecutionsAsync(
        string applicationId, string agentId, string sessionId, string threadId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentThreadExecutionService>() is not { } executions) return null;
        AgentServiceResult<IReadOnlyList<ThreadExecutionDto>> result =
            await executions.ListExecutionsAsync(agentId, sessionId, threadId, cancellationToken).ConfigureAwait(false);
        if (result.Status != AgentServiceStatus.Success || result.Value is null) return null;
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteStartArray("items");
            foreach (ThreadExecutionDto value in result.Value.OrderByDescending(static item => item.StartedAt).ThenBy(static item => item.ThreadExecutionId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                WriteExecution(writer, applicationId, sessionId, threadId, value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> ExecutionAsync(
        string applicationId, string agentId, string sessionId, string threadId, string executionId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentThreadExecutionService>() is not { } executions) return null;
        AgentServiceResult<ThreadExecutionDto> result =
            await executions.GetExecutionAsync(agentId, sessionId, threadId, executionId, cancellationToken).ConfigureAwait(false);
        if (result.Status != AgentServiceStatus.Success || result.Value is null) return null;
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            WriteExecution(writer, applicationId, sessionId, threadId, result.Value);
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> HarnessesAsync(
        string applicationId, string? harnessId, CancellationToken cancellationToken)
    {
        Dictionary<string, HarnessProjection> catalog = await CollectHarnessesAsync(cancellationToken)
            .ConfigureAwait(false);
        HarnessProjection[] values = harnessId is null
            ? catalog.Values.OrderBy(static value => value.Name, StringComparer.Ordinal)
                .ThenBy(static value => value.Id, StringComparer.Ordinal).ToArray()
            : ResolveHarness(catalog, harnessId) is { } selected
                ? [selected]
                : [];
        if (harnessId is not null && values.Length == 0) return null;
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteString("coverage", "generated-runtime-capability-catalog");
            if (harnessId is null) writer.WriteStartArray("items");
            foreach (HarnessProjection value in values)
            {
                if (harnessId is null) writer.WriteStartObject();
                WriteHarness(writer, applicationId, value,
                    services.GetService<IServiceProviderIsService>());
                if (harnessId is null) writer.WriteEndObject();
            }
            if (harnessId is null) writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> FunctionAsync(
        string applicationId, string harnessId, string functionId, CancellationToken cancellationToken)
    {
        Dictionary<string, HarnessProjection> catalog = await CollectHarnessesAsync(cancellationToken)
            .ConfigureAwait(false);
        HarnessProjection? harness = ResolveHarness(catalog, harnessId);
        CapabilityProjection? function = harness?.Functions.Values.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.Capability.Id.Value, functionId) ||
            StringComparer.Ordinal.Equals(value.Capability.Name, functionId));
        if (harness is null || function is null) return null;
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            WriteFunction(writer, applicationId, harness, function, writeContainer: false);
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> FunctionActionAsync(
        string applicationId, string harnessId, string functionId, string actionId,
        CancellationToken cancellationToken)
    {
        Dictionary<string, HarnessProjection> catalog = await CollectHarnessesAsync(cancellationToken)
            .ConfigureAwait(false);
        HarnessProjection? harness = ResolveHarness(catalog, harnessId);
        CapabilityProjection? function = harness?.Functions.Values.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.Capability.Id.Value, functionId) ||
            StringComparer.Ordinal.Equals(value.Capability.Name, functionId));
        AgentFunctionActionInspection? action = function?.Capability.Actions.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.Name, actionId));
        if (harness is null || function is null || action is null) return null;
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            WriteAction(writer, applicationId, harness, function, action, writeContainer: false);
            writer.WriteEndObject();
        });
    }

    private async Task<Dictionary<string, HarnessProjection>> CollectHarnessesAsync(
        CancellationToken cancellationToken)
    {
        ToolHarnessFactory[] factories = AgentGeneratedRegistry.GetToolHarnesses().ToArray();
        var catalog = new Dictionary<string, HarnessProjection>(StringComparer.Ordinal);
        foreach (ToolHarnessFactory factory in factories)
            catalog[factory.ActivationIdentity] = new HarnessProjection(factory.ActivationIdentity, factory);

        IAgentDefinitionService? agents = services.GetService<IAgentDefinitionService>();
        if (agents is null) return catalog;

        AgentManager? manager = services.GetService<AgentManager>();
        IReadOnlyList<AgentSummaryDto> summaries =
            await agents.ListAgentsAsync(cancellationToken).ConfigureAwait(false);
        foreach (AgentSummaryDto summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredAgentDto? stored = await agents.GetAgentAsync(summary.Id, cancellationToken)
                .ConfigureAwait(false);
            if (stored is null) continue;

            Agent? runtime = null;
            if (manager is not null)
            {
                try
                {
                    runtime = await manager.GetOrBuildAgentAsync(summary.Id, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    AddUnresolvedConfiguration(catalog, stored, summary.Id,
                        $"Agent runtime construction failed: {BoundDiagnostic(exception.Message)}");
                }
            }

            AgentConfig config = runtime?.Config ?? stored.Config;
            foreach (ToolHarnessReference reference in config.ToolHarnesses)
            {
                HarnessProjection projection = ResolveOrAddConfiguredHarness(
                    catalog, factories, reference.Name);
                projection.Agents.Add(summary.Id);
                if (reference.Functions is not null)
                    foreach (string function in reference.Functions)
                        projection.ConfiguredFunctions.Add(function);
            }

            if (runtime is null) continue;
            try
            {
                AgentCapabilityInspectionSnapshot snapshot =
                    await runtime.InspectCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
                foreach (AgentCapabilityInspection capability in snapshot.Functions)
                {
                    HarnessProjection? projection = capability.OwnerHarnessIdentity is { Length: > 0 } owner
                        ? ResolveHarness(catalog, owner)
                        : null;
                    if (projection is null && capability.OwnerHarnessIdentity is { Length: > 0 } unknownOwner)
                    {
                        projection = new HarnessProjection(unknownOwner, null);
                        catalog[unknownOwner] = projection;
                        projection.Diagnostics.Add("A materialized capability references an owner that is not present in the generated registry.");
                    }
                    if (projection is null) continue;
                    projection.Instantiated = true;
                    projection.Agents.Add(summary.Id);
                    projection.AddCapability(capability, summary.Id);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AddUnresolvedConfiguration(catalog, stored, summary.Id,
                    $"Capability inspection failed: {BoundDiagnostic(exception.Message)}");
            }
        }

        return catalog;
    }

    private static HarnessProjection ResolveOrAddConfiguredHarness(
        Dictionary<string, HarnessProjection> catalog,
        IReadOnlyList<ToolHarnessFactory> factories,
        string name)
    {
        HarnessProjection? exact = ResolveHarness(catalog, name);
        if (exact is not null) return exact;

        ToolHarnessFactory[] matches = factories.Where(factory =>
                StringComparer.Ordinal.Equals(factory.ActivationIdentity, name) ||
                StringComparer.OrdinalIgnoreCase.Equals(factory.Name, name))
            .ToArray();
        if (matches.Length == 1)
            return catalog[matches[0].ActivationIdentity];

        string id = "unresolved:" + name;
        var unresolved = new HarnessProjection(id, null) { Name = name };
        unresolved.Diagnostics.Add(matches.Length > 1
            ? "The configured short name is ambiguous; use the exact generated activation identity."
            : "No generated ToolHarness owner matches this configured reference.");
        catalog[id] = unresolved;
        return unresolved;
    }

    private static void AddUnresolvedConfiguration(
        Dictionary<string, HarnessProjection> catalog,
        StoredAgentDto stored,
        string agentId,
        string diagnostic)
    {
        foreach (ToolHarnessReference reference in stored.Config.ToolHarnesses)
        {
            HarnessProjection projection = ResolveOrAddConfiguredHarness(
                catalog, AgentGeneratedRegistry.GetToolHarnesses().ToArray(), reference.Name);
            projection.Agents.Add(agentId);
            projection.Diagnostics.Add(diagnostic);
            if (reference.Functions is not null)
                foreach (string function in reference.Functions)
                    projection.ConfiguredFunctions.Add(function);
        }
    }

    private static HarnessProjection? ResolveHarness(
        Dictionary<string, HarnessProjection> catalog, string identity)
    {
        if (catalog.TryGetValue(identity, out HarnessProjection? exact)) return exact;
        HarnessProjection[] aliases = catalog.Values.Where(value =>
                StringComparer.OrdinalIgnoreCase.Equals(value.Name, identity))
            .ToArray();
        return aliases.Length == 1 ? aliases[0] : null;
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> ObservabilityAsync(
        string applicationId, CancellationToken cancellationToken)
    {
        IAgentDefinitionService? agents = services.GetService<IAgentDefinitionService>();
        IAgentSessionService? sessions = services.GetService<IAgentSessionService>();
        IAgentThreadService? threads = services.GetService<IAgentThreadService>();
        if (agents is null || sessions is null || threads is null) return null;
        IReadOnlyList<AgentSummaryDto> agentValues = await agents.ListAgentsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SessionDto> sessionValues = await sessions.SearchSessionsAsync(null, cancellationToken).ConfigureAwait(false);
        int threadCount = 0;
        foreach (SessionDto session in sessionValues)
        {
            AgentServiceResult<IReadOnlyList<ThreadDto>> result =
                await threads.ListThreadsAsync(session.Id, cancellationToken).ConfigureAwait(false);
            if (result.Status == AgentServiceStatus.Success && result.Value is not null) threadCount += result.Value.Count;
        }
        TraceQueryResult? traceQuery = await QueryTracesAsync(cancellationToken).ConfigureAwait(false);
        IAgentStudioTelemetryStore? telemetryStore = services.GetService<IAgentStudioTelemetryStore>();
        TraceProjection[] traces = MergeTelemetryTraces(
            traceQuery?.Traces ?? [], telemetryStore);
        double[] durations = traces.Select(static trace => trace.DurationMilliseconds)
            .Where(static value => value >= 0)
            .Order()
            .ToArray();
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteString("capability", "durable-event-projection");
            writer.WriteNumber("agents", agentValues.Count);
            writer.WriteNumber("sessions", sessionValues.Count);
            writer.WriteNumber("threads", threadCount);
            writer.WriteNumber("traces", traces.Length);
            writer.WriteNumber("failedTraces", traces.Count(static trace => trace.State == "failed"));
            writer.WriteNumber("runningTraces", traces.Count(static trace => trace.State == "running"));
            writer.WriteNumber("events", traces.Sum(static trace => trace.SpanEventCount));
            writer.WriteNumber("inputTokens", traces.Sum(static trace => trace.InputTokens ?? 0));
            writer.WriteNumber("outputTokens", traces.Sum(static trace => trace.OutputTokens ?? 0));
            writer.WriteNumber("totalTokens", traces.Sum(static trace => trace.TotalTokens ?? 0));
            WriteNullableNumber(writer, "p50DurationMs", Percentile(durations, 0.50));
            WriteNullableNumber(writer, "p95DurationMs", Percentile(durations, 0.95));
            WriteNullableNumber(writer, "p99DurationMs", Percentile(durations, 0.99));
            writer.WriteString("traceQuery", "available");
            writer.WriteString("traceSource", traceQuery is null && telemetryStore is null
                ? "unavailable"
                : "canonical-thread-journal-and-studio-telemetry");
            writer.WriteString("metricsSource", traceQuery is null ? "unavailable" : "canonical-thread-journal");
            writer.WriteString("eventsSource", traceQuery is null ? "unavailable" : "canonical-thread-journal");
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> MetricsAsync(
        MetricsListFilter? filter, CancellationToken cancellationToken)
    {
        JournalQueryResult? journal = await QueryJournalAsync(cancellationToken).ConfigureAwait(false);
        if (journal is null) return null;
        IAgentStudioTelemetryStore? telemetryStore = services.GetService<IAgentStudioTelemetryStore>();
        TraceProjection[] traces = ApplyTraceFilter(
            MergeTelemetryTraces(journal.Traces.Traces, telemetryStore),
            filter is null
                ? null
                : new TraceListFilter(
                    filter.AgentId, filter.SessionId, filter.ThreadId, filter.State,
                    filter.After, filter.Before, 0, 200));
        var traceIds = traces.Select(static trace => trace.Id)
            .ToHashSet(StringComparer.Ordinal);
        double[] durations = traces.Select(static trace => trace.DurationMilliseconds)
            .Where(static value => value >= 0)
            .Order()
            .ToArray();
        ToolCallStartEvent[] toolCalls = journal.Events
            .Select(static item => item.Event)
            .OfType<ToolCallStartEvent>()
            .Where(value => value.TraceId is not null && traceIds.Contains(value.TraceId))
            .ToArray();
        var agentBreakdown = traces
            .GroupBy(static trace => trace.AgentId ?? "", StringComparer.Ordinal)
            .Select(group => new MetricBreakdownProjection(
                group.Key,
                group.Count(),
                group.Count(static trace => trace.State == "failed"),
                group.Average(static trace => trace.DurationMilliseconds),
                group.Sum(static trace => trace.TotalTokens ?? 0)))
            .OrderByDescending(static value => value.Count)
            .ThenBy(static value => value.Id, StringComparer.Ordinal)
            .ToArray();
        var functionBreakdown = toolCalls
            .GroupBy(static value => (
                Harness: value.ToolHarnessName ?? "",
                Function: value.Name))
            .Select(group => new FunctionMetricBreakdownProjection(
                group.Key.Harness,
                group.Key.Function,
                group.Count()))
            .OrderByDescending(static value => value.Calls)
            .ThenBy(static value => value.Harness, StringComparer.Ordinal)
            .ThenBy(static value => value.Function, StringComparer.Ordinal)
            .ToArray();
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteString("source", "canonical-thread-journal");
            writer.WriteStartArray("measures");
            WriteMeasure(writer, "traces", "Traces", traces.Length, "count");
            WriteMeasure(writer, "completed", "Completed", traces.Count(static trace => trace.State == "completed"), "count");
            WriteMeasure(writer, "failed", "Failed", traces.Count(static trace => trace.State == "failed"), "count");
            WriteMeasure(writer, "running", "Running", traces.Count(static trace => trace.State == "running"), "count");
            WriteMeasure(writer, "calls", "Function calls", toolCalls.Length, "count");
            WriteNullableMeasure(writer, "p50", "P50 latency", Percentile(durations, 0.50), "ms");
            WriteNullableMeasure(writer, "p95", "P95 latency", Percentile(durations, 0.95), "ms");
            WriteNullableMeasure(writer, "p99", "P99 latency", Percentile(durations, 0.99), "ms");
            WriteMeasure(writer, "inputTokens", "Input tokens",
                traces.Sum(static trace => trace.InputTokens ?? 0), "tokens");
            WriteMeasure(writer, "outputTokens", "Output tokens",
                traces.Sum(static trace => trace.OutputTokens ?? 0), "tokens");
            WriteMeasure(writer, "totalTokens", "Total tokens",
                traces.Sum(static trace => trace.TotalTokens ?? 0), "tokens");
            writer.WriteEndArray();
            writer.WriteStartArray("agents");
            foreach (MetricBreakdownProjection value in agentBreakdown)
            {
                writer.WriteStartObject();
                writer.WriteString("id", value.Id);
                writer.WriteNumber("traces", value.Count);
                writer.WriteNumber("failed", value.Failed);
                writer.WriteNumber("averageDurationMs", value.AverageDurationMilliseconds);
                writer.WriteNumber("totalTokens", value.TotalTokens);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("functions");
            foreach (FunctionMetricBreakdownProjection value in functionBreakdown)
            {
                writer.WriteStartObject();
                writer.WriteString("harness", value.Harness);
                writer.WriteString("function", value.Function);
                writer.WriteNumber("calls", value.Calls);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("capabilities");
            writer.WriteStartObject();
            writer.WriteBoolean("serverFiltering", true);
            writer.WriteBoolean("liveTail", false);
            writer.WriteBoolean("payloadInspection", false);
            writer.WriteBoolean("agentFilter", true);
            writer.WriteBoolean("sessionFilter", true);
            writer.WriteBoolean("threadFilter", true);
            writer.WriteBoolean("outcomeFilter", true);
            writer.WriteBoolean("timeFilter", true);
            writer.WriteString("timeRange", filter?.After is null && filter?.Before is null
                ? "all-retained-evidence"
                : "filtered-retained-evidence");
            writer.WriteBoolean("truncated", journal.Truncated);
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    private static MetricsListFilter? ParseMetricsFilter(string query)
    {
        if (String.IsNullOrEmpty(query) || query == "?") return null;
        string[] pairs = query.TrimStart('?').Split('&', StringSplitOptions.None);
        string? agentId = null;
        string? sessionId = null;
        string? threadId = null;
        string? state = null;
        DateTimeOffset? after = null;
        DateTimeOffset? before = null;
        foreach (string pair in pairs)
        {
            int separator = pair.IndexOf('=');
            if (separator <= 0 || separator == pair.Length - 1)
                throw new ArgumentException("Metrics query members must contain a name and value.", nameof(query));
            string name = Uri.UnescapeDataString(pair[..separator]);
            string value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            switch (name)
            {
                case "agentId" when agentId is null:
                    agentId = BoundedMetricsIdentity(value, name, query);
                    break;
                case "sessionId" when sessionId is null:
                    sessionId = BoundedMetricsIdentity(value, name, query);
                    break;
                case "threadId" when threadId is null:
                    threadId = BoundedMetricsIdentity(value, name, query);
                    break;
                case "state" when state is null:
                    state = value is "completed" or "failed" or "running" or "cancelled"
                        ? value
                        : throw new ArgumentException("Metrics state is not registered.", nameof(query));
                    break;
                case "after" when after is null:
                    after = MetricsUnixMilliseconds(value, name, query);
                    break;
                case "before" when before is null:
                    before = MetricsUnixMilliseconds(value, name, query);
                    break;
                default:
                    throw new ArgumentException("Metrics query contains an unknown or duplicate member.", nameof(query));
            }
        }
        if (after is not null && before is not null && after > before)
            throw new ArgumentException("Metrics time range is inverted.", nameof(query));
        return new MetricsListFilter(agentId, sessionId, threadId, state, after, before);

        static string BoundedMetricsIdentity(string value, string name, string query) =>
            value.Length is > 0 and <= 128 && value.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
                ? value
                : throw new ArgumentException(
                    $"{name} is not a bounded identity.", nameof(query));

        static DateTimeOffset MetricsUnixMilliseconds(string value, string name, string query) =>
            Int64.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long parsed) &&
            parsed >= 0 && parsed <= 253_402_300_799_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(parsed)
                : throw new ArgumentException(
                    $"{name} is not a valid Unix-millisecond timestamp.", nameof(query));
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> EventsAsync(
        EventListFilter? filter, CancellationToken cancellationToken)
    {
        JournalQueryResult? journal = await QueryJournalAsync(cancellationToken).ConfigureAwait(false);
        if (journal is null) return null;
        TraceEventProjection[] matching = ApplyEventFilter(journal.Events, filter);
        int offset = filter?.Offset ?? 0;
        int limit = filter?.Limit ?? 250;
        TraceEventProjection[] events = matching.Skip(offset).Take(limit).ToArray();
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteString("source", "canonical-thread-journal");
            writer.WriteNumber("count", events.Length);
            writer.WriteNumber("total", matching.Length);
            writer.WriteNumber("offset", offset);
            writer.WriteNumber("limit", limit);
            writer.WriteBoolean("hasMore", offset + events.Length < matching.Length);
            writer.WriteBoolean("truncated",
                journal.Truncated || offset + events.Length < matching.Length);
            writer.WritePropertyName("capabilities");
            writer.WriteStartObject();
            writer.WriteBoolean("liveTail", false);
            writer.WriteBoolean("payloadInspection", true);
            writer.WriteBoolean("serverFiltering", true);
            writer.WriteBoolean("offsetPagination", true);
            writer.WriteString("cursor", "retained-journal-head");
            writer.WriteEndObject();
            writer.WriteStartArray("items");
            foreach (TraceEventProjection item in events)
                WriteJournalEvent(writer, item);
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> EventPayloadAsync(
        string eventId, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        JournalQueryResult? journal = await QueryJournalAsync(cancellationToken).ConfigureAwait(false);
        TraceEventProjection? selected = journal?.Events.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.Event.EventId, eventId));
        if (selected is null) return null;
        IAgentStudioEventPayloadAuthority? authority =
            services.GetService<IAgentStudioEventPayloadAuthority>();
        if (authority is null ||
            !await authority.CanInspectAsync(principal, selected.Event, cancellationToken).ConfigureAwait(false))
            return Json(writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("state", "denied");
                writer.WriteString("eventId", selected.Event.EventId);
                writer.WriteString("type", selected.Event.GetType().Name);
                writer.WriteString("payloadFormat", "unavailable");
                writer.WriteBoolean("redacted", true);
                writer.WriteNull("payload");
                writer.WriteEndObject();
            });

        AgentStudioPayloadOptions options =
            services.GetService<Microsoft.Extensions.Options.IOptions<AgentStudioPayloadOptions>>()?.Value
            ?? new AgentStudioPayloadOptions();
        var sanitizer = new SpanPayloadSanitizer(new SpanSanitizerOptions
        {
            EnableRedaction = true,
            MaxStringLength = Math.Clamp(options.MaximumLength, 256, 262_144)
        });
        (string? payload, string format) = ExtractEventPayload(selected.Event);
        string? sanitized = payload is null ? null : sanitizer.Sanitize(payload);
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", sanitized is null ? "unavailable" : "authorized");
            writer.WriteString("eventId", selected.Event.EventId);
            writer.WriteString("type", selected.Event.GetType().Name);
            writer.WriteString("payloadFormat", format);
            writer.WriteBoolean("redacted", true);
            WriteNullableString(writer, "payload", sanitized);
            writer.WriteEndObject();
        });
    }

    private static (string? Payload, string Format) ExtractEventPayload(AgentEvent evt) => evt switch
    {
        TextDeltaEvent value => (value.Text, "text"),
        UserMessageEvent value => (value.Text, "text"),
        UserAudioTranscriptDeltaEvent value => (value.Text, "text"),
        ToolCallArgsEvent value => (value.ArgsJson, "json"),
        ToolCallEndEvent value => (value.ArgsJson, "json"),
        ToolCallResultEvent value => (
            value.Result.Json?.GetRawText() ?? value.Result.Text ?? value.Result.ResultType,
            value.Result.Json is null ? "text" : "json"),
        PermissionRequestEvent value => (
            value.Arguments?.GetRawText() ?? value.Reason,
            value.Arguments is null ? "text" : "json"),
        PermissionResponseEvent value => (value.Feedback, "text"),
        UserQuestionRequestEvent value => (
            String.Join("\n", value.Questions.Select(static question => question.Text)),
            "text"),
        QuestionResponseEvent value => (
            String.Join("\n", value.Answers.Select(static answer =>
                answer.CustomText ?? answer.Notes ?? String.Join(",", answer.SelectedOptionIds))),
            "text"),
        ThreadMessageReplacedEvent value => (value.Replacement.Text, "text"),
        SubAgentInvocationCompletedEvent value => (value.Summary, "text"),
        _ => (null, "unavailable")
    };

    private static TraceEventProjection[] ApplyEventFilter(
        IReadOnlyList<TraceEventProjection> events, EventListFilter? filter)
    {
        if (filter is null) return events.Take(2_000).ToArray();
        IEnumerable<TraceEventProjection> values = events;
        if (filter.AgentId is not null)
            values = values.Where(value =>
                StringComparer.Ordinal.Equals(EventAgentId(value), filter.AgentId));
        if (filter.SessionId is not null)
            values = values.Where(value =>
                StringComparer.Ordinal.Equals(value.SessionId, filter.SessionId));
        if (filter.ThreadId is not null)
            values = values.Where(value =>
                StringComparer.Ordinal.Equals(value.ThreadId, filter.ThreadId));
        if (filter.TraceId is not null)
            values = values.Where(value =>
                StringComparer.Ordinal.Equals(value.Event.TraceId, filter.TraceId));
        if (filter.SpanId is not null)
            values = values.Where(value =>
                StringComparer.Ordinal.Equals(value.Event.SpanId, filter.SpanId));
        if (filter.Type is not null)
            values = values.Where(value =>
                StringComparer.Ordinal.Equals(value.Event.GetType().Name, filter.Type));
        if (filter.ErrorsOnly)
            values = values.Where(value => value.Event is IErrorEvent);
        if (filter.After is not null)
            values = values.Where(value => value.Event.Timestamp >= filter.After);
        if (filter.Before is not null)
            values = values.Where(value => value.Event.Timestamp <= filter.Before);
        return values.ToArray();
    }

    private static string EventAgentId(TraceEventProjection item) =>
        item.Event switch
        {
            MessageTurnStartedEvent value => value.AgentId,
            MessageTurnFinishedEvent value => value.AgentId,
            MessageTurnErrorEvent value => value.AgentId ?? "",
            ThreadExecutionStartedEvent value => value.AgentId,
            ThreadExecutionFinishedEvent value => value.AgentId,
            _ => item.Event.Metadata?.AgentId ?? item.DefaultAgentId
        };

    private static EventListFilter? ParseEventFilter(string query)
    {
        if (String.IsNullOrEmpty(query) || query == "?") return null;
        string[] pairs = query.TrimStart('?').Split('&', StringSplitOptions.None);
        string? agentId = null;
        string? sessionId = null;
        string? threadId = null;
        string? traceId = null;
        string? spanId = null;
        string? type = null;
        bool errorsOnly = false;
        DateTimeOffset? after = null;
        DateTimeOffset? before = null;
        int? offset = null;
        int? limit = null;
        foreach (string pair in pairs)
        {
            int separator = pair.IndexOf('=');
            if (separator <= 0 || separator == pair.Length - 1)
                throw new ArgumentException("Event query members must contain a name and value.", nameof(query));
            string name = Uri.UnescapeDataString(pair[..separator]);
            string value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            switch (name)
            {
                case "agentId" when agentId is null:
                    agentId = BoundedEventQueryIdentity(value, name, query);
                    break;
                case "sessionId" when sessionId is null:
                    sessionId = BoundedEventQueryIdentity(value, name, query);
                    break;
                case "threadId" when threadId is null:
                    threadId = BoundedEventQueryIdentity(value, name, query);
                    break;
                case "traceId" when traceId is null:
                    traceId = BoundedEventQueryIdentity(value, name, query);
                    break;
                case "spanId" when spanId is null:
                    spanId = BoundedEventQueryIdentity(value, name, query);
                    break;
                case "type" when type is null:
                    type = BoundedEventQueryIdentity(value, name, query);
                    break;
                case "errors" when !errorsOnly:
                    errorsOnly = value == "true";
                    if (!errorsOnly) throw new ArgumentException("Event errors filter only accepts true.", nameof(query));
                    break;
                case "after" when after is null:
                    after = EventUnixMilliseconds(value, name, query);
                    break;
                case "before" when before is null:
                    before = EventUnixMilliseconds(value, name, query);
                    break;
                case "offset" when offset is null:
                    offset = BoundedEventInteger(value, name, 10_000, query);
                    break;
                case "limit" when limit is null:
                    limit = BoundedEventInteger(value, name, 2_000, query);
                    if (limit == 0) throw new ArgumentException("Event limit must be positive.", nameof(query));
                    break;
                default:
                    throw new ArgumentException("Event query contains an unknown or duplicate member.", nameof(query));
            }
        }
        if (after is not null && before is not null && after > before)
            throw new ArgumentException("Event time range is inverted.", nameof(query));
        return new EventListFilter(
            agentId, sessionId, threadId, traceId, spanId, type, errorsOnly,
            after, before, offset ?? 0, limit ?? 250);

        static string BoundedEventQueryIdentity(string value, string name, string query) =>
            value.Length is > 0 and <= 128 && value.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
                ? value
                : throw new ArgumentException(
                    $"{name} is not a bounded identity.", nameof(query));

        static int BoundedEventInteger(string value, string name, int maximum, string query) =>
            Int32.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int parsed) &&
            parsed >= 0 && parsed <= maximum
                ? parsed
                : throw new ArgumentException(
                    $"{name} is outside its permitted range.", nameof(query));

        static DateTimeOffset EventUnixMilliseconds(string value, string name, string query) =>
            Int64.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long parsed) &&
            parsed >= 0 && parsed <= 253_402_300_799_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(parsed)
                : throw new ArgumentException(
                    $"{name} is not a valid Unix-millisecond timestamp.", nameof(query));
    }

    private async ValueTask<StudioFrameworkSurfaceResponse?> TracesAsync(
        string applicationId, string? traceId, TraceListFilter? filter,
        CancellationToken cancellationToken)
    {
        IAgentStudioTelemetryStore? telemetryStore = services.GetService<IAgentStudioTelemetryStore>();
        TraceQueryResult? journalQuery = await QueryTracesAsync(cancellationToken).ConfigureAwait(false);
        TraceProjection[] source = MergeTelemetryTraces(
            journalQuery?.Traces ?? [], telemetryStore);
        TraceProjection[] matching = traceId is null
            ? ApplyTraceFilter(source, filter)
            : source.Where(value => StringComparer.Ordinal.Equals(value.Id, traceId)).ToArray();
        if (traceId is not null && matching.Length == 0 && journalQuery is null) return null;
        if (traceId is not null && matching.Length == 0) return null;
        TraceProjection[] values = traceId is null
            ? matching.Skip(filter?.Offset ?? 0).Take(filter?.Limit ?? 100).ToArray()
            : matching;
        AgentStudioTelemetryTrace? telemetryTrace = null;
        if (traceId is not null &&
            telemetryStore?.TryGetTrace(traceId, out telemetryTrace) != true)
            telemetryTrace = null;
        return Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", "current");
            writer.WriteString("source", "canonical-thread-journal");
            if (traceId is null)
            {
                writer.WriteNumber("total", matching.Length);
                writer.WriteNumber("count", values.Length);
                writer.WriteNumber("offset", filter?.Offset ?? 0);
                writer.WriteNumber("limit", filter?.Limit ?? 100);
                writer.WriteBoolean("hasMore", (filter?.Offset ?? 0) + values.Length < matching.Length);
                writer.WriteBoolean("truncated",
                    journalQuery?.Truncated == true ||
                    (filter?.Offset ?? 0) + values.Length < matching.Length);
                writer.WriteStartObject("capabilities");
                writer.WriteBoolean("agentFilter", true);
                writer.WriteBoolean("sessionFilter", true);
                writer.WriteBoolean("threadFilter", true);
                writer.WriteBoolean("outcomeFilter", true);
                writer.WriteBoolean("timeFilter", true);
                writer.WriteBoolean("offsetPagination", true);
                writer.WriteBoolean("keysetPagination", false);
                writer.WriteBoolean("serverFiltering", true);
                writer.WriteEndObject();
                writer.WriteStartArray("items");
            }
            foreach (TraceProjection value in values)
            {
                if (traceId is null) writer.WriteStartObject();
                WriteTrace(writer, applicationId, value, detail: traceId is not null, telemetryTrace);
                if (traceId is null) writer.WriteEndObject();
            }
            if (traceId is null) writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private static TraceProjection[] MergeTelemetryTraces(
        IReadOnlyList<TraceProjection> journalTraces,
        IAgentStudioTelemetryStore? telemetryStore)
    {
        var values = new Dictionary<string, TraceProjection>(StringComparer.Ordinal);
        foreach (TraceProjection trace in journalTraces)
            values[trace.Id] = EnrichTraceWithTelemetry(trace, telemetryStore);
        if (telemetryStore is not null)
        {
            foreach (AgentStudioTelemetryTrace telemetry in telemetryStore.ListTraces(500))
            {
                if (values.ContainsKey(telemetry.TraceId)) continue;
                AgentStudioTelemetrySpan? root = telemetry.Spans
                    .FirstOrDefault(static span => span.ParentSpanId is null);
                if (root is null) continue;
                root.Attributes.TryGetValue("agent.name", out string? agentName);
                root.Attributes.TryGetValue("hpd.session_id", out string? sessionId);
                root.Attributes.TryGetValue("hpd.thread_id", out string? threadId);
                values.Add(telemetry.TraceId, new TraceProjection(
                    telemetry.TraceId,
                    String.IsNullOrWhiteSpace(agentName) ? "Agent turn" : $"{agentName} turn",
                    root.Status == "failed" ? "failed" : "completed",
                    root.StartedAt,
                    root.DurationMilliseconds,
                    root.Attributes.GetValueOrDefault("agent.id"),
                    sessionId ?? "",
                    threadId ?? "",
                    null,
                    null,
                    null,
                    null,
                    null,
                    0,
                    telemetry.Spans.Select(static span => new SpanProjection(
                        span.SpanId,
                        span.ParentSpanId,
                        span.Name,
                        span.Kind,
                        span.Status,
                        span.StartedAt,
                        span.DurationMilliseconds,
                        span.Attributes)).ToArray(),
                    []));
            }
        }
        return values.Values
            .OrderByDescending(static trace => trace.StartedAt)
            .ThenBy(static trace => trace.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static TraceProjection EnrichTraceWithTelemetry(
        TraceProjection trace, IAgentStudioTelemetryStore? telemetryStore)
    {
        if (telemetryStore is null ||
            !telemetryStore.TryGetTrace(trace.Id, out AgentStudioTelemetryTrace telemetry))
            return trace;
        AgentStudioTelemetrySpan? root = telemetry.Spans
            .FirstOrDefault(static span => span.ParentSpanId is null);
        return root is null
            ? trace
            : trace with
            {
                State = root.Status == "failed" ? "failed" : trace.State,
                DurationMilliseconds = root.DurationMilliseconds
            };
    }

    private static TraceProjection[] ApplyTraceFilter(
        IReadOnlyList<TraceProjection> traces, TraceListFilter? filter)
    {
        if (filter is null) return traces.ToArray();
        IEnumerable<TraceProjection> values = traces;
        if (filter.AgentId is not null)
            values = values.Where(value =>
                StringComparer.Ordinal.Equals(value.AgentId, filter.AgentId));
        if (filter.SessionId is not null)
            values = values.Where(value =>
                StringComparer.Ordinal.Equals(value.SessionId, filter.SessionId));
        if (filter.ThreadId is not null)
            values = values.Where(value =>
                StringComparer.Ordinal.Equals(value.ThreadId, filter.ThreadId));
        if (filter.State is not null)
            values = values.Where(value =>
                StringComparer.Ordinal.Equals(value.State, filter.State));
        if (filter.After is not null)
            values = values.Where(value => value.StartedAt >= filter.After);
        if (filter.Before is not null)
            values = values.Where(value => value.StartedAt <= filter.Before);
        return values.ToArray();
    }

    private static TraceListFilter? ParseTraceFilter(string query)
    {
        if (String.IsNullOrEmpty(query) || query == "?") return null;
        string[] pairs = query.TrimStart('?').Split('&', StringSplitOptions.None);
        string? agentId = null;
        string? sessionId = null;
        string? threadId = null;
        string? state = null;
        DateTimeOffset? after = null;
        DateTimeOffset? before = null;
        int? offset = null;
        int? limit = null;
        foreach (string pair in pairs)
        {
            int separator = pair.IndexOf('=');
            if (separator <= 0 || separator == pair.Length - 1)
                throw new ArgumentException("Trace query members must contain a name and value.", nameof(query));
            string name = Uri.UnescapeDataString(pair[..separator]);
            string value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            switch (name)
            {
                case "agentId" when agentId is null:
                    agentId = BoundedQueryIdentity(value, name, query);
                    break;
                case "sessionId" when sessionId is null:
                    sessionId = BoundedQueryIdentity(value, name, query);
                    break;
                case "threadId" when threadId is null:
                    threadId = BoundedQueryIdentity(value, name, query);
                    break;
                case "state" when state is null:
                    state = value is "completed" or "failed" or "running" or "cancelled"
                        ? value
                        : throw new ArgumentException("Trace state is not registered.", nameof(query));
                    break;
                case "after" when after is null:
                    after = UnixMilliseconds(value, name, query);
                    break;
                case "before" when before is null:
                    before = UnixMilliseconds(value, name, query);
                    break;
                case "offset" when offset is null:
                    offset = BoundedInteger(value, name, 10_000, query);
                    break;
                case "limit" when limit is null:
                    limit = BoundedInteger(value, name, 200, query);
                    if (limit == 0) throw new ArgumentException("Trace limit must be positive.", nameof(query));
                    break;
                default:
                    throw new ArgumentException("Trace query contains an unknown or duplicate member.", nameof(query));
            }
        }
        if (after is not null && before is not null && after > before)
            throw new ArgumentException("Trace time range is inverted.", nameof(query));
        return new TraceListFilter(
            agentId, sessionId, threadId, state, after, before, offset ?? 0, limit ?? 100);

        static string BoundedQueryIdentity(string value, string name, string query) =>
            value.Length is > 0 and <= 128 && value.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
                ? value
                : throw new ArgumentException(
                    $"{name} is not a bounded identity.", nameof(query));

        static int BoundedInteger(string value, string name, int maximum, string query) =>
            Int32.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int parsed) &&
            parsed >= 0 && parsed <= maximum
                ? parsed
                : throw new ArgumentException(
                    $"{name} is outside its permitted range.", nameof(query));

        static DateTimeOffset UnixMilliseconds(string value, string name, string query) =>
            Int64.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long parsed) &&
            parsed >= 0 && parsed <= 253_402_300_799_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(parsed)
                : throw new ArgumentException(
                    $"{name} is not a valid Unix-millisecond timestamp.", nameof(query));
    }

    private async ValueTask<bool> TraceExistsAsync(string traceId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentStudioTelemetryStore>()
                ?.TryGetTrace(traceId, out _) == true)
            return true;
        TraceQueryResult? query = await QueryTracesAsync(cancellationToken).ConfigureAwait(false);
        return query?.Traces.Any(value => StringComparer.Ordinal.Equals(value.Id, traceId)) == true;
    }

    private async Task<TraceQueryResult?> QueryTracesAsync(CancellationToken cancellationToken)
    {
        JournalQueryResult? journal = await QueryJournalAsync(cancellationToken).ConfigureAwait(false);
        return journal?.Traces;
    }

    private async Task<JournalQueryResult?> QueryJournalAsync(CancellationToken cancellationToken)
    {
        SessionManager? manager = services.GetService<SessionManager>();
        if (manager is null) return null;
        ISessionStore store = manager.Store;
        IAgentThreadExecutionService? executionService =
            services.GetService<IAgentThreadExecutionService>();
        var projected = new Dictionary<string, List<TraceEventProjection>>(StringComparer.Ordinal);
        var allEvents = new List<TraceEventProjection>();
        var executionTimes = new Dictionary<string, ExecutionTimeProjection>(StringComparer.Ordinal);
        IReadOnlyList<string> sessionIds = await store.ListSessionIdsAsync(cancellationToken).ConfigureAwait(false);
        int threadBudget = 2_000;
        int eventBudget = 100_000;
        foreach (string sessionId in sessionIds.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (ThreadDescriptor descriptor in store.ListThreadsAsync(
                               sessionId, new ThreadListRequest(IncludeHidden: true, MaxCount: 500),
                               cancellationToken).ConfigureAwait(false))
            {
                if (threadBudget-- <= 0 || eventBudget <= 0) break;
                if (executionService is not null)
                {
                    AgentServiceResult<IReadOnlyList<ThreadExecutionDto>> executions =
                        await executionService.ListExecutionsAsync(
                            descriptor.DefaultAgent.AgentId,
                            descriptor.Key.SessionId,
                            descriptor.Key.ThreadId,
                            cancellationToken).ConfigureAwait(false);
                    if (executions.Status == AgentServiceStatus.Success && executions.Value is not null)
                    {
                        foreach (ThreadExecutionDto execution in executions.Value)
                        {
                            DateTimeOffset startedAt = execution.StartedAt;
                            DateTimeOffset? finishedAt = execution.FinishedAt;
                            executionTimes[execution.ThreadExecutionId] = new ExecutionTimeProjection(
                                execution.ThreadExecutionId,
                                descriptor.Key.SessionId,
                                descriptor.Key.ThreadId,
                                startedAt,
                                finishedAt);
                        }
                    }
                }
                await foreach (ThreadEventBatch batch in store.ReadThreadEventsAsync(
                                   descriptor.Key,
                                   new ThreadEventReadRequest(ThreadJournalCursor.Start(descriptor.Generation)),
                                   cancellationToken).ConfigureAwait(false))
                {
                    foreach (AgentEvent evt in batch.Events)
                    {
                        if (evt is ThreadExecutionStartedEvent executionStarted &&
                            executionStarted.ThreadExecutionId is { Length: > 0 } startedId)
                            executionTimes[startedId] = new ExecutionTimeProjection(
                                startedId, descriptor.Key.SessionId, descriptor.Key.ThreadId,
                                executionStarted.StartedAt, null);
                        if (evt is ThreadExecutionFinishedEvent executionFinished &&
                            executionFinished.ThreadExecutionId is { Length: > 0 } finishedId)
                        {
                            DateTimeOffset startedAt = executionTimes.TryGetValue(
                                finishedId, out ExecutionTimeProjection? existing)
                                ? existing.StartedAt
                                : executionFinished.FinishedAt;
                            executionTimes[finishedId] = new ExecutionTimeProjection(
                                finishedId, descriptor.Key.SessionId, descriptor.Key.ThreadId,
                                startedAt, executionFinished.FinishedAt);
                        }
                        if (eventBudget-- <= 0) continue;
                        var projectedEvent = new TraceEventProjection(
                            evt,
                            descriptor.Key.SessionId,
                            descriptor.Key.ThreadId,
                            descriptor.DefaultAgent.AgentId,
                            descriptor.Generation,
                            evt.ThreadSequenceNumber);
                        allEvents.Add(projectedEvent);
                        if (evt.TraceId is null) continue;
                        if (!projected.TryGetValue(evt.TraceId, out List<TraceEventProjection>? events))
                            projected.Add(evt.TraceId, events = []);
                        events.Add(projectedEvent);
                    }
                    if (eventBudget <= 0) break;
                }
            }
            if (threadBudget <= 0 || eventBudget <= 0) break;
        }

        TraceProjection[] traces = projected
            .Select(pair => ProjectTrace(pair.Key, pair.Value, executionTimes))
            .OrderByDescending(static trace => trace.StartedAt)
            .ThenBy(static trace => trace.Id, StringComparer.Ordinal)
            .Take(500)
            .ToArray();
        bool truncated = eventBudget <= 0 || threadBudget <= 0;
        return new JournalQueryResult(
            new TraceQueryResult(traces, truncated),
            allEvents.OrderByDescending(static item => item.Event.Timestamp)
                .ThenBy(static item => item.Generation)
                .ThenByDescending(static item => item.SequenceNumber)
                .Take(5_000)
                .ToArray(),
            truncated);
    }

    private static TraceProjection ProjectTrace(
        string traceId, List<TraceEventProjection> items,
        IReadOnlyDictionary<string, ExecutionTimeProjection> executionTimes)
    {
        TraceEventProjection[] events = items
            .OrderBy(static item => item.Event.Timestamp)
            .ThenBy(static item => item.Generation)
            .ThenBy(static item => item.SequenceNumber)
            .ToArray();
        TraceEventProjection? rootStart = events.FirstOrDefault(
            static item => item.Event is MessageTurnStartedEvent);
        MessageTurnFinishedEvent? finished = events
            .Select(static item => item.Event)
            .OfType<MessageTurnFinishedEvent>()
            .LastOrDefault();
        MessageTurnErrorEvent? error = events
            .Select(static item => item.Event)
            .OfType<MessageTurnErrorEvent>()
            .LastOrDefault();
        ThreadExecutionStartedEvent? executionStarted = events
            .Select(static item => item.Event)
            .OfType<ThreadExecutionStartedEvent>()
            .FirstOrDefault();
        ThreadExecutionFinishedEvent? executionFinished = events
            .Select(static item => item.Event)
            .OfType<ThreadExecutionFinishedEvent>()
            .LastOrDefault();
        DateTimeOffset startedAt = rootStart?.Event.Timestamp ?? events[0].Event.Timestamp;
        DateTimeOffset endedAt = events[^1].Event.Timestamp;
        double journalDuration = Math.Max(0, (endedAt - startedAt).TotalMilliseconds);
        double executionDuration = executionStarted is not null && executionFinished is not null
            ? Math.Max(0, (executionFinished.FinishedAt - executionStarted.StartedAt).TotalMilliseconds)
            : 0;
        string? executionId = events.Select(static item => item.Event.ThreadExecutionId)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        ExecutionTimeProjection? scopedExecution = executionTimes.Values
            .Where(value =>
                StringComparer.Ordinal.Equals(value.SessionId, events[0].SessionId) &&
                StringComparer.Ordinal.Equals(value.ThreadId, events[0].ThreadId) &&
                value.StartedAt <= startedAt.AddSeconds(1))
            .OrderByDescending(static value => value.StartedAt)
            .FirstOrDefault();
        executionId ??= scopedExecution?.ExecutionId;
        if (executionDuration == 0 && executionId is not null &&
            executionTimes.TryGetValue(executionId, out ExecutionTimeProjection? executionTime) &&
            executionTime.FinishedAt is { } executionFinishedAt)
            executionDuration = Math.Max(0, (executionFinishedAt - executionTime.StartedAt).TotalMilliseconds);
        if (executionDuration == 0 && scopedExecution?.FinishedAt is { } scopedFinishedAt)
            executionDuration = Math.Max(0, (scopedFinishedAt - scopedExecution.StartedAt).TotalMilliseconds);
        double duration = finished is not null && finished.Duration > TimeSpan.Zero
            ? Math.Max(0, finished.Duration.TotalMilliseconds)
            : executionDuration > 0 ? executionDuration : journalDuration;
        string state = error is not null
            ? "failed"
            : finished is not null
                ? finished.TerminationReason?.Contains("cancel", StringComparison.OrdinalIgnoreCase) == true
                    ? "cancelled"
                    : "completed"
                : "running";
        string? agentId = (rootStart?.Event as MessageTurnStartedEvent)?.AgentId
            ?? events.Select(static item => item.Event.Metadata?.AgentId)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?? events[0].DefaultAgentId;
        string name = (rootStart?.Event as MessageTurnStartedEvent)?.AgentName is { Length: > 0 } agentName
            ? $"{agentName} turn"
            : "Agent turn";
        UsageDetails? usage = finished?.Usage.AggregateCompatibleUsage();
        SpanProjection[] spans = ProjectSpans(events, duration);
        return new TraceProjection(
            traceId,
            name,
            state,
            startedAt,
            duration,
            agentId,
            events[0].SessionId,
            events[0].ThreadId,
            executionId,
            usage?.InputTokenCount,
            usage?.OutputTokenCount,
            usage?.TotalTokenCount,
            error?.ErrorMessage,
            events.Length,
            spans,
            events);
    }

    private static SpanProjection[] ProjectSpans(
        IReadOnlyList<TraceEventProjection> events, double traceDuration)
    {
        return events
            .Where(static item => item.Event.SpanId is { Length: > 0 })
            .GroupBy(static item => item.Event.SpanId!, StringComparer.Ordinal)
            .Select(group =>
            {
                TraceEventProjection[] spanEvents = group
                    .OrderBy(static item => item.Event.Timestamp)
                    .ThenBy(static item => item.SequenceNumber)
                    .ToArray();
                AgentEvent first = spanEvents[0].Event;
                AgentEvent defining = spanEvents
                    .Select(static item => item.Event)
                    .FirstOrDefault(static evt => evt is
                        MessageTurnStartedEvent or
                        AgentTurnStartedEvent or
                        ToolCallStartEvent)
                    ?? first;
                DateTimeOffset startedAt = spanEvents[0].Event.Timestamp;
                DateTimeOffset endedAt = spanEvents[^1].Event.Timestamp;
                double duration = first is MessageTurnStartedEvent
                    ? traceDuration
                    : Math.Max(0, (endedAt - startedAt).TotalMilliseconds);
                string status = spanEvents.Any(static item => item.Event is MessageTurnErrorEvent or IErrorEvent)
                    ? "failed"
                    : "completed";
                var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["event.count"] = spanEvents.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["session.id"] = spanEvents[0].SessionId,
                    ["thread.id"] = spanEvents[0].ThreadId
                };
                if (spanEvents[0].Event.ThreadExecutionId is { Length: > 0 } execution)
                    attributes["thread.execution.id"] = execution;
                switch (defining)
                {
                    case MessageTurnStartedEvent turn:
                        attributes["agent.id"] = turn.AgentId;
                        attributes["agent.name"] = turn.AgentName;
                        attributes["message.turn.id"] = turn.MessageTurnId;
                        break;
                    case AgentTurnStartedEvent iteration:
                        attributes["iteration"] = iteration.Iteration.ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case ToolCallStartEvent tool:
                        attributes["tool.call_id"] = tool.CallId;
                        attributes["tool.name"] = tool.Name;
                        if (tool.ToolHarnessName is { Length: > 0 } harness)
                            attributes["tool.harness"] = harness;
                        break;
                    case AgentTurnFinishedEvent model:
                        attributes["provider"] = model.ProviderKey ?? "";
                        attributes["model"] = model.ModelId ?? "";
                        attributes["outcome"] = model.Outcome.ToString();
                        break;
                }
                return new SpanProjection(
                    group.Key,
                    first.ParentSpanId,
                    SpanName(defining),
                    defining is ToolCallStartEvent ? "client" : "internal",
                    status,
                    startedAt,
                    duration,
                    attributes);
            })
            .OrderBy(static span => span.StartedAt)
            .ThenBy(static span => span.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string SpanName(AgentEvent evt) => evt switch
    {
        MessageTurnStartedEvent => "agent.turn",
        AgentTurnStartedEvent => "agent.iteration",
        ToolCallStartEvent => "agent.function",
        ThreadExecutionStartedEvent => "agent.execution",
        SubAgentInvocationStartedEvent => "agent.subagent",
        _ => evt.GetType().Name.EndsWith("Event", StringComparison.Ordinal)
            ? evt.GetType().Name[..^5]
            : evt.GetType().Name
    };

    private static void WriteTrace(
        Utf8JsonWriter writer, string applicationId, TraceProjection value, bool detail,
        AgentStudioTelemetryTrace? telemetryTrace = null)
    {
        writer.WriteString("id", value.Id);
        writer.WriteString("traceId", value.Id);
        writer.WriteString("name", value.Name);
        writer.WriteString("state", value.State);
        writer.WriteString("startedAt", value.StartedAt);
        writer.WriteNumber("durationMs", value.DurationMilliseconds);
        writer.WriteString("agentId", value.AgentId ?? "");
        writer.WriteString("sessionId", value.SessionId);
        writer.WriteString("threadId", value.ThreadId);
        writer.WriteString("executionId", value.ExecutionId ?? "");
        writer.WriteNumber("spanCount", value.Spans.Count);
        writer.WriteNumber("eventCount", value.SpanEventCount);
        WriteNullableNumber(writer, "inputTokens", value.InputTokens);
        WriteNullableNumber(writer, "outputTokens", value.OutputTokens);
        WriteNullableNumber(writer, "totalTokens", value.TotalTokens);
        WriteNullableString(writer, "error", value.Error);
        writer.WriteString("resourceToken", StudioResourceRouteToken.Encode(
            new StudioAgentTraceResource(applicationId, value.Id)));
        if (!detail) return;
        writer.WriteString("spanSource", telemetryTrace is { Spans.Count: > 0 }
            ? "otel-activity-projection"
            : "canonical-thread-journal");
        writer.WriteStartArray("spans");
        if (telemetryTrace is { Spans.Count: > 0 })
        {
            foreach (AgentStudioTelemetrySpan span in telemetryTrace.Spans)
                WriteTelemetrySpan(writer, span);
        }
        else
        {
            foreach (SpanProjection span in value.Spans)
            {
                writer.WriteStartObject();
                writer.WriteString("id", span.Id);
                WriteNullableString(writer, "parentSpanId", span.ParentSpanId);
                writer.WriteString("name", span.Name);
                writer.WriteString("kind", span.Kind);
                writer.WriteString("status", span.Status);
                writer.WriteString("startedAt", span.StartedAt);
                writer.WriteNumber("durationMs", span.DurationMilliseconds);
                writer.WritePropertyName("attributes");
                WriteStringMap(writer, span.Attributes);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
        writer.WriteStartArray("events");
        foreach (TraceEventProjection item in value.Events)
            WriteTraceEvent(writer, item);
        writer.WriteEndArray();
    }

    private static void WriteTelemetrySpan(Utf8JsonWriter writer, AgentStudioTelemetrySpan span)
    {
        writer.WriteStartObject();
        writer.WriteString("id", span.SpanId);
        WriteNullableString(writer, "parentSpanId", span.ParentSpanId);
        writer.WriteString("name", span.Name);
        writer.WriteString("kind", span.Kind);
        writer.WriteString("status", span.Status);
        writer.WriteString("startedAt", span.StartedAt);
        writer.WriteNumber("durationMs", span.DurationMilliseconds);
        writer.WriteString("source", span.SourceName);
        writer.WriteString("activityTraceId", span.ActivityTraceId);
        writer.WriteString("activitySpanId", span.ActivitySpanId);
        writer.WritePropertyName("attributes");
        WriteStringMap(writer, span.Attributes);
        writer.WriteStartArray("spanEvents");
        foreach (AgentStudioTelemetrySpanEvent spanEvent in span.Events)
        {
            writer.WriteStartObject();
            writer.WriteString("name", spanEvent.Name);
            writer.WriteString("timestamp", spanEvent.Timestamp);
            writer.WritePropertyName("attributes");
            WriteStringMap(writer, spanEvent.Attributes);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStringMap(
        Utf8JsonWriter writer, IReadOnlyDictionary<string, string> values)
    {
        writer.WriteStartObject();
        foreach ((string key, string value) in values)
            writer.WriteString(key, value);
        writer.WriteEndObject();
    }

    private static void WriteTraceEvent(Utf8JsonWriter writer, TraceEventProjection item)
    {
        writer.WriteStartObject();
        writer.WriteString("id", item.Event.EventId);
        writer.WriteString("type", item.Event.GetType().Name);
        writer.WriteString("kind", item.Event.Kind.ToString());
        writer.WriteString("channel", item.Event.Channel.ToString());
        writer.WriteNumber("sequenceNumber", item.SequenceNumber);
        writer.WriteString("occurredAt", item.Event.Timestamp);
        writer.WriteString("threadExecutionId", item.Event.ThreadExecutionId ?? "");
        writer.WriteString("sessionId", item.SessionId);
        writer.WriteString("threadId", item.ThreadId);
        writer.WriteString("traceId", item.Event.TraceId ?? "");
        writer.WriteString("spanId", item.Event.SpanId ?? "");
        writer.WriteString("parentSpanId", item.Event.ParentSpanId ?? "");
        writer.WriteEndObject();
    }

    private static void WriteJournalEvent(
        Utf8JsonWriter writer, TraceEventProjection item)
    {
        AgentEvent evt = item.Event;
        writer.WriteStartObject();
        writer.WriteString("id", evt.EventId);
        writer.WriteString("type", evt.GetType().Name);
        writer.WriteString("kind", evt.Kind.ToString());
        writer.WriteString("channel", evt.Channel.ToString());
        writer.WriteString("occurredAt", evt.Timestamp);
        writer.WriteNumber("sequenceNumber", item.SequenceNumber);
        writer.WriteNumber("generation", item.Generation);
        writer.WriteString("agentId",
            evt switch
            {
                MessageTurnStartedEvent value => value.AgentId,
                MessageTurnFinishedEvent value => value.AgentId,
                MessageTurnErrorEvent value => value.AgentId ?? "",
                ThreadExecutionStartedEvent value => value.AgentId,
                ThreadExecutionFinishedEvent value => value.AgentId,
                _ => evt.Metadata?.AgentId ?? item.DefaultAgentId
            });
        writer.WriteString("source",
            evt is IAgentRequestEvent sourceRequest
                ? sourceRequest.SourceName
                : evt.Metadata?.AgentName ?? "HPD.Agent");
        writer.WriteString("sessionId", item.SessionId);
        writer.WriteString("threadId", item.ThreadId);
        writer.WriteString("threadExecutionId", evt.ThreadExecutionId ?? "");
        writer.WriteString("traceId", evt.TraceId ?? "");
        writer.WriteString("spanId", evt.SpanId ?? "");
        writer.WriteString("parentSpanId", evt.ParentSpanId ?? "");
        writer.WriteBoolean("isError", evt is IErrorEvent);
        writer.WriteString("payloadState", "metadata-only");
        writer.WritePropertyName("attributes");
        writer.WriteStartObject();
        switch (evt)
        {
            case MessageTurnStartedEvent value:
                writer.WriteString("agent.name", value.AgentName);
                writer.WriteString("message.turn.id", value.MessageTurnId);
                writer.WriteString("conversation.id", value.ConversationId);
                break;
            case MessageTurnFinishedEvent value:
                writer.WriteString("agent.name", value.AgentName);
                writer.WriteString("message.turn.id", value.MessageTurnId);
                writer.WriteNumber("duration.ms", value.Duration.TotalMilliseconds);
                if (value.TerminationReason is { Length: > 0 } reason)
                    writer.WriteString("termination.reason", BoundDiagnostic(reason));
                break;
            case MessageTurnErrorEvent value:
                writer.WriteString("message.turn.id", value.MessageTurnId);
                writer.WriteString("error.type", value.ErrorType ?? "");
                writer.WriteString("error.message", BoundDiagnostic(value.ErrorMessage));
                break;
            case AgentTurnStartedEvent value:
                writer.WriteNumber("iteration", value.Iteration);
                break;
            case AgentTurnFinishedEvent value:
                writer.WriteNumber("iteration", value.Iteration);
                writer.WriteString("operation.id", value.OperationId);
                writer.WriteString("provider", value.ProviderKey ?? "");
                writer.WriteString("model", value.ModelId ?? "");
                writer.WriteString("outcome", value.Outcome.ToString());
                break;
            case ToolCallStartEvent value:
                writer.WriteString("tool.name", value.Name);
                writer.WriteString("tool.call_id", value.CallId);
                if (value.ToolHarnessName is { Length: > 0 } harness)
                    writer.WriteString("tool.harness", harness);
                break;
            case ToolCallEndEvent value:
                writer.WriteString("tool.name", value.Name);
                writer.WriteString("tool.call_id", value.CallId);
                break;
            case ToolCallResultEvent value:
                writer.WriteString("tool.call_id", value.CallId);
                if (value.Name is { Length: > 0 } name) writer.WriteString("tool.name", name);
                if (value.ToolHarnessName is { Length: > 0 } resultHarness)
                    writer.WriteString("tool.harness", resultHarness);
                break;
            case ThreadExecutionStartedEvent value:
                writer.WriteString("execution.id", value.ThreadExecutionId);
                break;
            case ThreadExecutionFinishedEvent value:
                writer.WriteString("execution.id", value.ThreadExecutionId);
                writer.WriteString("execution.outcome", value.Outcome.ToString());
                if (value.Error is { } executionError)
                    writer.WriteString("error.message", BoundDiagnostic(executionError.Message));
                break;
            case IAgentRequestEvent requestEvent:
                writer.WriteString("request.id", requestEvent.RequestId);
                break;
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteMeasure(
        Utf8JsonWriter writer, string id, string label, double value, string unit)
    {
        writer.WriteStartObject();
        writer.WriteString("id", id);
        writer.WriteString("label", label);
        writer.WriteNumber("value", value);
        writer.WriteString("unit", unit);
        writer.WriteBoolean("available", true);
        writer.WriteEndObject();
    }

    private static void WriteNullableMeasure(
        Utf8JsonWriter writer, string id, string label, double? value, string unit)
    {
        writer.WriteStartObject();
        writer.WriteString("id", id);
        writer.WriteString("label", label);
        if (value is null) writer.WriteNull("value");
        else writer.WriteNumber("value", value.Value);
        writer.WriteString("unit", unit);
        writer.WriteBoolean("available", value is not null);
        writer.WriteEndObject();
    }

    private async ValueTask<bool> AgentExistsAsync(string agentId, CancellationToken cancellationToken) =>
        services.GetService<IAgentDefinitionService>() is { } agents &&
        await agents.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false) is not null;

    private async ValueTask<bool> SessionExistsAsync(string sessionId, CancellationToken cancellationToken) =>
        services.GetService<IAgentSessionService>() is { } sessions &&
        await sessions.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false) is not null;

    private async ValueTask<bool> ThreadExistsAsync(string sessionId, string threadId, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentThreadService>() is not { } threads) return false;
        AgentServiceResult<ThreadDto> result =
            await threads.GetThreadAsync(sessionId, threadId, cancellationToken).ConfigureAwait(false);
        return result.Status == AgentServiceStatus.Success && result.Value is not null;
    }

    private async ValueTask<bool> ExecutionExistsAsync(StudioAgentExecutionResource execution, CancellationToken cancellationToken)
    {
        if (services.GetService<IAgentThreadService>() is not { } threads ||
            services.GetService<IAgentThreadExecutionService>() is not { } executions) return false;
        AgentServiceResult<ThreadDto> thread = await threads.GetThreadAsync(execution.SessionId, execution.ThreadId, cancellationToken).ConfigureAwait(false);
        if (thread.Status != AgentServiceStatus.Success || thread.Value is null) return false;
        AgentServiceResult<ThreadExecutionDto> value = await executions.GetExecutionAsync(thread.Value.DefaultAgentId,
            execution.SessionId, execution.ThreadId, execution.ThreadExecutionId, cancellationToken).ConfigureAwait(false);
        return value.Status == AgentServiceStatus.Success && value.Value is not null;
    }

    private async ValueTask<bool> HarnessExistsAsync(string harnessId, CancellationToken cancellationToken)
    {
        Dictionary<string, HarnessProjection> catalog =
            await CollectHarnessesAsync(cancellationToken).ConfigureAwait(false);
        return ResolveHarness(catalog, harnessId) is not null;
    }

    private async ValueTask<bool> FunctionExistsAsync(
        StudioAIFunctionResource resource, CancellationToken cancellationToken)
    {
        Dictionary<string, HarnessProjection> catalog =
            await CollectHarnessesAsync(cancellationToken).ConfigureAwait(false);
        HarnessProjection? harness = ResolveHarness(catalog, resource.HarnessId);
        return harness?.Functions.Values.Any(value =>
            StringComparer.Ordinal.Equals(value.Capability.Id.Value, resource.FunctionId) ||
            StringComparer.Ordinal.Equals(value.Capability.Name, resource.FunctionId)) == true;
    }

    private async ValueTask<bool> FunctionActionExistsAsync(
        StudioAIFunctionActionResource resource, CancellationToken cancellationToken)
    {
        Dictionary<string, HarnessProjection> catalog =
            await CollectHarnessesAsync(cancellationToken).ConfigureAwait(false);
        HarnessProjection? harness = ResolveHarness(catalog, resource.HarnessId);
        CapabilityProjection? function = harness?.Functions.Values.FirstOrDefault(value =>
            StringComparer.Ordinal.Equals(value.Capability.Id.Value, resource.FunctionId) ||
            StringComparer.Ordinal.Equals(value.Capability.Name, resource.FunctionId));
        return function?.Capability.Actions.Any(value =>
            StringComparer.Ordinal.Equals(value.Name, resource.ActionId)) == true;
    }

    private static void WriteHarness(
        Utf8JsonWriter writer, string applicationId, HarnessProjection value,
        IServiceProviderIsService? serviceProbe)
    {
        ToolHarnessFactory? factory = value.Factory;
        string[] functionNames = value.Functions.Values.Select(static item => item.Capability.Name)
            .Concat(factory?.FunctionNames ?? [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, int> declaredActions = (factory?.FunctionDeclarations ?? [])
            .GroupBy(static item => item.FunctionName, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key,
                static group => group.Max(item => item.ActionNames.Count), StringComparer.Ordinal);
        var materializedNames = new HashSet<string>(
            value.Functions.Values.Select(static item => item.Capability.Name), StringComparer.Ordinal);
        int actionCount = value.Functions.Values.Sum(item =>
            Math.Max(item.Capability.Actions.Count,
                declaredActions.GetValueOrDefault(item.Capability.Name)));
        actionCount += declaredActions
            .Where(pair => !materializedNames.Contains(pair.Key))
            .Sum(static pair => pair.Value);
        string[] skills = value.Functions.Values
            .Where(static item => item.Capability.Kind is
                HPDCapabilityKind.SkillActivation or
                HPDCapabilityKind.SkillResource or
                HPDCapabilityKind.SkillScript)
            .Select(static item => item.Capability.Name)
            .Concat(factory?.SkillDeclarationNames ?? [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] subAgents = factory?.SubAgentDeclarationNames?.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray() ?? [];
        string[] mcpServers = factory?.McpSourceNames?.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray() ?? [];
        string[] openApiSources = factory?.OpenApiSourceNames?.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray() ?? [];
        string[] workflows = factory?.MultiAgentDeclarationNames?.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray() ?? [];

        writer.WriteString("id", value.Id);
        writer.WriteString("harnessId", value.Id);
        writer.WriteString("name", value.Name);
        writer.WriteString("description", factory?.Description ?? "");
        writer.WriteString("activationIdentity", value.Id);
        writer.WriteString("ownerType", factory?.ToolHarnessType.FullName ?? "");
        writer.WriteString("assembly", factory?.ToolHarnessType.Assembly.GetName().Name ?? "");
        writer.WriteString("origin", factory is null ? "unresolved" : "generated");
        writer.WriteString("availability", factory is null
            ? "unavailable"
            : value.Instantiated ? "available" : "declared");
        writer.WriteString("runtimeState", value.Instantiated ? "materialized" : "not-materialized");
        writer.WriteString("instanceStatus", value.Instantiated ? "generated-instance" : "not-instantiated");
        writer.WriteString("resourceToken", StudioResourceRouteToken.Encode(
            new StudioToolHarnessResource(applicationId, value.Id)));

        WriteStringArray(writer, "agents", value.Agents);
        WriteStringArray(writer, "configuredFunctions", value.ConfiguredFunctions);
        WriteStringArray(writer, "availableFunctions", functionNames);
        WriteStringArray(writer, "skills", skills);
        WriteStringArray(writer, "subAgents", subAgents);
        WriteStringArray(writer, "mcpServers", mcpServers);
        WriteStringArray(writer, "openApiSources", openApiSources);
        WriteStringArray(writer, "multiAgentWorkflows", workflows);
        WriteStringArray(writer, "inheritedHarnessIdentities",
            factory?.InheritedHarnessIdentities ?? []);
        writer.WriteNumber("functionCount", functionNames.Length);
        writer.WriteNumber("actionCount", actionCount);
        writer.WriteNumber("skillCount", skills.Length);
        writer.WriteNumber("subAgentCount", subAgents.Length);
        writer.WriteNumber("mcpServerCount", mcpServers.Length);
        writer.WriteNumber("openApiSourceCount", openApiSources.Length);
        writer.WriteNumber("multiAgentWorkflowCount", workflows.Length);

        writer.WriteStartArray("functions");
        foreach (CapabilityProjection function in value.Functions.Values
                     .OrderBy(static item => item.Capability.Name, StringComparer.Ordinal)
                     .ThenBy(static item => item.Capability.Id.Value, StringComparer.Ordinal))
            WriteFunction(writer, applicationId, value, function);
        writer.WriteEndArray();

        writer.WritePropertyName("collapsing");
        writer.WriteStartObject();
        writer.WriteBoolean("collapsed", factory?.HasDescription == true);
        WriteNullableString(writer, "description", factory?.Description);
        WriteNullableString(writer, "functionResult", factory?.FunctionResult);
        WriteNullableString(writer, "systemPrompt", factory?.SystemPrompt);
        writer.WriteEndObject();

        writer.WritePropertyName("settings");
        writer.WriteStartObject();
        writer.WriteBoolean("available", factory?.Configuration is not null);
        WriteNullableString(writer, "configurationType",
            factory?.Configuration?.ConfigurationType.FullName);
        writer.WriteBoolean("supportsRunOverrides",
            factory?.Configuration?.SupportsRunOverrides == true);
        writer.WriteBoolean("hasValidator", factory?.Configuration?.HasValidator == true);
        if (factory?.Configuration is { } configuration)
        {
            writer.WritePropertyName("schema");
            WriteSettingSchema(writer, configuration.Schema);
            try
            {
                writer.WritePropertyName("defaults");
                configuration.Encode(configuration.CreateDefaults()).WriteTo(writer);
            }
            catch (Exception)
            {
                writer.WriteNull("defaults");
            }
        }
        writer.WriteEndObject();

        writer.WritePropertyName("metadata");
        writer.WriteStartObject();
        writer.WriteBoolean("typed", factory?.MetadataType is not null);
        WriteNullableString(writer, "type", factory?.MetadataType?.FullName);
        writer.WriteEndObject();

        writer.WriteStartArray("dependencies");
        foreach (ToolHarnessConstructorDependencyDescriptor dependency in
                 factory?.ConstructorDependencies ?? [])
        {
            if (factory is null) continue;
            writer.WriteStartObject();
            writer.WriteString("name", dependency.Name);
            writer.WriteString("typeName", TrimGlobalPrefix(dependency.TypeName));
            writer.WriteString("kind", dependency.Kind.ToString());
            writer.WriteBoolean("optional", dependency.IsOptional);
            writer.WriteString("resolutionSource",
                ResolveDependencySource(factory, dependency, serviceProbe));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("middleware");
        foreach (ToolHarnessMiddlewareDescriptor middleware in factory?.EagerMiddleware ?? [])
            WriteMiddleware(writer, middleware, "eager");
        foreach (ToolHarnessMiddlewareDescriptor middleware in factory?.Middleware ?? [])
            WriteMiddleware(writer, middleware, "expansion-gated");
        writer.WriteEndArray();

        WriteStringArray(writer, "diagnostics", value.Diagnostics);
        WriteCapabilityGraph(writer, value, skills, subAgents, mcpServers, openApiSources, workflows);
    }

    private static void WriteFunction(
        Utf8JsonWriter writer, string applicationId, HarnessProjection harness,
        CapabilityProjection value, bool writeContainer = true)
    {
        AgentCapabilityInspection capability = value.Capability;
        if (writeContainer) writer.WriteStartObject();
        writer.WriteString("id", capability.Id.Value);
        writer.WriteString("functionId", capability.Id.Value);
        writer.WriteString("name", capability.Name);
        writer.WriteString("description", capability.Description);
        writer.WriteString("kind", capability.Kind.ToString());
        writer.WriteString("invocationMode", capability.InvocationModePolicy.ToString());
        writer.WriteString("invocationHandling", capability.InvocationModeHandling.ToString());
        writer.WriteString("replayPolicy", capability.ReplayPolicy.ToString());
        WritePermission(writer, capability.Permission);
        WriteJson(writer, "schema", capability.InputSchema);
        if (capability.ReturnSchema is { } returnSchema)
            WriteJson(writer, "returnSchema", returnSchema);
        WriteNullableString(writer, "schemaFingerprint", capability.SchemaFingerprint);
        WriteNullableString(writer, "ownerHarnessIdentity", capability.OwnerHarnessIdentity);
        WriteNullableString(writer, "declaringHarnessIdentity", capability.DeclaringHarnessIdentity);
        WriteNullableString(writer, "declarationMemberName", capability.DeclarationMemberName);
        WriteNullableString(writer, "implementationIdentity", capability.ImplementationIdentity);
        WriteNullableString(writer, "actionArgumentName", capability.ActionArgumentName);
        WriteNullableString(writer, "actionDiscriminator", capability.ActionDiscriminator);
        WriteStringArray(writer, "parentContainerIds",
            capability.ParentContainerIds.Select(static item => item.Value));
        WriteStringArray(writer, "reveals",
            capability.RevealedCapabilityIds.Select(static item => item.Value));
        WriteStringArray(writer, "agents", value.Agents);
        writer.WriteNumber("variantCount", value.VariantCount);
        writer.WriteBoolean("isSubAgent", capability.IsSubAgent);
        WriteNullableString(writer, "executionModel", capability.ExecutionModel);
        writer.WriteString("resourceToken", StudioResourceRouteToken.Encode(
            new StudioAIFunctionResource(applicationId, harness.Id, capability.Id.Value)));
        writer.WriteStartArray("actions");
        foreach (AgentFunctionActionInspection action in capability.Actions)
            WriteAction(writer, applicationId, harness, value, action);
        writer.WriteEndArray();
        if (writeContainer) writer.WriteEndObject();
    }

    private static void WriteAction(
        Utf8JsonWriter writer, string applicationId, HarnessProjection harness,
        CapabilityProjection function, AgentFunctionActionInspection value,
        bool writeContainer = true)
    {
        if (writeContainer) writer.WriteStartObject();
        writer.WriteString("id", value.Name);
        writer.WriteString("actionId", value.Name);
        writer.WriteString("name", value.Name);
        writer.WriteString("description", value.Description);
        writer.WriteString("invocationMode", value.InvocationModePolicy.ToString());
        writer.WriteString("invocationHandling", value.InvocationModeHandling.ToString());
        writer.WriteString("replayPolicy", value.ReplayPolicy.ToString());
        WritePermission(writer, value.Permission);
        WriteJson(writer, "schema", value.InputSchema);
        writer.WriteString("resourceToken", StudioResourceRouteToken.Encode(
            new StudioAIFunctionActionResource(
                applicationId, harness.Id, function.Capability.Id.Value, value.Name)));
        if (writeContainer) writer.WriteEndObject();
    }

    private static void WritePermission(
        Utf8JsonWriter writer, AIFunctionPermissionDeclaration? permission)
    {
        writer.WritePropertyName("permission");
        writer.WriteStartObject();
        writer.WriteString("kind", permission is null
            ? "unavailable"
            : permission.RequiresPermission ? "required" : "not-required");
        writer.WriteBoolean("requiresPermission", permission?.RequiresPermission == true);
        WriteNullableString(writer, "authority", permission?.Authority);
        WriteNullableString(writer, "policyId", permission?.PolicyDescriptorId);
        WriteNullableString(writer, "source", permission?.Source.ToString());
        writer.WriteEndObject();
    }

    private static void WriteCapabilityGraph(
        Utf8JsonWriter writer, HarnessProjection harness,
        IReadOnlyList<string> skills, IReadOnlyList<string> subAgents,
        IReadOnlyList<string> mcpServers, IReadOnlyList<string> openApiSources,
        IReadOnlyList<string> workflows)
    {
        const string harnessNode = "harness:";
        writer.WritePropertyName("capabilityGraph");
        writer.WriteStartObject();
        writer.WriteStartArray("nodes");
        WriteGraphNode(writer, harnessNode + harness.Id, "ToolHarness", harness.Name,
            harness.Factory?.ToolHarnessType.FullName ?? "");
        foreach (CapabilityProjection function in harness.Functions.Values
                     .OrderBy(static item => item.Capability.Name, StringComparer.Ordinal))
        {
            string functionNode = "function:" + function.Capability.Id.Value;
            WriteGraphNode(writer, functionNode, function.Capability.Kind.ToString(),
                function.Capability.Name, function.Capability.Id.Value);
            foreach (AgentFunctionActionInspection action in function.Capability.Actions)
                WriteGraphNode(writer, functionNode + ":action:" + action.Name,
                    "AIFunctionAction", action.Name, action.Description);
        }
        foreach ((string kind, IReadOnlyList<string> names) in new[]
                 {
                     ("Skill", skills),
                     ("SubAgent", subAgents),
                     ("MCP", mcpServers),
                     ("OpenAPI", openApiSources),
                     ("MultiAgent", workflows)
                 })
            foreach (string name in names)
                WriteGraphNode(writer, kind.ToLowerInvariant() + ":" + harness.Id + ":" + name,
                    kind, name, "");
        writer.WriteEndArray();
        writer.WriteStartArray("edges");
        foreach (CapabilityProjection function in harness.Functions.Values)
        {
            string functionNode = "function:" + function.Capability.Id.Value;
            WriteGraphEdge(writer, harnessNode + harness.Id, functionNode, "owns");
            foreach (AgentFunctionActionInspection action in function.Capability.Actions)
                WriteGraphEdge(writer, functionNode,
                    functionNode + ":action:" + action.Name, "contains");
        }
        foreach ((string kind, IReadOnlyList<string> names) in new[]
                 {
                     ("Skill", skills),
                     ("SubAgent", subAgents),
                     ("MCP", mcpServers),
                     ("OpenAPI", openApiSources),
                     ("MultiAgent", workflows)
                 })
            foreach (string name in names)
                WriteGraphEdge(writer, harnessNode + harness.Id,
                    kind.ToLowerInvariant() + ":" + harness.Id + ":" + name, "declares");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteGraphNode(
        Utf8JsonWriter writer, string id, string kind, string label, string description)
    {
        writer.WriteStartObject();
        writer.WriteString("id", id);
        writer.WriteString("kind", kind);
        writer.WriteString("label", label);
        writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteGraphEdge(
        Utf8JsonWriter writer, string source, string target, string relation)
    {
        writer.WriteStartObject();
        writer.WriteString("source", source);
        writer.WriteString("target", target);
        writer.WriteString("relation", relation);
        writer.WriteEndObject();
    }

    private static void WriteMiddleware(
        Utf8JsonWriter writer, ToolHarnessMiddlewareDescriptor value, string activation)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.MiddlewareType.Name);
        writer.WriteString("type", value.MiddlewareType.FullName ?? value.MiddlewareType.Name);
        writer.WriteString("activation", activation);
        writer.WriteString("ownership", value.Ownership.ToString());
        writer.WriteBoolean("usesHarnessConfiguration", value.UsesHarnessConfiguration);
        writer.WriteBoolean("requiresCanonicalWorkspaceIdentity",
            value.RequiresCanonicalWorkspaceIdentity);
        WriteNullableString(writer, "configurationType", value.ConfigurationType?.FullName);
        writer.WriteEndObject();
    }

    private static void WriteSettingSchema(Utf8JsonWriter writer, HarnessSettingSchema schema)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", schema.Kind.ToString());
        writer.WriteBoolean("run", schema.Run);
        writer.WriteBoolean("nullable", schema.Nullable);
        if (schema.Properties is { Count: > 0 })
        {
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            foreach (var pair in schema.Properties.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(pair.Key);
                WriteSettingSchema(writer, pair.Value);
            }
            writer.WriteEndObject();
        }
        if (schema.Element is not null)
        {
            writer.WritePropertyName("element");
            WriteSettingSchema(writer, schema.Element);
        }
        writer.WriteEndObject();
    }

    private static void WriteJson(Utf8JsonWriter writer, string name, JsonElement value)
    {
        writer.WritePropertyName(name);
        if (value.ValueKind == JsonValueKind.Undefined) writer.WriteNullValue();
        else value.WriteTo(writer);
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteNumber(name, value.Value);
    }

    private static double? Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0) return null;
        int index = Math.Clamp((int)Math.Ceiling(percentile * values.Count) - 1, 0, values.Count - 1);
        return values[index];
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (string value in values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static string ResolveDependencySource(
        ToolHarnessFactory factory, ToolHarnessConstructorDependencyDescriptor dependency,
        IServiceProviderIsService? serviceProbe)
    {
        if (dependency.Kind == ToolHarnessConstructorDependencyKind.HarnessConfiguration)
            return "generated-settings-contract";
        if (dependency.Kind == ToolHarnessConstructorDependencyKind.Literal)
            return "generated-literal-default";
        if (factory.DependencyDefaults?.Any(value =>
                value.ServiceType == dependency.ContractType) == true)
            return "owner-local-default";
        return serviceProbe?.IsService(dependency.ContractType) == true
            ? "application-di"
            : dependency.IsOptional ? "optional-unresolved" : "missing";
    }

    private static string TrimGlobalPrefix(string value) =>
        value.StartsWith("global::", StringComparison.Ordinal) ? value[8..] : value;

    private static string BoundDiagnostic(string value)
    {
        string bounded = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return bounded.Length <= 240 ? bounded : bounded[..240];
    }

    private static string SerializeArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var pair in arguments)
            {
                writer.WritePropertyName(pair.Key);
                JsonSerializer.Serialize(writer, pair.Value, HPDJsonContext.Default.Object);
            }
            writer.WriteEndObject();
            writer.Flush();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteThread(Utf8JsonWriter writer, string applicationId, ThreadDto value)
    {
        writer.WriteString("id", value.Id);
        writer.WriteString("sessionId", value.SessionId);
        writer.WriteString("defaultAgentId", value.DefaultAgentId);
        writer.WriteString("name", value.Name);
        writer.WriteString("kind", value.Kind.ToString());
        writer.WriteString("visibility", value.Visibility.ToString());
        writer.WriteString("createdAt", value.CreatedAt);
        writer.WriteString("lastActivity", value.LastActivity);
        writer.WriteNumber("messageCount", value.MessageCount);
        writer.WriteNumber("totalForks", value.TotalForks);
        if (value.SubAgentName is not null) writer.WriteString("subAgentName", value.SubAgentName);
        writer.WriteString("resourceToken", StudioResourceRouteToken.Encode(
            new StudioAgentThreadResource(applicationId, value.SessionId, value.Id)));
    }

    private static void WriteExecution(
        Utf8JsonWriter writer, string applicationId, string sessionId, string threadId, ThreadExecutionDto value)
    {
        writer.WriteString("id", value.ThreadExecutionId);
        writer.WriteString("state", value.Status);
        writer.WriteString("startedAt", value.StartedAt);
        if (value.FinishedAt is not null) writer.WriteString("finishedAt", value.FinishedAt.Value);
        writer.WriteString("resourceToken", StudioResourceRouteToken.Encode(
            new StudioAgentExecutionResource(applicationId, sessionId, threadId, value.ThreadExecutionId)));
    }

    private static StudioFrameworkSurfaceResponse Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
            writer.Flush();
        }
        return StudioFrameworkSurfaceResponse.Create(200, "application/json", buffer.WrittenSpan, MaximumBytes);
    }

    private static void WriteLayer(Utf8JsonWriter writer, string id, string label, string state, string description)
    {
        writer.WriteStartObject();
        writer.WriteString("id", id);
        writer.WriteString("label", label);
        writer.WriteString("state", state);
        writer.WriteString("description", description);
        writer.WriteEndObject();
    }

    private static void WriteCheck(Utf8JsonWriter writer, string id, string label, string state, string summary)
    {
        writer.WriteStartObject();
        writer.WriteString("id", id);
        writer.WriteString("label", label);
        writer.WriteString("state", state);
        writer.WriteString("summary", summary);
        writer.WriteEndObject();
    }

    private static StudioFrameworkSurfaceResponse Unsupported(string code) => Json(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("state", "unsupported");
        writer.WriteString("code", code);
        writer.WriteEndObject();
    });

    private sealed class HarnessProjection(string id, ToolHarnessFactory? factory)
    {
        public string Id { get; } = id;
        public ToolHarnessFactory? Factory { get; } = factory;
        public string Name { get; set; } = factory?.Name ?? id;
        public bool Instantiated { get; set; }
        public HashSet<string> Agents { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ConfiguredFunctions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CapabilityProjection> Functions { get; } =
            new(StringComparer.Ordinal);
        public List<string> Diagnostics { get; } = [];

        public void AddCapability(AgentCapabilityInspection capability, string agentId)
        {
            if (!Functions.TryGetValue(capability.Id.Value, out CapabilityProjection? function))
            {
                function = new CapabilityProjection(capability);
                Functions.Add(capability.Id.Value, function);
            }
            else if (!StringComparer.Ordinal.Equals(
                         function.Capability.SchemaFingerprint, capability.SchemaFingerprint))
            {
                function.VariantCount++;
            }
            function.Agents.Add(agentId);
        }
    }

    private sealed class CapabilityProjection(AgentCapabilityInspection capability)
    {
        public AgentCapabilityInspection Capability { get; } = capability;
        public HashSet<string> Agents { get; } = new(StringComparer.Ordinal);
        public int VariantCount { get; set; } = 1;
    }

    private sealed record TraceQueryResult(
        IReadOnlyList<TraceProjection> Traces,
        bool Truncated);

    private sealed record TraceListFilter(
        string? AgentId,
        string? SessionId,
        string? ThreadId,
        string? State,
        DateTimeOffset? After,
        DateTimeOffset? Before,
        int Offset,
        int Limit);

    private sealed record EventListFilter(
        string? AgentId,
        string? SessionId,
        string? ThreadId,
        string? TraceId,
        string? SpanId,
        string? Type,
        bool ErrorsOnly,
        DateTimeOffset? After,
        DateTimeOffset? Before,
        int Offset,
        int Limit);

    private sealed record MetricsListFilter(
        string? AgentId,
        string? SessionId,
        string? ThreadId,
        string? State,
        DateTimeOffset? After,
        DateTimeOffset? Before);

    private sealed record JournalQueryResult(
        TraceQueryResult Traces,
        IReadOnlyList<TraceEventProjection> Events,
        bool Truncated);

    private sealed record MetricBreakdownProjection(
        string Id,
        int Count,
        int Failed,
        double AverageDurationMilliseconds,
        long TotalTokens);

    private sealed record FunctionMetricBreakdownProjection(
        string Harness,
        string Function,
        int Calls);

    private sealed record TraceEventProjection(
        AgentEvent Event,
        string SessionId,
        string ThreadId,
        string DefaultAgentId,
        long Generation,
        long SequenceNumber);

    private sealed record ExecutionTimeProjection(
        string ExecutionId,
        string SessionId,
        string ThreadId,
        DateTimeOffset StartedAt,
        DateTimeOffset? FinishedAt);

    private sealed record TraceProjection(
        string Id,
        string Name,
        string State,
        DateTimeOffset StartedAt,
        double DurationMilliseconds,
        string? AgentId,
        string SessionId,
        string ThreadId,
        string? ExecutionId,
        long? InputTokens,
        long? OutputTokens,
        long? TotalTokens,
        string? Error,
        int SpanEventCount,
        IReadOnlyList<SpanProjection> Spans,
        IReadOnlyList<TraceEventProjection> Events);

    private sealed record SpanProjection(
        string Id,
        string? ParentSpanId,
        string Name,
        string Kind,
        string Status,
        DateTimeOffset StartedAt,
        double DurationMilliseconds,
        IReadOnlyDictionary<string, string> Attributes);
}
