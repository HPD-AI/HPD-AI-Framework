using System.Collections.Immutable;
using HPD.Base;

namespace HPD.Auth.Infrastructure.Base;

/// <summary>Describes the closed refresh-token digest-key authority installed by the host.</summary>
public sealed record AuthRefreshDigestKeyRingCapability
{
    /// <summary>Gets the stable capability module identifier.</summary>
    public required string ModuleId { get; init; }
    /// <summary>Gets the positive active issuance version.</summary>
    public required int ActiveIssuanceVersion { get; init; }
    /// <summary>Gets sorted unique validation versions.</summary>
    public required ImmutableArray<int> ValidationVersions { get; init; }
    /// <summary>Gets key ownership.</summary>
    public required AuthDigestKeyOwnership Ownership { get; init; }
    /// <summary>Gets whether readiness verification succeeded.</summary>
    public required bool IsReady { get; init; }
    /// <summary>Gets the UTC verification instant.</summary>
    public required DateTimeOffset LastVerifiedAt { get; init; }
}

/// <summary>Owns one version-addressed refresh-token digest key.</summary>
public sealed record AuthRefreshDigestKey : IDisposable
{
    /// <summary>Gets the positive key version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the deeply owned 32- or 64-byte secret material.</summary>
    public required AuthOwnedSecretBytes KeyMaterial { get; init; }
    /// <inheritdoc />
    public void Dispose() => KeyMaterial.Dispose();
}

/// <summary>Supplies exact HMAC keys for refresh-token issuance and validation.</summary>
public interface IAuthRefreshTokenDigestKeyRing
{
    /// <summary>Gets the active issuance key.</summary>
    AuthAuthorityResult<AuthRefreshDigestKey> GetActiveIssuanceKey();
    /// <summary>Gets exactly one validation key.</summary>
    /// <param name="version">Positive registered version.</param>
    AuthAuthorityResult<AuthRefreshDigestKey> GetValidationKey(int version);
    /// <summary>Gets immutable capability authority.</summary>
    AuthRefreshDigestKeyRingCapability Capability { get; }
}

/// <summary>Owns non-secret encrypted envelope bytes.</summary>
public sealed class AuthOwnedEnvelopeBytes
{
    private readonly byte[] _bytes;
    private AuthOwnedEnvelopeBytes(byte[] bytes) => _bytes = bytes;
    /// <summary>Copies one encrypted envelope.</summary>
    public static AuthOwnedEnvelopeBytes From(ReadOnlySpan<byte> value) => new(value.ToArray());
    /// <summary>Gets the envelope byte count.</summary>
    public int Length => _bytes.Length;
    /// <summary>Returns a fresh copy.</summary>
    public byte[] ToArray() => _bytes.ToArray();
    internal ReadOnlySpan<byte> Span => _bytes;
}

/// <summary>Describes authenticated token-delivery protection authority.</summary>
public sealed record AuthTokenDeliveryProtectorCapability
{
    /// <summary>Gets the stable capability module identifier.</summary>
    public required string ModuleId { get; init; }
    /// <summary>Gets the positive active protector version.</summary>
    public required int ActiveVersion { get; init; }
    /// <summary>Gets sorted unique decryptable versions.</summary>
    public required ImmutableArray<int> ValidationVersions { get; init; }
    /// <summary>Gets key ownership.</summary>
    public required AuthDigestKeyOwnership Ownership { get; init; }
    /// <summary>Gets whether authenticated encryption is guaranteed.</summary>
    public required bool AuthenticatedEncryption { get; init; }
    /// <summary>Gets whether rotation is supported.</summary>
    public required bool SupportsRotation { get; init; }
    /// <summary>Gets whether readiness verification succeeded.</summary>
    public required bool IsReady { get; init; }
    /// <summary>Gets the UTC verification instant.</summary>
    public required DateTimeOffset LastVerifiedAt { get; init; }
}

/// <summary>Provides one protected delivery envelope.</summary>
public sealed record AuthProtectedTokenEnvelope
{
    /// <summary>Gets the positive protector version.</summary>
    public required int ProtectorVersion { get; init; }
    /// <summary>Gets copied encrypted bytes.</summary>
    public required AuthOwnedEnvelopeBytes Ciphertext { get; init; }
}

/// <summary>Protects one-time refresh-token delivery material with authenticated encryption.</summary>
public interface IAuthTokenDeliveryProtector
{
    /// <summary>Protects plaintext under exact canonical associated data.</summary>
    /// <param name="plaintext">Deeply owned bearer bytes.</param>
    /// <param name="associatedData">Canonical associated-data bytes.</param>
    AuthAuthorityResult<AuthProtectedTokenEnvelope> Protect(AuthOwnedSecretBytes plaintext, AuthOwnedEnvelopeBytes associatedData);
    /// <summary>Unprotects one envelope under exact canonical associated data.</summary>
    /// <param name="protectorVersion">Stored positive protector version.</param>
    /// <param name="ciphertext">Stored encrypted envelope.</param>
    /// <param name="associatedData">Canonical associated-data bytes.</param>
    AuthAuthorityResult<AuthOwnedSecretBytes> Unprotect(int protectorVersion, AuthOwnedEnvelopeBytes ciphertext, AuthOwnedEnvelopeBytes associatedData);
    /// <summary>Gets immutable protection authority.</summary>
    AuthTokenDeliveryProtectorCapability Capability { get; }
}
