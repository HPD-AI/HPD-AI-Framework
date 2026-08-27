using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph.Runtime;

internal enum GraphRuntimeExecutableCatalogInvalidV1
{
    Empty = 1, TooMany = 2, MissingDeclaration = 3, ExtraDeclaration = 4,
    DuplicateNodeKey = 5, DuplicateFactoryIdentity = 6, InvalidIdentity = 7, InvalidRevision = 8,
}

internal sealed record GraphRuntimeExecutableFactoryDeclarationV1
{
    internal GraphRuntimeExecutableFactoryDeclarationV1(BoundedAscii nodeKey, string implementationIdentity,
        uint catalogRevision)
    {
        GraphTopologyNodeV1.ValidateKey(nodeKey, nameof(nodeKey));
        NodeKey = nodeKey;
        ImplementationIdentity = implementationIdentity ?? throw new ArgumentNullException(nameof(implementationIdentity));
        CatalogRevision = catalogRevision;
    }

    internal BoundedAscii NodeKey { get; }
    internal string ImplementationIdentity { get; }
    internal uint CatalogRevision { get; }
}

internal sealed record GraphRuntimeExecutableFactoryBindingV1(
    BoundedAscii NodeKey, StableId128 FactoryIdentity, string ImplementationIdentity, uint CatalogRevision);

internal abstract record GraphRuntimeExecutableCatalogResultV1
{
    private GraphRuntimeExecutableCatalogResultV1() { }
    internal sealed record Created(GraphRuntimeExecutableFactoryCatalogV1 Catalog) : GraphRuntimeExecutableCatalogResultV1;
    internal sealed record Invalid(GraphRuntimeExecutableCatalogInvalidV1 Reason) : GraphRuntimeExecutableCatalogResultV1;
}

internal sealed class GraphRuntimeExecutableFactoryCatalogV1
{
    internal delegate byte[] FactoryHashV1(ReadOnlySpan<byte> preimage);
    internal const int MaximumEntries = 64;
    private static readonly byte[] FactoryDomain = "hpd-s2-graph-executable-factory-v1\0"u8.ToArray();
    private static readonly byte[] FingerprintDomain = "hpd-s2-graph-executable-factory-catalog-v1\0"u8.ToArray();
    private readonly GraphRuntimeExecutableFactoryBindingV1[] _entries;

    private GraphRuntimeExecutableFactoryCatalogV1(GraphRuntimeExecutableFactoryBindingV1[] entries)
    {
        _entries = entries;
        Entries = Array.AsReadOnly(_entries);
        Fingerprint = ComputeFingerprint(_entries);
    }

    internal IReadOnlyList<GraphRuntimeExecutableFactoryBindingV1> Entries { get; }
    internal Hash256 Fingerprint { get; }

    // This is the sole runtime entry point intended for generated application manifest output.
    internal static GraphRuntimeExecutableCatalogResultV1 FromGeneratedApplicationManifest(
        IEnumerable<GraphRuntimeExecutableFactoryDeclarationV1> declarations) =>
        FromGeneratedApplicationManifest(declarations, null, static preimage => SHA256.HashData(preimage));

    internal static GraphRuntimeExecutableCatalogResultV1 FromGeneratedApplicationManifest(
        IEnumerable<GraphRuntimeExecutableFactoryDeclarationV1> declarations,
        IEnumerable<BoundedAscii>? declaredNodeKeys, FactoryHashV1? factoryHash)
    {
        if (factoryHash is null)
            return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.InvalidIdentity);
        if (declarations is null)
            return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.MissingDeclaration);
        var owned = declarations.Take(MaximumEntries + 1).ToArray();
        if (owned.Length == 0)
            return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.Empty);
        if (owned.Length > MaximumEntries)
            return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.TooMany);
        if (owned.Any(static declaration => declaration is null))
            return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.MissingDeclaration);
        if (declaredNodeKeys is not null)
        {
            var authority = declaredNodeKeys.Take(MaximumEntries + 1).ToArray();
            if (authority.Length == 0 || authority.Length > MaximumEntries ||
                authority.Any(static key => !key.IsValid) || authority.Distinct().Count() != authority.Length)
                return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.InvalidIdentity);
            var entryKeys = owned.Select(static declaration => declaration.NodeKey).ToArray();
            if (authority.Any(key => !entryKeys.Contains(key)))
                return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.MissingDeclaration);
            if (entryKeys.Any(key => !authority.Contains(key)))
                return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.ExtraDeclaration);
        }

        var entries = new GraphRuntimeExecutableFactoryBindingV1[owned.Length];
        for (var index = 0; index < owned.Length; index++)
        {
            var declaration = owned[index];
            if (declaration.CatalogRevision == 0)
                return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.InvalidRevision);
            if (!TryNormalizeIdentity(declaration.ImplementationIdentity, out var identity))
                return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.InvalidIdentity);
            if (!TryDeriveFactoryIdentity(declaration.NodeKey, identity, declaration.CatalogRevision,
                    factoryHash, out var factoryIdentity))
                return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.InvalidIdentity);
            entries[index] = new(declaration.NodeKey, factoryIdentity, identity, declaration.CatalogRevision);
        }
        Array.Sort(entries, CompareEntries);
        for (var index = 1; index < entries.Length; index++)
        {
            if (entries[index - 1].NodeKey == entries[index].NodeKey)
                return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.DuplicateNodeKey);
            if (entries[index - 1].FactoryIdentity.Equals(entries[index].FactoryIdentity) ||
                entries.Take(index - 1).Any(entry => entry.FactoryIdentity.Equals(entries[index].FactoryIdentity)))
                return new GraphRuntimeExecutableCatalogResultV1.Invalid(GraphRuntimeExecutableCatalogInvalidV1.DuplicateFactoryIdentity);
        }
        return new GraphRuntimeExecutableCatalogResultV1.Created(new(entries));
    }

    private static bool TryNormalizeIdentity(string value, out string normalized)
    {
        try { normalized = value.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { normalized = string.Empty; return false; }
        if (!string.Equals(value, normalized, StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(normalized) is 0 or > 512)
            return false;
        var colon = normalized.IndexOf(':');
        var at = normalized.LastIndexOf('@');
        if (colon <= 0 || at <= colon + 1 || at == normalized.Length - 1 ||
            normalized.IndexOf(':', colon + 1) >= 0 || normalized.IndexOf('@') != at)
            return false;
        var assembly = normalized.AsSpan(0, colon);
        var type = normalized.AsSpan(colon + 1, at - colon - 1);
        var revision = normalized.AsSpan(at + 1);
        if (!ValidAssembly(assembly) || !ValidType(type) ||
            revision[0] == '0' || !AllDecimal(revision) ||
            !uint.TryParse(revision, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
            return false;
        return true;
    }

    private static bool ValidAssembly(ReadOnlySpan<char> value)
    { foreach (var character in value) if (character is <= ' ' or >= (char)0x7f or ':' or '@') return false; return true; }
    private static bool ValidType(ReadOnlySpan<char> value)
    { foreach (var character in value) if (char.IsControl(character) || character is ':' or '@') return false; return true; }
    private static bool AllDecimal(ReadOnlySpan<char> value)
    { foreach (var character in value) if (character is < '0' or > '9') return false; return true; }

    private static bool TryDeriveFactoryIdentity(BoundedAscii nodeKey, string identity, uint revision,
        FactoryHashV1 factoryHash, out StableId128 result)
    {
        var key = Encoding.UTF8.GetBytes(nodeKey.ToString());
        var implementation = Encoding.UTF8.GetBytes(identity);
        var preimage = new byte[FactoryDomain.Length + 2 + key.Length + 2 + implementation.Length + 4];
        var offset = 0;
        FactoryDomain.CopyTo(preimage, offset); offset += FactoryDomain.Length;
        BinaryPrimitives.WriteUInt16BigEndian(preimage.AsSpan(offset), checked((ushort)key.Length)); offset += 2;
        key.CopyTo(preimage, offset); offset += key.Length;
        BinaryPrimitives.WriteUInt16BigEndian(preimage.AsSpan(offset), checked((ushort)implementation.Length)); offset += 2;
        implementation.CopyTo(preimage, offset); offset += implementation.Length;
        BinaryPrimitives.WriteUInt32BigEndian(preimage.AsSpan(offset), revision);
        byte[]? digest;
        try { digest = factoryHash(preimage); }
        catch (Exception)
        { result = default; return false; }
        if (digest is null || digest.Length != 32 || digest.AsSpan(0, 16).IndexOfAnyExcept((byte)0) < 0)
        { result = default; return false; }
        result = StableId128.FromBytes(digest.AsSpan(0, 16)); return true;
    }

    private static int CompareEntries(GraphRuntimeExecutableFactoryBindingV1 left,
        GraphRuntimeExecutableFactoryBindingV1 right)
    {
        var compared = Encoding.UTF8.GetBytes(left.NodeKey.ToString()).AsSpan()
            .SequenceCompareTo(Encoding.UTF8.GetBytes(right.NodeKey.ToString()));
        if (compared != 0) return compared;
        Span<byte> leftId = stackalloc byte[16]; Span<byte> rightId = stackalloc byte[16];
        left.FactoryIdentity.TryWriteBytes(leftId); right.FactoryIdentity.TryWriteBytes(rightId);
        compared = leftId.SequenceCompareTo(rightId);
        if (compared != 0) return compared;
        compared = Encoding.UTF8.GetBytes(left.ImplementationIdentity).AsSpan()
            .SequenceCompareTo(Encoding.UTF8.GetBytes(right.ImplementationIdentity));
        return compared != 0 ? compared : left.CatalogRevision.CompareTo(right.CatalogRevision);
    }

    private static Hash256 ComputeFingerprint(IEnumerable<GraphRuntimeExecutableFactoryBindingV1> entries)
    {
        var values = entries.ToArray();
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartArray(2); writer.WriteUInt64(1); writer.WriteStartArray(values.Length);
        foreach (var entry in values) WriteBinding(writer, entry);
        writer.WriteEndArray(); writer.WriteEndArray();
        return Hash(FingerprintDomain, writer.Encode());
    }

    internal static void WriteBinding(CborWriter writer, GraphRuntimeExecutableFactoryBindingV1 entry)
    {
        writer.WriteStartArray(4); writer.WriteTextString(entry.NodeKey.ToString());
        WriteId(writer, entry.FactoryIdentity); writer.WriteTextString(entry.ImplementationIdentity);
        writer.WriteUInt64(entry.CatalogRevision); writer.WriteEndArray();
    }

    internal static void WriteId(CborWriter writer, StableId128 id)
    { Span<byte> bytes = stackalloc byte[16]; id.TryWriteBytes(bytes); writer.WriteByteString(bytes); }

    internal static Hash256 Hash(byte[] domain, byte[] payload)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(domain); hash.AppendData(payload); Span<byte> digest = stackalloc byte[32];
        hash.TryGetHashAndReset(digest, out _); return Hash256.FromBytes(digest);
    }
}

internal abstract record GraphRuntimeExecutableCompileResultV1
{
    private GraphRuntimeExecutableCompileResultV1() { }
    internal sealed record Compiled(GraphRuntimeExecutablePlanV1 Plan) : GraphRuntimeExecutableCompileResultV1;
    internal sealed record InvalidCatalog : GraphRuntimeExecutableCompileResultV1;
    internal sealed record TopologyMismatch : GraphRuntimeExecutableCompileResultV1;
    internal sealed record MissingFactory(BoundedAscii NodeKey) : GraphRuntimeExecutableCompileResultV1;
    internal sealed record ExtraFactory(BoundedAscii NodeKey) : GraphRuntimeExecutableCompileResultV1;
}

internal sealed class GraphRuntimeExecutablePlanV1
{
    private static readonly byte[] FingerprintDomain = "hpd-s2-graph-executable-plan-v1\0"u8.ToArray();
    private readonly byte[][] _capacityScopes;
    private readonly CapacityChargeV1[] _capacityCharges;
    private readonly GraphRuntimeExecutableFactoryBindingV1[] _nodeBindings;

    private GraphRuntimeExecutablePlanV1(GraphTopologyPlanV1 topology, GraphRuntimeExecutableFactoryCatalogV1 catalog,
        CapacityChargeV1[] charges)
    {
        Session = topology.Session; GraphGeneration = topology.GraphGeneration;
        TopologyFingerprint = topology.Fingerprint; CapacityGrantId = topology.CapacityGrantId;
        CatalogFingerprint = catalog.Fingerprint;
        _capacityCharges = charges;
        _capacityScopes = charges.Select(static charge => CapacityScopeCanonicalCodecV1.Encode(charge.Scope)).ToArray();
        _nodeBindings = catalog.Entries.ToArray();
        CapacityCharges = Array.AsReadOnly(_capacityCharges); NodeBindings = Array.AsReadOnly(_nodeBindings);
        Fingerprint = ComputeFingerprint();
    }

    internal SessionAuthorityStampV1 Session { get; }
    internal GraphGenerationId GraphGeneration { get; }
    internal Hash256 TopologyFingerprint { get; }
    internal CapacityGrantId CapacityGrantId { get; }
    internal IReadOnlyList<CapacityChargeV1> CapacityCharges { get; }
    internal Hash256 CatalogFingerprint { get; }
    internal IReadOnlyList<GraphRuntimeExecutableFactoryBindingV1> NodeBindings { get; }
    internal Hash256 Fingerprint { get; }

    internal static GraphRuntimeExecutableCompileResultV1 Compile(GraphTopologyPlanV1 topology,
        Hash256 expectedTopologyFingerprint, GraphRuntimeExecutableCatalogResultV1 catalogResult,
        IEnumerable<CapacityChargeV1> capacityCharges)
    {
        if (topology is null || catalogResult is not GraphRuntimeExecutableCatalogResultV1.Created created ||
            capacityCharges is null)
            return new GraphRuntimeExecutableCompileResultV1.InvalidCatalog();
        if (expectedTopologyFingerprint == default || topology.Fingerprint != expectedTopologyFingerprint)
            return new GraphRuntimeExecutableCompileResultV1.TopologyMismatch();
        var charges = capacityCharges.Take(CapacityRequestV1.MaximumCharges + 1).ToArray();
        if (charges.Length is 0 or > CapacityRequestV1.MaximumCharges || charges.Any(static charge => charge is null))
            return new GraphRuntimeExecutableCompileResultV1.InvalidCatalog();
        Array.Sort(charges, CompareCharges);
        if (charges.Zip(charges.Skip(1), (left, right) => CompareCharges(left, right) == 0).Any(x => x) ||
            !topology.CapacityDimensions.SequenceEqual(charges.Select(static charge => charge.DimensionId).Distinct()))
            return new GraphRuntimeExecutableCompileResultV1.InvalidCatalog();

        var topologyKeys = topology.Nodes.Select(static node => node.Key).ToArray();
        var catalogKeys = created.Catalog.Entries.Select(static entry => entry.NodeKey).ToArray();
        foreach (var key in topologyKeys)
            if (!catalogKeys.Contains(key)) return new GraphRuntimeExecutableCompileResultV1.MissingFactory(key);
        foreach (var key in catalogKeys)
            if (!topologyKeys.Contains(key)) return new GraphRuntimeExecutableCompileResultV1.ExtraFactory(key);
        if (topologyKeys.Length != catalogKeys.Length || topologyKeys.Length > 64)
            return new GraphRuntimeExecutableCompileResultV1.InvalidCatalog();
        return new GraphRuntimeExecutableCompileResultV1.Compiled(new(topology, created.Catalog, charges));
    }

    private static int CompareCharges(CapacityChargeV1 left, CapacityChargeV1 right)
    {
        var compared = left.DimensionId.Value.CompareTo(right.DimensionId.Value);
        if (compared != 0) return compared;
        compared = CapacityScopeCanonicalCodecV1.Encode(left.Scope).AsSpan()
            .SequenceCompareTo(CapacityScopeCanonicalCodecV1.Encode(right.Scope));
        if (compared != 0) return compared;
        Span<byte> leftPurpose = stackalloc byte[16]; Span<byte> rightPurpose = stackalloc byte[16];
        if (!left.Purpose.TryWriteBytes(leftPurpose) || !right.Purpose.TryWriteBytes(rightPurpose))
            throw new InvalidOperationException("A validated capacity purpose lost its identity.");
        compared = leftPurpose.SequenceCompareTo(rightPurpose);
        return compared != 0 ? compared : left.Amount.CompareTo(right.Amount);
    }

    private Hash256 ComputeFingerprint()
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartArray(8); writer.WriteUInt64(1);
        writer.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(Session));
        WriteId(writer, GraphGeneration.TryWriteBytes); WriteHash(writer, TopologyFingerprint);
        WriteId(writer, CapacityGrantId.TryWriteBytes); writer.WriteStartArray(_capacityCharges.Length);
        for (var index = 0; index < _capacityCharges.Length; index++)
        {
            var charge = _capacityCharges[index]; writer.WriteStartArray(4); writer.WriteUInt64(charge.DimensionId.Value);
            writer.WriteEncodedValue(_capacityScopes[index]); WriteId(writer, charge.Purpose.TryWriteBytes);
            writer.WriteUInt64(checked((ulong)charge.Amount)); writer.WriteEndArray();
        }
        writer.WriteEndArray(); WriteHash(writer, CatalogFingerprint); writer.WriteStartArray(_nodeBindings.Length);
        foreach (var binding in _nodeBindings) GraphRuntimeExecutableFactoryCatalogV1.WriteBinding(writer, binding);
        writer.WriteEndArray(); writer.WriteEndArray();
        return GraphRuntimeExecutableFactoryCatalogV1.Hash(FingerprintDomain, writer.Encode());
    }

    private delegate bool IdWriter(Span<byte> bytes);
    private static void WriteId(CborWriter writer, IdWriter write)
    { Span<byte> bytes = stackalloc byte[16]; if (!write(bytes)) throw new InvalidOperationException(); writer.WriteByteString(bytes); }
    private static void WriteHash(CborWriter writer, Hash256 hash)
    { Span<byte> bytes = stackalloc byte[32]; hash.TryWriteBytes(bytes); writer.WriteByteString(bytes); }
}
