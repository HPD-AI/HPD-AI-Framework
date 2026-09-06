using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Encodes a complete semantic thread as a new canonical journal.</summary>
public static class ThreadJournalEncoder
{
    public static IReadOnlyList<AgentEvent> Encode(
        Thread thread,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<AgentEvent>? structuralEvents = null)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(messages);

        var events = new List<AgentEvent> { ThreadEventFactory.ThreadCreated(thread) };
        if (structuralEvents is not null)
            events.AddRange(structuralEvents);

        foreach (var message in messages)
        {
            events.AddRange(ThreadMessageEventConverter.ToThreadEvents(
                thread.SessionId,
                thread.Id,
                message,
                GetTurnId(message)));
        }

        if (thread.MiddlewareState.Count > 0)
            events.Add(ThreadEventFactory.ThreadMiddlewareStateCommitted(thread.SessionId, thread.Id, thread.MiddlewareState));
        events.AddRange(Planning.PlanJournalSnapshots.Create(thread));
        return events;
    }

    private static string? GetTurnId(ChatMessage message) =>
        message.AdditionalProperties?.TryGetValue<string>("hpd.messageTurnId", out var turnId) == true
            ? turnId
            : null;
}
