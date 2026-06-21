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

        var messageId = CreateMessageId(record);
        var existing = await ResolveExistingProjectionAsync(
            thread,
            messageId,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var message = new ChatMessage(ToChatRole(record.Role), record.Text)
        {
            MessageId = messageId
        };

        await _sessionStore.AppendThreadEventAsync(
            thread.SessionId,
            thread.ThreadId,
            ThreadEventFactory.MessageStarted(thread.SessionId, thread.ThreadId, message),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _sessionStore.AppendThreadEventAsync(
            thread.SessionId,
            thread.ThreadId,
            ThreadEventFactory.ContentAdded(
                thread.SessionId,
                thread.ThreadId,
                messageId,
                new TextContent(record.Text)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var completed = ThreadEventFactory.MessageCompleted(thread.SessionId, thread.ThreadId, messageId);
        await _sessionStore.AppendThreadEventAsync(
            thread.SessionId,
            thread.ThreadId,
            completed,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ResolveProjectedEventRefAsync(
            thread,
            completed.EventId,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ThreadProjectedEventRef> ResolveProjectedEventRefAsync(
        ThreadRef thread,
        string? eventId,
        CancellationToken cancellationToken)
    {
        var document = await _sessionStore.LoadThreadDocumentAsync(
            thread.SessionId,
            thread.ThreadId,
            cancellationToken).ConfigureAwait(false);

        var evt = document?.Events.LastOrDefault(e =>
            string.Equals(e.EventId, eventId, StringComparison.Ordinal));

        return evt is null
            ? new ThreadProjectedEventRef(eventId ?? string.Empty, 0)
            : new ThreadProjectedEventRef(evt.EventId ?? string.Empty, evt.SequenceNumber);
    }

    private async ValueTask<ThreadProjectedEventRef?> ResolveExistingProjectionAsync(
        ThreadRef thread,
        string messageId,
        CancellationToken cancellationToken)
    {
        var document = await _sessionStore.LoadThreadDocumentAsync(
            thread.SessionId,
            thread.ThreadId,
            cancellationToken).ConfigureAwait(false);

        var completed = document?.Events.LastOrDefault(e =>
            e is MessageCompletedEvent completedEvent
            && string.Equals(completedEvent.MessageId, messageId, StringComparison.Ordinal));

        return completed is null
            ? null
            : new ThreadProjectedEventRef(completed.EventId ?? string.Empty, completed.SequenceNumber);
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
