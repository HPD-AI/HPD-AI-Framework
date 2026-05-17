namespace HPD.Sandbox.Local.Platforms.Linux;

internal sealed class BwrapMountPointCleaner
{
    private readonly object _gate = new();
    private readonly HashSet<string> _createdPaths = new(StringComparer.Ordinal);
    private int _activeInvocations;
    private bool _cleanupRequested;

    public IDisposable BeginInvocation()
    {
        lock (_gate)
            _activeInvocations++;

        return new InvocationLease(this);
    }

    public void Track(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                    _createdPaths.Add(path);
            }
        }
    }

    public void CleanupWhenIdle()
    {
        string[] paths;
        lock (_gate)
        {
            if (_activeInvocations > 0)
            {
                _cleanupRequested = true;
                return;
            }

            paths = DrainPaths();
        }

        RemoveCreatedEmptyPaths(paths);
    }

    public void ForceCleanup()
    {
        string[] paths;
        lock (_gate)
        {
            _cleanupRequested = false;
            paths = DrainPaths();
        }

        RemoveCreatedEmptyPaths(paths);
    }

    internal int TrackedPathCount
    {
        get
        {
            lock (_gate)
                return _createdPaths.Count;
        }
    }

    private void EndInvocation()
    {
        string[]? paths = null;
        lock (_gate)
        {
            if (_activeInvocations > 0)
                _activeInvocations--;

            if (_activeInvocations == 0 && _cleanupRequested)
            {
                _cleanupRequested = false;
                paths = DrainPaths();
            }
        }

        if (paths is not null)
            RemoveCreatedEmptyPaths(paths);
    }

    private string[] DrainPaths()
    {
        var paths = _createdPaths.ToArray();
        _createdPaths.Clear();
        return paths;
    }

    private static void RemoveCreatedEmptyPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.OrderByDescending(path => path.Length))
        {
            try
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    if (info.Length == 0)
                        File.Delete(path);
                }
                else if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                {
                    Directory.Delete(path, recursive: false);
                }
            }
            catch
            {
                // Best effort. A still-running bwrap process may hold the
                // mount source, or another process may have already removed it.
            }
        }
    }

    private sealed class InvocationLease(BwrapMountPointCleaner owner) : IDisposable
    {
        private BwrapMountPointCleaner? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndInvocation();
        }
    }
}
