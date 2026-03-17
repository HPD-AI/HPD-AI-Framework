namespace HPDOS.Core.Platform.Resources;

public enum PlatformFileMode { Read, Write, ReadWrite }

/// <summary>
/// File resource with managed handle and exclusive-access locking.
/// Uses PlatformFileMode to avoid collision with System.IO.FileMode.
/// </summary>
public sealed class FileResource : IAsyncDisposable
{
    public string Path           { get; }
    public PlatformFileMode Mode { get; }

    private readonly FileStream _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public FileResource(string path, PlatformFileMode mode, FileStream stream)
    {
        Path = path;
        Mode = mode;
        _stream = stream;
    }

    public async ValueTask<T> UseAsync<T>(Func<FileStream, ValueTask<T>> operation, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lock.WaitAsync(ct);
        try   { return await operation(_stream); }
        finally { _lock.Release(); }
    }

    public async ValueTask UseAsync(Func<FileStream, ValueTask> operation, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lock.WaitAsync(ct);
        try   { await operation(_stream); }
        finally { _lock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _stream.DisposeAsync();
        _lock.Dispose();
    }
}
