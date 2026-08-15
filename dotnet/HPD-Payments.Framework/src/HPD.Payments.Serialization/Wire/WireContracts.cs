using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Payments.Serialization.Wire;

/// <summary>Identifies one frozen authority family on the wire without using a CLR type name.</summary>
public enum AuthorityFamily
{
    /// <summary>No authority; the default value is invalid on the wire.</summary>
    None = 0,
    /// <summary>Agreement authority.</summary>
    Agreement = 1,
    /// <summary>Effective-commercial-fact authority.</summary>
    EffectiveCommercialFact = 2,
    /// <summary>Scoped-identity authority.</summary>
    ScopedIdentity = 3,
    /// <summary>Measured-fact authority.</summary>
    MeasuredFact = 4,
    /// <summary>Measurement-generation authority.</summary>
    MeasurementGeneration = 5,
    /// <summary>Valuation authority.</summary>
    Valuation = 6,
    /// <summary>Obligation authority.</summary>
    Obligation = 7,
    /// <summary>Requested-transition authority.</summary>
    RequestedTransition = 8,
    /// <summary>Issuance-fact authority.</summary>
    IssuanceFact = 9,
    /// <summary>Held-position authority.</summary>
    HeldPosition = 10,
    /// <summary>Value-movement authority.</summary>
    ValueMovement = 11,
    /// <summary>Entitlement grant/removal fact authority.</summary>
    EntitlementGrantRemovalFact = 12,
    /// <summary>Restriction-fact authority.</summary>
    RestrictionFact = 13,
    /// <summary>Capability-evidence authority.</summary>
    CapabilityEvidence = 14,
    /// <summary>External-effect authority.</summary>
    ExternalEffect = 15,
    /// <summary>Work-requirement authority.</summary>
    WorkRequirement = 16,
    /// <summary>Publication-obligation authority.</summary>
    PublicationObligation = 17,
}

/// <summary>Describes the result of reading a versioned durable/public representation.</summary>
public enum CompatibilityDisposition
{
    /// <summary>No disposition; the default value is invalid.</summary>
    None = 0,
    /// <summary>The representation and semantics are supported and may be interpreted.</summary>
    Supported = 1,
    /// <summary>Owned bytes were preserved but must not be interpreted by this reader.</summary>
    Quarantined = 2,
    /// <summary>The family or semantic version is known to be unsupported.</summary>
    Unsupported = 3,
    /// <summary>The reader cannot determine compatibility without an unavailable fact.</summary>
    Indeterminate = 4,
}

/// <summary>Defines immutable resource limits for one wire read.</summary>
/// <param name="MaximumDocumentBytes">Maximum UTF-8 document size.</param>
/// <param name="MaximumDepth">Maximum JSON nesting depth.</param>
/// <param name="MaximumSemanticFields">Maximum number of semantic fields.</param>
/// <param name="MaximumUnknownProperties">Maximum number of preserved unknown properties.</param>
public sealed record WireReadLimits(
    int MaximumDocumentBytes,
    int MaximumDepth,
    int MaximumSemanticFields,
    int MaximumUnknownProperties)
{
    /// <summary>Gets conservative defaults suitable for public and durable boundaries.</summary>
    public static WireReadLimits Default { get; } = new(1_048_576, 32, 256, 64);

    /// <summary>Validates that every bound is positive and within implementation-safe limits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A bound is non-positive or exceeds its hard ceiling.</exception>
    public void Validate()
    {
        if (MaximumDocumentBytes is <= 0 or > 16_777_216) throw new ArgumentOutOfRangeException(nameof(MaximumDocumentBytes));
        if (MaximumDepth is <= 0 or > 128) throw new ArgumentOutOfRangeException(nameof(MaximumDepth));
        if (MaximumSemanticFields is <= 0 or > 4_096) throw new ArgumentOutOfRangeException(nameof(MaximumSemanticFields));
        if (MaximumUnknownProperties is < 0 or > 1_024) throw new ArgumentOutOfRangeException(nameof(MaximumUnknownProperties));
    }
}

/// <summary>A stable, versioned authority representation whose payload is retained as owned JSON.</summary>
public sealed class AuthorityWireDocument
{
    /// <summary>Gets or sets the explicit stable wire discriminator.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets the semantic contract version, independent of representation syntax.</summary>
    [JsonPropertyName("semanticVersion")]
    public int SemanticVersion { get; set; }

    /// <summary>Gets or sets the JSON representation version.</summary>
    [JsonPropertyName("representationVersion")]
    public int RepresentationVersion { get; set; }

    /// <summary>Gets or sets the canonical semantic fields. Field names are wire identities, not CLR names.</summary>
    [JsonPropertyName("semanticFields")]
#pragma warning disable CA2227 // Source-generated STJ requires a settable extension-compatible dictionary.
    public Dictionary<string, JsonElement> SemanticFields { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets unknown top-level properties retained for lossless forwarding.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }
#pragma warning restore CA2227
}

/// <summary>Contains an owned read result and never defaults an unknown representation into a known family.</summary>
/// <param name="Disposition">Compatibility decision.</param>
/// <param name="Family">Known family, or null when the discriminator is unknown.</param>
/// <param name="Document">Parsed document when structurally valid.</param>
/// <param name="OwnedUtf8">Defensive owned copy of the complete input for quarantine or forwarding.</param>
/// <param name="Reason">Stable diagnostic reason code.</param>
public sealed record WireReadResult(
    CompatibilityDisposition Disposition,
    AuthorityFamily? Family,
    AuthorityWireDocument? Document,
    ReadOnlyMemory<byte> OwnedUtf8,
    string Reason);
