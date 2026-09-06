using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Durable background received by a fresh child; source events are not imported.</summary>
[Serialization.DurableEvent]
[Serialization.EventType("SUBAGENT_CONTEXT_RECEIVED")]
public sealed record SubAgentContextReceivedEvent(
    string MessageId,
    string Text,
    ThreadKey Source,
    ThreadJournalCursor SourceCursor,
    int FormatVersion = 1) : AgentEvent
{
    internal ChatMessage ToMessage()
    {
        if (FormatVersion != 1 || string.IsNullOrWhiteSpace(MessageId) || string.IsNullOrWhiteSpace(Text))
            throw new InvalidOperationException("subagent_context_invalid");
        return new(ChatRole.User, "[Parent conversation background; the delegated assignment follows separately]\n" + Text)
        {
            MessageId = MessageId,
            CreatedAt = Timestamp
        };
    }
}

