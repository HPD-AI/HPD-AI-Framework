namespace HPD.Base.Sqlite;

/// <summary>
/// Coordinates ordinary provider work with exclusive schema replacement. The
/// turnstile prevents new readers from starving a waiting migration.
/// </summary>
internal sealed class SqliteSchemaGenerationGate
{
    private readonly SemaphoreSlim _turnstile = new(1, 1);
    private readonly SemaphoreSlim _roomEmpty = new(1, 1);
    private readonly SemaphoreSlim _readerMutex = new(1, 1);
    private int _readers;

    /// <summary>Executes the acquire shared async operation.</summary>
    public async ValueTask<IAsyncDisposable> AcquireSharedAsync(CancellationToken cancellationToken)
    {
        await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _readerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_readers == 0)
                    await _roomEmpty.WaitAsync(cancellationToken).ConfigureAwait(false);
                checked { _readers++; }
            }
            finally
            {
                _readerMutex.Release();
            }
        }
        finally
        {
            _turnstile.Release();
        }

        return new SharedLease(this);
    }

    /// <summary>Executes the acquire exclusive async operation.</summary>
    public async ValueTask<IAsyncDisposable> AcquireExclusiveAsync(CancellationToken cancellationToken)
    {
        await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _roomEmpty.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new ExclusiveLease(this);
        }
        catch
        {
            _turnstile.Release();
            throw;
        }
    }

    private async ValueTask ReleaseSharedAsync()
    {
        await _readerMutex.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _readers--;
            if (_readers == 0)
                _roomEmpty.Release();
        }
        finally
        {
            _readerMutex.Release();
        }
    }

    private sealed class SharedLease(SqliteSchemaGenerationGate owner) : IAsyncDisposable
    {
        private int _disposed;

        /// <summary>Executes the dispose async operation.</summary>
        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _disposed, 1) == 0
                ? owner.ReleaseSharedAsync()
                : ValueTask.CompletedTask;
    }

    private sealed class ExclusiveLease(SqliteSchemaGenerationGate owner) : IAsyncDisposable
    {
        private int _disposed;

        /// <summary>Executes the dispose async operation.</summary>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner._roomEmpty.Release();
                owner._turnstile.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
