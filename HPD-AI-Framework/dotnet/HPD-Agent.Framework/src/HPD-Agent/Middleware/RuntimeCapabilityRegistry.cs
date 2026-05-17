using System.Collections.Concurrent;

namespace HPD.Agent.Middleware;

/// <summary>
/// Registry for services created for a single agent runtime or turn.
/// </summary>
public interface IRuntimeCapabilityRegistry
{
    bool IsSealed { get; }

    void Set<TCapability>(TCapability capability)
        where TCapability : notnull;

    bool TryGet<TCapability>(out TCapability capability)
        where TCapability : notnull;

    TCapability GetRequired<TCapability>()
        where TCapability : notnull;

    void Seal();
}

/// <summary>
/// Thread-safe runtime capability registry keyed by capability type.
/// </summary>
public sealed class RuntimeCapabilityRegistry : IRuntimeCapabilityRegistry
{
    private readonly ConcurrentDictionary<Type, object> _capabilities = new();
    private int _sealed;

    public bool IsSealed => Volatile.Read(ref _sealed) == 1;

    public void Set<TCapability>(TCapability capability)
        where TCapability : notnull
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (IsSealed)
        {
            throw new InvalidOperationException(
                $"Runtime capability registry is sealed and cannot register '{typeof(TCapability).FullName}'.");
        }

        _capabilities[typeof(TCapability)] = capability;
    }

    public bool TryGet<TCapability>(out TCapability capability)
        where TCapability : notnull
    {
        if (_capabilities.TryGetValue(typeof(TCapability), out var value) &&
            value is TCapability typed)
        {
            capability = typed;
            return true;
        }

        capability = default!;
        return false;
    }

    public TCapability GetRequired<TCapability>()
        where TCapability : notnull
    {
        if (TryGet<TCapability>(out var capability))
            return capability;

        throw new InvalidOperationException(
            $"Runtime capability '{typeof(TCapability).FullName}' is not available.");
    }

    public void Seal() => Interlocked.Exchange(ref _sealed, 1);
}
