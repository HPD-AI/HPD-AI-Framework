using System.Collections.Immutable;
using System.Security.Cryptography;
using HPD.Base;

namespace HPD.Auth.Infrastructure.Base;

/// <summary>Represents an available or safely unavailable host-owned Auth authority value.</summary>
/// <typeparam name="T">Deeply owned authority value type.</typeparam>
public sealed class AuthAuthorityResult<T> where T : class
{
    private AuthAuthorityResult(T? value) => Value = value;

    /// <summary>Gets whether the authority value is available.</summary>
    public bool IsAvailable => Value is not null;

    /// <summary>Gets the available value, otherwise <see langword="null"/>.</summary>
    public T? Value { get; }

    /// <summary>Creates an available authority result.</summary>
    /// <param name="value">Deeply owned value.</param>
    public static AuthAuthorityResult<T> Available(T value) => new(
        value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the single non-disclosing unavailable result.</summary>
    public static AuthAuthorityResult<T> Unavailable() => new(null);
}

/// <summary>Identifies who owns durable recovery-code digest key material.</summary>
public enum AuthDigestKeyOwnership
{
    /// <summary>The host owns key configuration and disaster recovery.</summary>
    Host = 0,
    /// <summary>An external key-management authority owns the material.</summary>
    ExternalKms = 1,
}

/// <summary>Owns secret bytes and clears its private buffer when disposed.</summary>
public sealed class AuthOwnedSecretBytes : IDisposable
{
    private byte[]? _bytes;

    private AuthOwnedSecretBytes(byte[] bytes) => _bytes = bytes;

    /// <summary>Creates deeply owned secret material by copying <paramref name="value"/>.</summary>
    /// <param name="value">Secret bytes to copy.</param>
    /// <returns>A new independently owned secret value.</returns>
    public static AuthOwnedSecretBytes From(ReadOnlySpan<byte> value) => new(value.ToArray());

    /// <summary>Gets the secret byte count without disclosing the material.</summary>
    public int Length => (_bytes ?? throw new ObjectDisposedException(nameof(AuthOwnedSecretBytes))).Length;

    /// <summary>Copies the secret into an exactly sized caller-owned destination.</summary>
    /// <param name="destination">Destination whose length must equal <see cref="Length"/>.</param>
    public void CopyTo(Span<byte> destination)
    {
        byte[] bytes = _bytes ?? throw new ObjectDisposedException(nameof(AuthOwnedSecretBytes));
        if (destination.Length != bytes.Length)
            throw new ArgumentException("The destination must exactly match the secret length.", nameof(destination));
        bytes.CopyTo(destination);
    }

    internal ReadOnlySpan<byte> DangerousReadOnlySpan => _bytes ?? throw new ObjectDisposedException(nameof(AuthOwnedSecretBytes));

    /// <inheritdoc />
    public void Dispose()
    {
        byte[]? bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null)
            CryptographicOperations.ZeroMemory(bytes);
    }
}

/// <summary>Describes the closed recovery-code digest-key authority installed by the host.</summary>
public sealed record AuthRecoveryCodeDigestKeyRingCapability
{
    /// <summary>Gets the stable capability module identifier.</summary>
    public required string ModuleId { get; init; }
    /// <summary>Gets the positive version used for new recovery codes.</summary>
    public required int ActiveIssuanceVersion { get; init; }
    /// <summary>Gets the sorted unique versions accepted for validation.</summary>
    public required ImmutableArray<int> ValidationVersions { get; init; }
    /// <summary>Gets the authority that owns key material.</summary>
    public required AuthDigestKeyOwnership Ownership { get; init; }
    /// <summary>Gets whether the authority has completed readiness verification.</summary>
    public required bool IsReady { get; init; }
    /// <summary>Gets the UTC instant of the last successful verification.</summary>
    public required DateTimeOffset LastVerifiedAt { get; init; }
}

/// <summary>Owns one version-addressed recovery-code digest key.</summary>
public sealed record AuthRecoveryCodeDigestKey : IDisposable
{
    /// <summary>Gets the positive stable key version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the deeply owned 32- or 64-byte secret material.</summary>
    public required AuthOwnedSecretBytes KeyMaterial { get; init; }
    /// <inheritdoc />
    public void Dispose() => KeyMaterial.Dispose();
}

/// <summary>Supplies purpose-separated HMAC keys for recovery-code issuance and validation.</summary>
public interface IAuthRecoveryCodeDigestKeyRing
{
    /// <summary>Gets the one active issuance key.</summary>
    /// <returns>The active key, or one safe readiness failure.</returns>
    AuthAuthorityResult<AuthRecoveryCodeDigestKey> GetActiveIssuanceKey();
    /// <summary>Gets exactly one version-addressed validation key.</summary>
    /// <param name="version">Positive version declared by the installed capability.</param>
    /// <returns>The requested key, or one safe invalid-key result.</returns>
    AuthAuthorityResult<AuthRecoveryCodeDigestKey> GetValidationKey(int version);
    /// <summary>Gets immutable key-ring capability authority.</summary>
    AuthRecoveryCodeDigestKeyRingCapability Capability { get; }
}

internal static class AuthRecoveryCodeDigestAuthority
{
    internal const string Purpose = "hpd.auth.recovery-code.v1";
    internal const int MaximumValidationVersions = 16;

    internal static ImmutableArray<int> ValidateCapability(IAuthRecoveryCodeDigestKeyRing keyRing)
    {
        ArgumentNullException.ThrowIfNull(keyRing);
        AuthRecoveryCodeDigestKeyRingCapability capability = keyRing.Capability
            ?? throw new AuthBasePersistenceException("auth.recoveryCode.digestKeyUnavailable");
        ImmutableArray<int> versions = capability.ValidationVersions;
        if (!capability.IsReady
            || string.IsNullOrWhiteSpace(capability.ModuleId)
            || capability.ActiveIssuanceVersion <= 0
            || versions.IsDefaultOrEmpty
            || versions.Length > MaximumValidationVersions
            || !Enum.IsDefined(capability.Ownership)
            || capability.LastVerifiedAt.Offset != TimeSpan.Zero
            || !versions.SequenceEqual(versions.Order())
            || versions.Distinct().Count() != versions.Length
            || versions.Any(static value => value <= 0)
            || !versions.Contains(capability.ActiveIssuanceVersion))
            throw new AuthBasePersistenceException("auth.recoveryCode.digestKeyUnavailable");
        return versions;
    }

    internal static byte[] Digest(AuthRecoveryCodeDigestKey key, string canonicalCode)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalCode);
        if (key.Version <= 0 || key.KeyMaterial.Length is not (32 or 64))
            throw new AuthBasePersistenceException("auth.recoveryCode.digestKeyUnavailable");
        byte[] purpose = System.Text.Encoding.UTF8.GetBytes(Purpose);
        byte[] code = System.Text.Encoding.UTF8.GetBytes(canonicalCode);
        byte[] input = new byte[purpose.Length + code.Length];
        purpose.CopyTo(input, 0);
        code.CopyTo(input, purpose.Length);
        try
        {
            return HMACSHA256.HashData(key.KeyMaterial.DangerousReadOnlySpan, input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(code);
        }
    }
}
