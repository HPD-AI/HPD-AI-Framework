namespace HPD.Agent.Adapters.Slack.OAuth;

/// <summary>
/// Persists and retrieves Slack bot tokens per workspace (team ID).
/// Implement this to store tokens in a database, Key Vault, etc.
/// The default registration uses <see cref="InMemorySlackTokenStore"/> (non-persistent).
/// </summary>
public interface ISlackTokenStore
{
    /// <summary>
    /// Stores the bot token for the given team. Called after a successful OAuth callback.
    /// </summary>
    Task SaveAsync(string teamId, string botToken, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the bot token for the given team, or <c>null</c> if not installed.
    /// </summary>
    Task<string?> GetAsync(string teamId, CancellationToken ct = default);
}

/// <summary>
/// In-memory <see cref="ISlackTokenStore"/>. Tokens are lost on restart.
/// Suitable for local dev and testing. Use a durable store in production.
/// </summary>
public sealed class InMemorySlackTokenStore : ISlackTokenStore
{
    private readonly Dictionary<string, string> _tokens = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task SaveAsync(string teamId, string botToken, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { _tokens[teamId] = botToken; }
        finally { _lock.Release(); }
    }

    public async Task<string?> GetAsync(string teamId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _tokens.TryGetValue(teamId, out var t) ? t : null; }
        finally { _lock.Release(); }
    }
}
