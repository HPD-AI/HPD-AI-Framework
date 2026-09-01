using System.Security.Cryptography;

namespace HPD.Agent.Providers;

/// <summary>
/// Owns process-local literal provider secrets registered during agent construction.
/// </summary>
public sealed class ProviderRuntimeSecretRegistry : IProviderRuntimeSecretRegistry, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, char[]> _secrets = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Copies a caller-provided secret into an owned, clearable runtime registration.
    /// </summary>
    /// <param name="value">The secret characters to copy.</param>
    /// <returns>An opaque registration name suitable for <see cref="ExplicitApiKeyProviderAuthentication"/>.</returns>
    public string Register(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            throw new ArgumentException("A provider secret cannot be empty.", nameof(value));

        var owned = value.ToArray();
        lock (_sync)
        {
            if (_disposed)
            {
                CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(owned.AsSpan()));
                throw new ObjectDisposedException(nameof(ProviderRuntimeSecretRegistry));
            }

            var name = $"runtime-secret:{Guid.NewGuid():N}";
            _secrets.Add(name, owned);
            return name;
        }
    }

    /// <inheritdoc />
    public IProviderSecretBuffer Acquire(string registrationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationName);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_secrets.TryGetValue(registrationName, out var value))
                throw new KeyNotFoundException("The process-local provider secret registration is unavailable.");
            return new OwnedProviderSecretBuffer(value);
        }
    }

    /// <summary>Clears all owned secret storage and invalidates every registration.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
                return ValueTask.CompletedTask;
            _disposed = true;
            foreach (var value in _secrets.Values)
                CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(value.AsSpan()));
            _secrets.Clear();
        }
        return ValueTask.CompletedTask;
    }
}
