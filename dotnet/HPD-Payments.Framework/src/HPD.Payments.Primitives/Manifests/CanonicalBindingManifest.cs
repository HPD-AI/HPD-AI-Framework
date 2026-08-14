using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Primitives.Manifests;

/// <summary>Represents the bounded immutable <c>CanonicalBinding</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public sealed record CanonicalBinding(CanonicalDigest Original, CanonicalDigest Successor, NamedTime VerifiedAt)
{
    /// <summary>Gets whether the value satisfies its required-field and bound invariants; a default value is invalid.</summary>
    public bool IsValid => Original is not null && Successor is not null && VerifiedAt.IsValid && VerifiedAt.Kind == TimeKind.Verify;
}

/// <summary>Represents the bounded immutable <c>CanonicalBindingManifest</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public sealed class CanonicalBindingManifest : IEquatable<CanonicalBindingManifest>
{
    /// <summary>Specifies the maximum admitted size or count enforced by the containing type.</summary>
    public const int MaximumBindings = 64;
    private readonly CanonicalBinding[] _bindings;
    /// <summary>Gets the validated <c>ManifestId</c> component; it does not imply ambient context or mutation authority.</summary>
    public SemanticId ManifestId { get; }
    /// <summary>Gets the validated <c>Version</c> component; it does not imply ambient context or mutation authority.</summary>
    public ContractVersion Version { get; }
    /// <summary>Gets the validated <c>Bindings</c> component; it does not imply ambient context or mutation authority.</summary>
    public IReadOnlyList<CanonicalBinding> Bindings => Array.AsReadOnly(_bindings);

    /// <summary>Represents the bounded immutable <c>CanonicalBindingManifest</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
    public CanonicalBindingManifest(SemanticId manifestId, ContractVersion version, IEnumerable<CanonicalBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings.ToArray();
        if (!manifestId.IsValid || !version.IsValid || _bindings.Length is 0 or > MaximumBindings || _bindings.Any(static x => !x.IsValid)) throw new ArgumentException("Invalid or over-bound co-binding manifest.");
        ManifestId = manifestId; Version = version;
    }

    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public bool Equals(CanonicalBindingManifest? other) => other is not null && ManifestId == other.ManifestId && Version == other.Version && _bindings.SequenceEqual(other._bindings);
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public override bool Equals(object? obj) => Equals(obj as CanonicalBindingManifest);
    /// <summary>Returns a process-local hash consistent with equality; the hash is never a persisted identity.</summary>
    public override int GetHashCode() { var h = new HashCode(); h.Add(ManifestId); h.Add(Version); foreach (var b in _bindings) h.Add(b); return h.ToHashCode(); }
}
