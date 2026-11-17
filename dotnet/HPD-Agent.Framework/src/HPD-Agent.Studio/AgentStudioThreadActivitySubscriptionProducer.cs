using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using HPD.Agent;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Studio;

/// <summary>
/// Emits bounded invalidation cursors from the canonical thread journal and live runtime.
/// Consumers re-read the regular scoped activity projection.
/// </summary>
internal sealed class AgentStudioThreadActivitySubscriptionProducer(IServiceProvider services)
    : IStudioSubscriptionProducer
{
    public async IAsyncEnumerable<StudioCanonicalJson> StreamAsync(
        StudioProducerInvocation invocation,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!await AuthorizedAsync(invocation, cancellationToken).ConfigureAwait(false) ||
            !TryRead(invocation.Request, out string? applicationId, out string? agentId, out string? sessionId,
                out string? threadId, out string? targetChecksum) ||
            applicationId is null || agentId is null || sessionId is null || threadId is null ||
            targetChecksum is null ||
            !StringComparer.Ordinal.Equals(applicationId, invocation.Bootstrap.ApplicationGraph.ApplicationId) ||
            !StudioSha256.FixedTimeEquals(
                new StudioAgentResource(applicationId, agentId).AuthorityChecksum,
                StudioSha256.FromDigest(Convert.FromHexString(targetChecksum))))
            yield break;

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<SessionManager>() is not { Store: var store } ||
            scope.ServiceProvider.GetService<IAgentThreadService>() is not { } threads ||
            scope.ServiceProvider.GetService<IAgentStreamingService>() is not { } streaming)
            yield break;
        AgentServiceResult<ThreadDto> descriptor = await threads.GetThreadAsync(
            sessionId, threadId, cancellationToken).ConfigureAwait(false);
        if (descriptor.Status != AgentServiceStatus.Success || descriptor.Value is null)
            yield break;
        bool owned = StringComparer.Ordinal.Equals(descriptor.Value.DefaultAgentId, agentId);
        if (!owned && descriptor.Value.ForkedFrom is { } sourceThreadId)
        {
            AgentServiceResult<ThreadDto> source = await threads.GetThreadAsync(
                sessionId, sourceThreadId, cancellationToken).ConfigureAwait(false);
            owned = source.Status == AgentServiceStatus.Success && source.Value is not null &&
                StringComparer.Ordinal.Equals(source.Value.DefaultAgentId, agentId);
        }
        if (!owned) yield break;

        AgentServiceResult<ThreadEventObservationLease> observed = await streaming.ObserveThreadEventsAsync(
            agentId, new ThreadKey(sessionId, threadId), AgentEventHierarchy.ExactThread,
            cancellationToken).ConfigureAwait(false);
        if (observed.Status != AgentServiceStatus.Success || observed.Value is null)
            yield break;
        await using ThreadEventObservationLease lease = observed.Value;
        ThreadEventHead? head = await store.GetThreadEventHeadAsync(
            new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false);
        if (head is null) yield break;
        long lastSequence = head.ThreadSequenceNumber;
        yield return Result(head.Generation, lastSequence, threadId);

        while (!cancellationToken.IsCancellationRequested)
        {
            while (lease.LiveEvents.Reader.TryRead(out AgentEventDelivery? delivery))
            {
                if (delivery is null) continue;
                AgentEvent item = delivery.Event;
                if (!StringComparer.Ordinal.Equals(item.SessionId, sessionId) ||
                    !StringComparer.Ordinal.Equals(item.ThreadId, threadId) ||
                    item.ThreadSequenceNumber <= lastSequence)
                    continue;
                ThreadEventHead? current = await store.GetThreadEventHeadAsync(
                    new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false);
                if (current is null) yield break;
                lastSequence = item.ThreadSequenceNumber;
                yield return Result(current.Generation, lastSequence, threadId);
            }

            if (!await lease.LiveEvents.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                yield break;
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

    private static bool TryRead(StudioCanonicalJson request, out string? applicationId, out string? agentId,
        out string? sessionId, out string? threadId, out string? targetChecksum)
    {
        applicationId = null;
        agentId = null;
        sessionId = null;
        threadId = null;
        targetChecksum = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.EnumerateObject().Select(static value => value.Name)
                    .SequenceEqual(["sessionId", "target", "threadId"], StringComparer.Ordinal))
                return false;
            JsonElement target = root.GetProperty("target");
            if (target.ValueKind != JsonValueKind.Object ||
                !target.EnumerateObject().Select(static value => value.Name)
                    .SequenceEqual(["agentId", "applicationId", "authorityChecksum", "kind"], StringComparer.Ordinal) ||
                !StringComparer.Ordinal.Equals(target.GetProperty("kind").GetString(), "agent"))
                return false;
            agentId = target.GetProperty("agentId").GetString();
            applicationId = target.GetProperty("applicationId").GetString();
            sessionId = root.GetProperty("sessionId").GetString();
            threadId = root.GetProperty("threadId").GetString();
            targetChecksum = target.GetProperty("authorityChecksum").GetString();
            return !string.IsNullOrWhiteSpace(applicationId) &&
                !string.IsNullOrWhiteSpace(agentId) && !string.IsNullOrWhiteSpace(sessionId) &&
                !string.IsNullOrWhiteSpace(threadId) && targetChecksum is { Length: 64 };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static StudioCanonicalJson Result(long generation, long sequenceNumber, string threadId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("generation", generation);
            writer.WriteString("kind", "invalidation");
            writer.WriteNumber("sequenceNumber", sequenceNumber);
            writer.WriteString("threadId", threadId);
            writer.WriteEndObject();
        }
        return StudioCanonicalJson.Create(buffer.WrittenSpan, 16_384);
    }
}
