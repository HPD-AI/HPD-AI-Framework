using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Primitives.Compatibility;

/// <summary>Represents the bounded immutable <c>CompatibilityKind</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public enum CompatibilityKind
{
    /// <summary>Invalid default compatibility result.</summary>
    None = 0,
    /// <summary>The discriminator and version fall inside the declared reader range.</summary>
    Compatible,
    /// <summary>The input is valid but its discriminator or version is outside the reader range.</summary>
    Unsupported,
    /// <summary>Compatibility cannot be decided because the range or input metadata is invalid.</summary>
    Indeterminate
}

/// <summary>Represents the bounded immutable <c>ReaderRange</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public readonly record struct ReaderRange(string Discriminator, ContractVersion Minimum, ContractVersion Maximum)
{
    /// <summary>Gets whether the value satisfies its required-field and bound invariants; a default value is invalid.</summary>
    public bool IsValid => ScopeId.TryComponent(Discriminator, out _) && Minimum.IsValid && Maximum.IsValid &&
        (Minimum.Major < Maximum.Major || Minimum.Major == Maximum.Major && Minimum.Minor <= Maximum.Minor);
    /// <summary>Classifies a discriminator and version without defaulting unknown or invalid metadata to compatibility.</summary>
    public CompatibilityKind Classify(string discriminator, ContractVersion version)
    {
        if (!IsValid || !version.IsValid || !ScopeId.TryComponent(discriminator, out _)) return CompatibilityKind.Indeterminate;
        if (!StringComparer.Ordinal.Equals(Discriminator, discriminator)) return CompatibilityKind.Unsupported;
        var lower = version.Major > Minimum.Major || version.Major == Minimum.Major && version.Minor >= Minimum.Minor;
        var upper = version.Major < Maximum.Major || version.Major == Maximum.Major && version.Minor <= Maximum.Minor;
        return lower && upper ? CompatibilityKind.Compatible : CompatibilityKind.Unsupported;
    }
}
