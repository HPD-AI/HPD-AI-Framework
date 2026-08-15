using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Contains one immutable, canonically ordered application provider catalog.</summary>
public sealed class ProviderCatalogV1 : IEquatable<ProviderCatalogV1>
{
    /// <summary>The maximum number of provider-family contributions in one application catalog.</summary>
    public const int MaximumContributions = 256;

    /// <summary>Initializes a nonempty provider catalog and computes its canonical fingerprint.</summary>
    /// <param name="contributions">One to 256 provider-family contributions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contributions"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="contributions"/> is empty, contains null, or contains a duplicate identity tuple.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="contributions"/> exceeds 256 items.</exception>
    public ProviderCatalogV1(IEnumerable<ProviderContributionV1> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        var values = CollectBounded(contributions);
        if (values.Length == 0)
            throw new ArgumentException("A provider catalog cannot be empty.", nameof(contributions));
        if (values.Any(static value => value is null))
            throw new ArgumentException("A provider catalog cannot contain null contributions.", nameof(contributions));
        Array.Sort(values, CompareContributions);
        for (var index = 1; index < values.Length; index++)
        {
            if (CompareContributions(values[index - 1], values[index]) == 0)
                throw new ArgumentException("A provider catalog contains a duplicate provider, family, and factory identity tuple.", nameof(contributions));
        }
        Contributions = Array.AsReadOnly(values);
        Fingerprint = ProviderCatalogV1Codec.ComputeIntegrityHash(this);
    }

    /// <summary>Gets the strictly ordered provider-family contributions.</summary>
    public IReadOnlyList<ProviderContributionV1> Contributions { get; }

    /// <summary>Gets the schema-bound canonical catalog fingerprint.</summary>
    public Hash256 Fingerprint { get; }

    /// <inheritdoc />
    public bool Equals(ProviderCatalogV1? other) =>
        other is not null && Contributions.SequenceEqual(other.Contributions);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ProviderCatalogV1 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var contribution in Contributions) hash.Add(contribution);
        return hash.ToHashCode();
    }

    /// <summary>Returns whether two catalogs contain the same canonical contributions.</summary>
    public static bool operator ==(ProviderCatalogV1? left, ProviderCatalogV1? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);

    /// <summary>Returns whether two catalogs contain different canonical contributions.</summary>
    public static bool operator !=(ProviderCatalogV1? left, ProviderCatalogV1? right) => !(left == right);

    internal static int CompareContributions(ProviderContributionV1 left, ProviderContributionV1 right)
    {
        var result = StringComparer.Ordinal.Compare(left.ProviderId.ToString(), right.ProviderId.ToString());
        if (result != 0) return result;
        result = StringComparer.Ordinal.Compare(left.FamilyId.ToString(), right.FamilyId.ToString());
        return result != 0
            ? result
            : StringComparer.Ordinal.Compare(left.FactoryId.ToString(), right.FactoryId.ToString());
    }

    private static ProviderContributionV1[] CollectBounded(IEnumerable<ProviderContributionV1> source)
    {
        var values = new List<ProviderContributionV1>();
        foreach (var value in source)
        {
            if (values.Count == MaximumContributions)
                throw new ArgumentOutOfRangeException(nameof(source), "A provider catalog cannot exceed 256 contributions.");
            values.Add(value);
        }
        return values.ToArray();
    }
}

internal static class ProviderCatalogV1Codec
{
    internal const string SchemaIdentifier = "hpd.provider-catalog.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;

    internal static byte[] Encode(ProviderCatalogV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contributions.Count is < 1 or > ProviderCatalogV1.MaximumContributions)
            throw new ArgumentException("The provider catalog is invalid.", nameof(value));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(1);
        writer.WriteUInt64(1);
        writer.WriteStartArray(value.Contributions.Count);
        foreach (var contribution in value.Contributions) ProviderContributionV1Codec.Write(writer, contribution);
        writer.WriteEndArray();
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static Hash256 ComputeIntegrityHash(ProviderCatalogV1 value) =>
        AuthorityIntegrityHashV1.Compute(SchemaIdentifier, Major, Minor, Encode(value));

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out ProviderCatalogV1? value)
    {
        value = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 1 || reader.ReadUInt64() != 1)
                return false;
            var count = reader.ReadStartArray();
            if (count is null or < 1 or > ProviderCatalogV1.MaximumContributions)
                return false;
            var contributions = new ProviderContributionV1[count.Value];
            for (var index = 0; index < contributions.Length; index++)
            {
                contributions[index] = ProviderContributionV1Codec.Read(reader);
                if (index > 0 && ProviderCatalogV1.CompareContributions(contributions[index - 1], contributions[index]) >= 0)
                    return false;
            }
            reader.ReadEndArray();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0)
                return false;
            value = new ProviderCatalogV1(contributions);
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException)
        {
            value = null;
            return false;
        }
    }
}
