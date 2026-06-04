using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Ledger;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Branch;

/// <summary>
/// Projects committed audio turns into HPD Agent's event-sourced session branch store.
/// </summary>
public sealed class SessionBranchProjectionSink : IBranchProjectionSink
{
    private readonly ISessionRepository _sessionRepository;

    public SessionBranchProjectionSink(ISessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
    }

    public async ValueTask<BranchProjectedEventRef> ProjectAsync(
        BranchRef branch,
        BranchProjectionRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(record);

        var messageId = CreateMessageId(record);
        var existing = await ResolveExistingProjectionAsync(
            branch,
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

        await _sessionRepository.AppendBranchEventAsync(
            branch.SessionId,
            branch.BranchId,
            BranchEventFactory.MessageStarted(branch.SessionId, branch.BranchId, message),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _sessionRepository.AppendBranchEventAsync(
            branch.SessionId,
            branch.BranchId,
            BranchEventFactory.ContentAdded(
                branch.SessionId,
                branch.BranchId,
                messageId,
                new TextContent(record.Text)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var completed = BranchEventFactory.MessageCompleted(branch.SessionId, branch.BranchId, messageId);
        await _sessionRepository.AppendBranchEventAsync(
            branch.SessionId,
            branch.BranchId,
            completed,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await ResolveProjectedEventRefAsync(
            branch,
            completed.EventId,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<BranchProjectedEventRef> ResolveProjectedEventRefAsync(
        BranchRef branch,
        string? eventId,
        CancellationToken cancellationToken)
    {
        var document = await _sessionRepository.LoadBranchDocumentAsync(
            branch.SessionId,
            branch.BranchId,
            cancellationToken).ConfigureAwait(false);

        var evt = document?.Events.LastOrDefault(e =>
            string.Equals(e.EventId, eventId, StringComparison.Ordinal));

        return evt is null
            ? new BranchProjectedEventRef(eventId ?? string.Empty, 0)
            : new BranchProjectedEventRef(evt.EventId ?? string.Empty, evt.SequenceNumber);
    }

    private async ValueTask<BranchProjectedEventRef?> ResolveExistingProjectionAsync(
        BranchRef branch,
        string messageId,
        CancellationToken cancellationToken)
    {
        var document = await _sessionRepository.LoadBranchDocumentAsync(
            branch.SessionId,
            branch.BranchId,
            cancellationToken).ConfigureAwait(false);

        var completed = document?.Events.LastOrDefault(e =>
            e is MessageCompletedEvent completedEvent
            && string.Equals(completedEvent.MessageId, messageId, StringComparison.Ordinal));

        return completed is null
            ? null
            : new BranchProjectedEventRef(completed.EventId ?? string.Empty, completed.SequenceNumber);
    }

    private static string CreateMessageId(BranchProjectionRecord record)
    {
        if (record.Kind is BranchProjectionKind.AssistantOutput &&
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

    private static ChatRole ToChatRole(BranchProjectionRole role)
    {
        return role is BranchProjectionRole.Assistant
            ? ChatRole.Assistant
            : ChatRole.User;
    }
}
