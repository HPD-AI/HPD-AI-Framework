using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Primitives.Classification;

/// <summary>Preserves an unknown durable discriminator/version and byte-for-byte owned payload without interpreting it as a known variant.</summary>
public sealed class UnknownVariant : IEquatable<UnknownVariant>
{
    /// <summary>Specifies the maximum admitted size or count enforced by the containing type.</summary>
    public const int MaximumPayloadBytes = 262_144;
    /// <summary>Gets the validated <c>Discriminator</c> component; it does not imply ambient context or mutation authority.</summary>
    public string Discriminator { get; }
    /// <summary>Gets the validated <c>Version</c> component; it does not imply ambient context or mutation authority.</summary>
    public ContractVersion Version { get; }
    /// <summary>Gets the validated <c>Payload</c> component; it does not imply ambient context or mutation authority.</summary>
    public OwnedClassifiedBytes Payload { get; }

    /// <summary>Creates a quarantinable unknown variant and defensively copies its payload.</summary>
    /// <param name="discriminator">A bounded stable lowercase ASCII discriminator.</param>
    /// <param name="version">The original non-default contract version.</param>
    /// <param name="payload">Borrowed payload bytes, copied before return.</param>
    /// <param name="containingClassification">Classification at least as conservative as the containing channel.</param>
    /// <exception cref="ArgumentException">Metadata, classification, or payload length is invalid.</exception>
    public UnknownVariant(string discriminator, ContractVersion version, ReadOnlySpan<byte> payload, ClassificationMark containingClassification)
    {
        if (!ScopeId.TryComponent(discriminator, out var stable) || !version.IsValid) throw new ArgumentException("Unknown variants still require bounded stable metadata.");
        Discriminator = stable;
        Version = version;
        Payload = new(payload, containingClassification, MaximumPayloadBytes);
    }

    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public bool Equals(UnknownVariant? other) => other is not null && StringComparer.Ordinal.Equals(Discriminator, other.Discriminator) && Version == other.Version && Payload.Equals(other.Payload);
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public override bool Equals(object? obj) => Equals(obj as UnknownVariant);
    /// <summary>Returns a process-local hash consistent with equality; the hash is never a persisted identity.</summary>
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Discriminator), Version, Payload);
}
