using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace HPD.Agent.Providers;

/// <summary>
/// Ephemeral revisioned authorization store for tests, samples, and disposable processes.
/// It is not a durable security boundary.
/// </summary>
public sealed class InMemoryProviderAuthorizationStore : IProviderAuthorizationStore, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<ProviderAuthorizationIdentity, Entry> _entries = [];
    private bool _disposed;

    /// <inheritdoc />
    public ValueTask<ProviderAuthorizationRecord?> LoadAsync(
        ProviderAuthorizationIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ValueTask.FromResult(_entries.TryGetValue(identity, out var entry)
                ? CreateRecord(entry)
                : null);
        }
    }

    /// <inheritdoc />
    public ValueTask<ProviderAuthorizationWriteResult> TrySaveAsync(
        ProviderAuthorizationIdentity identity,
        string? expectedRevision,
        ProviderAuthorizationEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(identity, out var current))
            {
                if (!string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
                    return ValueTask.FromResult<ProviderAuthorizationWriteResult>(
                        new ProviderAuthorizationWriteResult.Conflict(CreateRecord(current)));
            }
            else if (expectedRevision is not null)
            {
                return ValueTask.FromResult<ProviderAuthorizationWriteResult>(
                    new ProviderAuthorizationWriteResult.Conflict(null));
            }

            var revision = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            _entries[identity] = new Entry(
                envelope.SchemaVersion,
                envelope.ProtectedPayload.Value.ToArray(),
                revision);
            return ValueTask.FromResult<ProviderAuthorizationWriteResult>(
                new ProviderAuthorizationWriteResult.Written(revision));
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> TryDeleteAsync(
        ProviderAuthorizationIdentity identity,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(identity, out var current) ||
                !string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
                return ValueTask.FromResult(false);
            _entries.Remove(identity);
            CryptographicOperations.ZeroMemory(current.Payload);
            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            foreach (var entry in _entries.Values)
                CryptographicOperations.ZeroMemory(entry.Payload);
            _entries.Clear();
            return ValueTask.CompletedTask;
        }
    }

    private static ProviderAuthorizationRecord CreateRecord(Entry entry) => new()
    {
        Revision = entry.Revision,
        Envelope = new ProviderAuthorizationEnvelope
        {
            SchemaVersion = entry.SchemaVersion,
            ProtectedPayload = new OwnedProviderProtectedBuffer(entry.Payload)
        }
    };

    private sealed record Entry(string SchemaVersion, byte[] Payload, string Revision);
}

/// <summary>Ephemeral, process-local authorization transaction store.</summary>
public sealed class InMemoryProviderAuthorizationTransactionStore :
    IProviderAuthorizationTransactionStore,
    IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <inheritdoc />
    public ValueTask<string> CreateAsync(
        ProviderAuthorizationTransactionEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.ContainsKey(envelope.TransactionId))
                throw new InvalidOperationException("The authorization transaction identity already exists.");
            var revision = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            _entries.Add(envelope.TransactionId, new Entry(
                envelope.TransactionId,
                envelope.AuthorizationScopeIdentity,
                envelope.ExpiresAt,
                envelope.ProtectedPayload.Value.ToArray(),
                revision));
            return ValueTask.FromResult(revision);
        }
    }

    /// <inheritdoc />
    public ValueTask<ProviderAuthorizationTransactionRecord?> LoadAsync(
        string transactionId,
        string authorizationScopeIdentity,
        CancellationToken cancellationToken = default)
    {
        Validate(transactionId, authorizationScopeIdentity);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(transactionId, out var entry) ||
                !string.Equals(entry.AuthorizationScopeIdentity, authorizationScopeIdentity, StringComparison.Ordinal))
                return ValueTask.FromResult<ProviderAuthorizationTransactionRecord?>(null);
            return ValueTask.FromResult<ProviderAuthorizationTransactionRecord?>(CreateRecord(entry));
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> TrySaveAsync(
        ProviderAuthorizationTransactionEnvelope envelope,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(envelope.TransactionId, out var current) ||
                !string.Equals(current.AuthorizationScopeIdentity, envelope.AuthorizationScopeIdentity, StringComparison.Ordinal) ||
                !string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
                return ValueTask.FromResult(false);

            var revision = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var replacement = new Entry(
                envelope.TransactionId,
                envelope.AuthorizationScopeIdentity,
                envelope.ExpiresAt,
                envelope.ProtectedPayload.Value.ToArray(),
                revision);
            _entries[envelope.TransactionId] = replacement;
            CryptographicOperations.ZeroMemory(current.Payload);
            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> TryConsumeAsync(
        string transactionId,
        string authorizationScopeIdentity,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        Validate(transactionId, authorizationScopeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(transactionId, out var entry) ||
                !string.Equals(entry.AuthorizationScopeIdentity, authorizationScopeIdentity, StringComparison.Ordinal) ||
                !string.Equals(entry.Revision, expectedRevision, StringComparison.Ordinal))
                return ValueTask.FromResult(false);
            _entries.Remove(transactionId);
            CryptographicOperations.ZeroMemory(entry.Payload);
            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc />
    public ValueTask CancelAsync(
        string transactionId,
        string authorizationScopeIdentity,
        CancellationToken cancellationToken = default)
    {
        Validate(transactionId, authorizationScopeIdentity);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(transactionId, out var entry) &&
                string.Equals(entry.AuthorizationScopeIdentity, authorizationScopeIdentity, StringComparison.Ordinal))
            {
                _entries.Remove(transactionId);
                CryptographicOperations.ZeroMemory(entry.Payload);
            }
            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            foreach (var entry in _entries.Values)
                CryptographicOperations.ZeroMemory(entry.Payload);
            _entries.Clear();
            return ValueTask.CompletedTask;
        }
    }

    private static void Validate(string transactionId, string scopeIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeIdentity);
    }

    private static ProviderAuthorizationTransactionRecord CreateRecord(Entry entry) => new()
    {
        Revision = entry.Revision,
        Envelope = new ProviderAuthorizationTransactionEnvelope
        {
            TransactionId = entry.TransactionId,
            AuthorizationScopeIdentity = entry.AuthorizationScopeIdentity,
            ExpiresAt = entry.ExpiresAt,
            ProtectedPayload = new OwnedProviderProtectedBuffer(entry.Payload)
        }
    };

    private sealed record Entry(
        string TransactionId,
        string AuthorizationScopeIdentity,
        DateTimeOffset ExpiresAt,
        byte[] Payload,
        string Revision);
}

internal sealed class OwnedProviderProtectedBuffer : IProviderProtectedBuffer
{
    private byte[]? _value;

    internal OwnedProviderProtectedBuffer(ReadOnlySpan<byte> value) => _value = value.ToArray();

    public ReadOnlyMemory<byte> Value =>
        _value ?? throw new ObjectDisposedException(nameof(OwnedProviderProtectedBuffer));

    public ValueTask DisposeAsync()
    {
        var value = Interlocked.Exchange(ref _value, null);
        if (value is not null)
            CryptographicOperations.ZeroMemory(value);
        return ValueTask.CompletedTask;
    }
}
