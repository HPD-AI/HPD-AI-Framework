// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Studio;

/// <summary>
/// Streams bounded live canonical Agent events from the process-wide runtime event hub.
/// The subscription emits event metadata and correlation identities; payload inspection remains a
/// separate authorization capability.
/// </summary>
internal sealed class AgentStudioEventSubscriptionProducer(IServiceProvider services)
    : IStudioSubscriptionProducer
{
    public async IAsyncEnumerable<StudioCanonicalJson> StreamAsync(
        StudioProducerInvocation invocation,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
            !IsEmptyObject(invocation.Request))
            yield break;

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<AgentManager>() is not { } manager)
            yield break;
        var inbox = manager.CreateGlobalEventInbox(EventInboxOptions.LossyTelemetry(4096));
        await using (inbox.ConfigureAwait(false))
        {
            var retained = new Queue<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!cancellationToken.IsCancellationRequested)
            {
                while (inbox.Reader.TryRead(out AgentEvent? evt))
                {
                    if (evt is null || !seen.Add(evt.EventId)) continue;
                    retained.Enqueue(evt.EventId);
                    while (retained.Count > 4096)
                        seen.Remove(retained.Dequeue());
                    yield return Serialize(evt);
                }

                if (!await inbox.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    yield break;
            }
        }
    }

    private async ValueTask<bool> AuthorizedAsync(
        StudioProducerInvocation invocation, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AuthorizationResult result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(invocation.Bootstrap.HttpContext.User, null, "base.studio.resource.inspect")
            .ConfigureAwait(false);
        return result.Succeeded;
    }

    private static bool IsEmptyObject(StudioCanonicalJson request)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray());
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static StudioCanonicalJson Serialize(AgentEvent evt)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("agentId", AgentId(evt));
            writer.WritePropertyName("attributes");
            writer.WriteStartObject();
            if (evt is IAgentRequestEvent request)
                writer.WriteString("request.source", request.SourceName);
            if (evt.ThreadExecutionId is { Length: > 0 } executionId)
                writer.WriteString("thread.execution.id", executionId);
            writer.WriteEndObject();
            writer.WriteString("channel", evt.Channel.ToString());
            writer.WriteString("cursor", evt.EventId);
            writer.WriteString("eventId", evt.EventId);
            writer.WriteBoolean("isError", evt is IErrorEvent);
            writer.WriteString("kind", "event");
            writer.WriteString("level", evt is IErrorEvent ? "error" : "information");
            writer.WriteString("occurredAt", evt.Timestamp);
            WriteNullableString(writer, "parentSpanId", evt.ParentSpanId);
            writer.WriteNumber("sequenceNumber", evt.ThreadSequenceNumber);
            writer.WriteString("sessionId", evt.SessionId ?? "");
            writer.WriteString("source",
                evt is IAgentRequestEvent sourceRequest
                    ? sourceRequest.SourceName
                    : evt.Metadata?.AgentName ?? "HPD.Agent");
            WriteNullableString(writer, "spanId", evt.SpanId);
            writer.WriteString("threadId", evt.ThreadId ?? "");
            writer.WriteString("traceId", evt.TraceId ?? "");
            writer.WriteString("type", evt.GetType().Name);
            writer.WriteString("eventKind", evt.Kind.ToString());
            writer.WriteEndObject();
        }
        return StudioCanonicalJson.Create(buffer.WrittenSpan, 16_384);
    }

    private static string AgentId(AgentEvent evt) => evt switch
    {
        MessageTurnStartedEvent value => value.AgentId,
        MessageTurnFinishedEvent value => value.AgentId,
        MessageTurnErrorEvent value => value.AgentId ?? "",
        ThreadExecutionStartedEvent value => value.AgentId,
        ThreadExecutionFinishedEvent value => value.AgentId,
        _ => evt.Metadata?.AgentId ?? ""
    };

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }
}
