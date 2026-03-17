using System.ComponentModel.DataAnnotations;

namespace HPD.Auth.Core.Entities;

/// <summary>
/// Stores FIDO2/WebAuthn passkey credentials for passwordless authentication.
/// v2.2: Added InstanceId for multi-tenancy.
/// </summary>
public class UserPasskey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Multi-tenancy discriminator.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.Empty;

    public Guid UserId { get; set; }

    /// <summary>
    /// FIDO2 credential ID (base64url-encoded).
    /// </summary>
    [MaxLength(1024)]
    public string CredentialId { get; set; } = string.Empty;

    /// <summary>
    /// FIDO2 public key (CBOR-encoded, stored as base64).
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Signature counter to detect cloned authenticators.
    /// </summary>
    public uint SignatureCounter { get; set; } = 0;

    /// <summary>
    /// AAGUID of the authenticator (identifies the device model).
    /// </summary>
    public Guid? AaGuid { get; set; }

    /// <summary>
    /// User-supplied name for this passkey (e.g., "Touch ID on MacBook Pro").
    /// </summary>
    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>
    /// Whether this passkey supports user verification (biometric, PIN).
    /// </summary>
    public bool UserVerified { get; set; } = false;

    /// <summary>
    /// Whether this passkey is a resident credential (discoverable).
    /// </summary>
    public bool IsDiscoverable { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }

    // Navigation
    public ApplicationUser User { get; set; } = null!;
}
