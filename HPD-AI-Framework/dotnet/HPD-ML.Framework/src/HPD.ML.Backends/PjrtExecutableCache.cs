namespace HPD.ML.Backends.Pjrt;

internal sealed class PjrtExecutableCache<TKey> : IDisposable
    where TKey : notnull
{
    private readonly PjrtClient _client;
    private readonly Func<TKey, string> _stableHloFactory;
    private readonly Dictionary<TKey, PjrtLoadedExecutable> _executables = [];
    private bool _disposed;

    public PjrtExecutableCache(PjrtClient client, Func<TKey, string> stableHloFactory)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stableHloFactory = stableHloFactory ?? throw new ArgumentNullException(nameof(stableHloFactory));
    }

    public int Count => _executables.Count;

    public PjrtLoadedExecutable GetOrCompile(TKey key)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PjrtExecutableCache<TKey>));

        if (_executables.TryGetValue(key, out var executable))
            return executable;

        executable = _client.CompileMlir(_stableHloFactory(key));
        _executables.Add(key, executable);
        return executable;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var executable in _executables.Values)
            executable.Dispose();
        _executables.Clear();
    }
}
