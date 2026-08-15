using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Primitives.Identity;

/// <summary>Represents the bounded immutable <c>CanonicalDigestProfileId</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public sealed class CanonicalDigestProfileId : IEquatable<CanonicalDigestProfileId>
{
    /// <summary>Specifies the maximum admitted size or count enforced by the containing type.</summary>
    public const int MaximumDescriptorUtf8Bytes = 2048;
    /// <summary>Represents the bounded immutable <c>SemanticDiscriminator</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
    public string SemanticDiscriminator { get; }
    /// <summary>Represents the bounded immutable <c>SemanticVersion</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
    public ContractVersion SemanticVersion { get; }
    /// <summary>Gets the validated <c>FieldSet</c> component; it does not imply ambient context or mutation authority.</summary>
    public string FieldSet { get; }
    /// <summary>Gets the validated <c>Normalization</c> component; it does not imply ambient context or mutation authority.</summary>
    public string Normalization { get; }
    /// <summary>Gets the validated <c>NumericTimeGrammar</c> component; it does not imply ambient context or mutation authority.</summary>
    public string NumericTimeGrammar { get; }
    /// <summary>Gets the validated <c>CollectionOrder</c> component; it does not imply ambient context or mutation authority.</summary>
    public string CollectionOrder { get; }
    /// <summary>Gets the validated <c>AlgorithmKeyId</c> component; it does not imply ambient context or mutation authority.</summary>
    public string AlgorithmKeyId { get; }

    /// <summary>Represents the bounded immutable <c>CanonicalDigestProfileId</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
    public CanonicalDigestProfileId(string semanticDiscriminator, ContractVersion semanticVersion, string fieldSet, string normalization, string numericTimeGrammar, string collectionOrder, string algorithmKeyId)
    {
        if (!semanticVersion.IsValid) throw new ArgumentException("Invalid semantic profile version.");
        var values = new[] { semanticDiscriminator, fieldSet, normalization, numericTimeGrammar, collectionOrder, algorithmKeyId };
        if (values.Any(static x => !ScopeId.TryComponent(x, out _)) || values.Sum(Encoding.UTF8.GetByteCount) > MaximumDescriptorUtf8Bytes) throw new ArgumentException("Invalid or over-bound canonical profile.");
        (SemanticDiscriminator, SemanticVersion, FieldSet, Normalization, NumericTimeGrammar, CollectionOrder, AlgorithmKeyId) =
            (semanticDiscriminator, semanticVersion, fieldSet, normalization, numericTimeGrammar, collectionOrder, algorithmKeyId);
    }

    /// <summary>Returns the stable textual representation defined by the containing type, or its explicit invalid diagnostic where supported.</summary>
    public string ToCanonicalText() => $"{SemanticDiscriminator}|{SemanticVersion.Major}.{SemanticVersion.Minor}|{FieldSet}|{Normalization}|{NumericTimeGrammar}|{CollectionOrder}|{AlgorithmKeyId}";
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public bool Equals(CanonicalDigestProfileId? other) => other is not null && StringComparer.Ordinal.Equals(ToCanonicalText(), other.ToCanonicalText());
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public override bool Equals(object? obj) => Equals(obj as CanonicalDigestProfileId);
    /// <summary>Returns a process-local hash consistent with equality; the hash is never a persisted identity.</summary>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToCanonicalText());
}

/// <summary>Represents the bounded immutable <c>CanonicalDigest</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public sealed class CanonicalDigest : IEquatable<CanonicalDigest>
{
    /// <summary>Specifies the maximum admitted size or count enforced by the containing type.</summary>
    public const int MaximumDigestBytes = 128;
    private readonly byte[] _bytes;
    /// <summary>Gets the validated <c>Profile</c> component; it does not imply ambient context or mutation authority.</summary>
    public CanonicalDigestProfileId Profile { get; }
    /// <summary>Gets the validated <c>Algorithm</c> component; it does not imply ambient context or mutation authority.</summary>
    public string Algorithm { get; }
    /// <summary>Gets the enforced bound or owned logical length.</summary>
    public int Length => _bytes.Length;

    /// <summary>Represents the bounded immutable <c>CanonicalDigest</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
    public CanonicalDigest(CanonicalDigestProfileId profile, string algorithm, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!ScopeId.TryComponent(algorithm, out var stable) || bytes.Length is 0 or > MaximumDigestBytes) throw new ArgumentException("Invalid digest descriptor or length.");
        Profile = profile; Algorithm = stable; _bytes = bytes.ToArray();
    }

    /// <summary>Computes SHA-256 over caller-provided canonical bytes and owns the resulting digest.</summary>
    public static CanonicalDigest Sha256(CanonicalDigestProfileId profile, ReadOnlySpan<byte> canonicalBytes) => new(profile, "sha256", SHA256.HashData(canonicalBytes));
    /// <summary>Returns a newly allocated copy; callers never receive an alias to retained storage.</summary>
    public byte[] CopyBytes() => (byte[])_bytes.Clone();
    /// <summary>Returns the stable textual representation defined by the containing type, or its explicit invalid diagnostic where supported.</summary>
    public string ToCanonicalText() => $"{Profile.ToCanonicalText()}|{Algorithm}|{Convert.ToHexString(_bytes)}";
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public bool Equals(CanonicalDigest? other) => other is not null && Profile.Equals(other.Profile) && StringComparer.Ordinal.Equals(Algorithm, other.Algorithm) && _bytes.AsSpan().SequenceEqual(other._bytes);
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public override bool Equals(object? obj) => Equals(obj as CanonicalDigest);
    /// <summary>Returns a process-local hash consistent with equality; the hash is never a persisted identity.</summary>
    public override int GetHashCode() { var h = new HashCode(); h.Add(Profile); h.Add(Algorithm, StringComparer.Ordinal); foreach (var b in _bytes) h.Add(b); return h.ToHashCode(); }
}
