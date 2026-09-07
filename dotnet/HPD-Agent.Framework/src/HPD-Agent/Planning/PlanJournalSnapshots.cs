using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Agent.Planning;

/// <summary>Produces durable plan facts when a journal is seeded from thread state.</summary>
internal static class PlanJournalSnapshots
{
    internal static IReadOnlyList<AgentEvent> Create(Thread thread)
    {
        if (!thread.MiddlewareState.TryGetValue(typeof(PlanModePersistentStateData).FullName!, out var json)) return [];
        var state = JsonSerializer.Deserialize(json, SessionJsonContext.Combined.PlanModePersistentStateData)
            ?? throw new JsonException("Invalid persisted plan state.");
        return state.Plans.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            var plan = pair.Value;
            var identity = $"{thread.SessionId}:{thread.Id}:{pair.Key}:{json}";
            return (AgentEvent)new PlanUpdatedEvent(plan.Id, pair.Key, PlanUpdateType.Snapshot, plan,
                "Restored current plan", new DateTimeOffset(DateTime.SpecifyKind(plan.CompletedAt ?? plan.CreatedAt, DateTimeKind.Utc)))
            {
                SessionId = thread.SessionId, ThreadId = thread.Id,
                EventId = "plan-snapshot-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            };
        }).ToArray();
    }
}
