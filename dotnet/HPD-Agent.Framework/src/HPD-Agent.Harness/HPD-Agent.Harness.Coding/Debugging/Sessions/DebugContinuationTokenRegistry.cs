namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugContinuationTokenContext(
    string AgentRuntimeRegistrationId,
    string DebugTreeId,
    string DebugSessionId,
    string QueryKind,
    string QueryIdentity,
    long Generation);

internal sealed record DebugContinuationState(long AdapterOffset, object? State = null);

/// <summary>Tree-owned opaque continuation state. Tokens contain no adapter IDs or caller claims.</summary>
internal sealed class DebugContinuationTokenRegistry
{
    internal const int MaximumTokens = 128;
    internal static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _lifetime;

    public DebugContinuationTokenRegistry(Func<DateTimeOffset>? clock = null, TimeSpan? lifetime = null)
    {
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        _lifetime = lifetime ?? DefaultLifetime;
        if (_lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
    }

    public int Count { get { lock (_gate) return _entries.Count; } }

    public string Create(DebugContinuationTokenContext context, DebugContinuationState state)
    {
        Validate(context);
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            RemoveExpiredLocked(_clock());
            while (_entries.Count >= MaximumTokens)
            {
                var oldest = _entries.MinBy(pair => pair.Value.CreatedAt).Key;
                _entries.Remove(oldest);
            }
            var token = Guid.NewGuid().ToString("N");
            _entries.Add(token, new(context, state, _clock()));
            return token;
        }
    }

    public DebugContinuationState Resolve(string token, DebugContinuationTokenContext expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Validate(expected);
        lock (_gate)
        {
            var now = _clock();
            RemoveExpiredLocked(now);
            if (!_entries.TryGetValue(token, out var entry))
                throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceExpired,
                    "The debugger continuation is unknown or expired.");
            if (!SameOwner(entry.Context, expected))
                throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceOwnerMismatch,
                    "The debugger continuation belongs to another runtime, tree, or protocol session.");
            if (!string.Equals(entry.Context.QueryKind, expected.QueryKind, StringComparison.Ordinal) ||
                !string.Equals(entry.Context.QueryIdentity, expected.QueryIdentity, StringComparison.Ordinal))
                throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments,
                    "The debugger continuation does not match this query.");
            if (entry.Context.Generation != expected.Generation)
            {
                _entries.Remove(token);
                throw new DebugSemanticException(DebugSemanticFailureReason.ReferenceExpired,
                    "The debugger continuation expired when projected state changed.");
            }
            return entry.State;
        }
    }

    public int Revoke(string debugSessionId, string? queryKind = null, long? olderThanGeneration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(debugSessionId);
        lock (_gate)
        {
            var matches = _entries.Where(pair =>
                string.Equals(pair.Value.Context.DebugSessionId, debugSessionId, StringComparison.Ordinal) &&
                (queryKind is null || string.Equals(pair.Value.Context.QueryKind, queryKind, StringComparison.Ordinal)) &&
                (olderThanGeneration is null || pair.Value.Context.Generation < olderThanGeneration)).Select(x => x.Key).ToArray();
            foreach (var token in matches) _entries.Remove(token);
            return matches.Length;
        }
    }

    public void Clear() { lock (_gate) _entries.Clear(); }

    private void RemoveExpiredLocked(DateTimeOffset now)
    {
        foreach (var token in _entries.Where(pair => now - pair.Value.CreatedAt >= _lifetime).Select(x => x.Key).ToArray())
            _entries.Remove(token);
    }

    private static bool SameOwner(DebugContinuationTokenContext left, DebugContinuationTokenContext right)
        => string.Equals(left.AgentRuntimeRegistrationId, right.AgentRuntimeRegistrationId, StringComparison.Ordinal)
        && string.Equals(left.DebugTreeId, right.DebugTreeId, StringComparison.Ordinal)
        && string.Equals(left.DebugSessionId, right.DebugSessionId, StringComparison.Ordinal);

    private static void Validate(DebugContinuationTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.AgentRuntimeRegistrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.DebugTreeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.DebugSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.QueryKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.QueryIdentity);
        if (context.Generation < 0) throw new ArgumentOutOfRangeException(nameof(context.Generation));
    }

    private sealed record Entry(DebugContinuationTokenContext Context, DebugContinuationState State, DateTimeOffset CreatedAt);
}
