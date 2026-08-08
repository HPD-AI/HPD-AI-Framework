namespace HPD.Base;

/// <summary>Supplies one closed 256-bit key to BASE opaque-token protection.</summary>
public sealed record BaseOpaqueTokenKey
{
    /// <summary>Gets the unique wire key identifier.</summary>
    public required byte Id { get; init; }
    /// <summary>Gets the 32-byte key material. BASE copies it during option validation.</summary>
    public required byte[] Key { get; init; }
    /// <summary>Gets the inclusive UTC instant at which this key may begin issuing tokens.</summary>
    public required DateTimeOffset IssueNotBefore { get; init; }
    /// <summary>Gets the optional exclusive UTC instant at which this key stops issuing tokens.</summary>
    public DateTimeOffset? IssueUntil { get; init; }
    /// <summary>Gets the optional exclusive UTC instant at which this key stops decrypting tokens.</summary>
    public DateTimeOffset? DecryptUntil { get; init; }
}

/// <summary>Configures the shared closed key ring for purpose-bound BASE tokens.</summary>
public sealed class HPDBaseTokenProtectionOptions
{
    /// <summary>Gets or sets the key used to create new tokens and manifests.</summary>
    public required BaseOpaqueTokenKey ActiveKey { get; set; }
    /// <summary>Gets or sets retained keys accepted only for decryption or validation.</summary>
    public BaseOpaqueTokenKey[] DecryptionKeys { get; set; } = [];
}

internal sealed record BaseTokenProtectionRegistration(bool ExplicitlyConfigured);
