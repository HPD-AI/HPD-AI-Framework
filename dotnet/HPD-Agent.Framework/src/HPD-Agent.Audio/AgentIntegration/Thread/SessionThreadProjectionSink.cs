using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Ledger;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Thread;

/// <summary>
/// Projects committed audio turns into HPD Agent's event-sourced session thread store.
/// </summary>
public sealed class SessionThreadProjectionSink : IThreadProjectionSink
{
    private readonly ISessionStore _sessionStore;

    public SessionThreadProjectionSink(ISessionStore sessionStore)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    }

    public async ValueTask<ThreadProjectedEventRef> ProjectAsync(
        ThreadRef thread,
        ThreadProjectionRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(record);

        await EnsureThreadExistsAsync(thread, cancellationToken).ConfigureAwait(false);

        var messageId = CreateMessageId(record);
        var existing = await ResolveExistingProjectionAsync(
            thread,
            messageId,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var role = ToChatRole(record.Role);

        await _sessionStore.AppendThreadEventAsync(
            thread.SessionId,
            thread.ThreadId,
            ThreadEventFactory.TextMessageStarted(
                thread.SessionId,
                thread.ThreadId,
                null,
                messageId,
                role.Value,
                0),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(record.Text))
        {
            await _sessionStore.AppendThreadEventAsync(
                thread.SessionId,
                thread.ThreadId,
                ThreadEventFactory.TextDelta(
                    thread.SessionId,
                    thread.ThreadId,
                    null,
                    messageId,
                    record.Text,
                    0),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var completed = ThreadEventFactory.TextMessageCompleted(
            thread.SessionId,
            thread.ThreadId,
            null,
            messageId,
            0);
        var committed = await _sessionStore.AppendThreadEventAsync(
            thread.SessionId,
            thread.ThreadId,
            completed,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ThreadProjectedEventRef(
            committed.EventId,
            committed.ThreadSequenceNumber);
    }

    private async ValueTask EnsureThreadExistsAsync(
        ThreadRef thread,
        CancellationToken cancellationToken)
    {
        var key = new ThreadKey(thread.SessionId, thread.ThreadId);
        if (await _sessionStore.GetThreadAsync(key, cancellationToken).ConfigureAwait(false) is not null)
            return;

        var created = new ThreadCreatedEvent(
            thread.AgentId,
            Name: null,
            Description: null,
            Tags: null,
            ThreadMetadata: null,
            CreatedAt: DateTime.UtcNow)
        {
            SessionId = thread.SessionId,
            ThreadId = thread.ThreadId
        };
        await _sessionStore.AppendThreadEventAsync(
            thread.SessionId,
            thread.ThreadId,
            created,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ThreadProjectedEventRef?> ResolveExistingProjectionAsync(
        ThreadRef thread,
        string messageId,
        CancellationToken cancellationToken)
    {
        var key = new ThreadKey(thread.SessionId, thread.ThreadId);
        var descriptor = await _sessionStore.GetThreadAsync(key, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
            return null;

        TextMessageEndEvent? completed = null;
        await foreach (var batch in _sessionStore.ReadThreadEventsAsync(
            key,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(descriptor.Generation)),
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var candidate in batch.Events.OfType<TextMessageEndEvent>())
            {
                if (string.Equals(candidate.MessageId, messageId, StringComparison.Ordinal))
                    completed = candidate;
            }
        }

        return completed is null
            ? null
            : new ThreadProjectedEventRef(completed.EventId, completed.ThreadSequenceNumber);
    }

    private static string CreateMessageId(ThreadProjectionRecord record)
    {
        if (record.Kind is ThreadProjectionKind.AssistantOutput &&
            record.OutputFlowId is { } outputFlowId &&
            !string.IsNullOrWhiteSpace(outputFlowId.Value))
        {
            return $"audio-output-{outputFlowId.Value}";
        }

        var turnId = string.IsNullOrWhiteSpace(record.TurnId.Value)
            ? Guid.NewGuid().ToString("N")
            : record.TurnId.Value;

        return $"audio-turn-{turnId}";
    }

    private static ChatRole ToChatRole(ThreadProjectionRole role)
    {
        return role is ThreadProjectionRole.Assistant
            ? ChatRole.Assistant
            : ChatRole.User;
    }
}
