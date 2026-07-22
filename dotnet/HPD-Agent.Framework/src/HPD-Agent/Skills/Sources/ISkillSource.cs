namespace HPD.Agent;

/// <summary>Discovers runtime skills owned by one registered tool harness.</summary>
public interface ISkillSource
{
    /// <summary>Returns a complete immutable view of the source's current skills.</summary>
    ValueTask<IReadOnlyList<Skill>> GetSkillsAsync(
        SkillSourceContext context,
        CancellationToken cancellationToken);
}

/// <summary>A skill source capable of emitting invalidation hints.</summary>
public interface IWatchableSkillSource : ISkillSource
{
    /// <summary>Watches for changes that require a complete source reread.</summary>
    IAsyncEnumerable<SkillSourceChange> WatchAsync(
        SkillSourceContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Context supplied to a harness-bound skill source.</summary>
public sealed record SkillSourceContext(
    string AgentName,
    string OwnerToolHarnessName,
    string? SessionId,
    IServiceProvider? Services);

/// <summary>Describes a source invalidation hint.</summary>
public sealed record SkillSourceChange(
    string? SkillId,
    SkillSourceChangeKind Kind,
    DateTimeOffset ObservedAt);

/// <summary>Classifies source invalidation hints.</summary>
public enum SkillSourceChangeKind
{
    /// <summary>A definition was added.</summary>
    Added,
    /// <summary>A definition or its content changed.</summary>
    Updated,
    /// <summary>A definition was removed.</summary>
    Deleted,
    /// <summary>The source requires full reconciliation.</summary>
    Reset
}

/// <summary>A mutable in-memory source useful for hosts and tests.</summary>
public sealed class InMemorySkillSource : IWatchableSkillSource
{
    private IReadOnlyList<Skill> _skills;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        Guid,
        System.Threading.Channels.Channel<SkillSourceChange>> _watchers = new();

    /// <summary>Initializes the source with an optional skill snapshot.</summary>
    public InMemorySkillSource(IEnumerable<Skill>? skills = null)
        => _skills = skills?.ToArray() ?? Array.Empty<Skill>();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Skill>> GetSkillsAsync(
        SkillSourceContext context,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(Volatile.Read(ref _skills));

    /// <summary>Atomically replaces the source snapshot and emits an invalidation hint.</summary>
    public void Replace(IEnumerable<Skill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        Volatile.Write(ref _skills, skills.ToArray());
        var change = new SkillSourceChange(null, SkillSourceChangeKind.Reset, DateTimeOffset.UtcNow);
        foreach (var watcher in _watchers.Values)
            watcher.Writer.TryWrite(change);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SkillSourceChange> WatchAsync(
        SkillSourceContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var channel = System.Threading.Channels.Channel.CreateBounded<SkillSourceChange>(
            new System.Threading.Channels.BoundedChannelOptions(16)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        _watchers[id] = channel;
        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return change;
        }
        finally
        {
            _watchers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}
