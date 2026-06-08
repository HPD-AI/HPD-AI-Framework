namespace HPD.Agent.Bots;

/// <summary>
/// Transport-neutral bot adapter entry point.
/// </summary>
public interface IBotAdapter
{
    /// <summary>Stable adapter name, such as <c>slack</c> or <c>telegram</c>.</summary>
    string Name { get; }

    /// <summary>Handles one inbound platform event envelope.</summary>
    Task<BotAdapterResponse> HandleAsync(
        BotInboundEnvelope envelope,
        CancellationToken cancellationToken = default);
}
