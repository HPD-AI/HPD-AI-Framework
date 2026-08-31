using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace HPD.Base;

/// <summary>Derives one deterministic L80 equality-point plan from a policy-constrained query.</summary>
internal static class BaseLogicalIndexPointPlanContract
{
    private static readonly byte[] ProofPurpose = "base.logicalIndex.pointPredicateProof.v1"u8.ToArray();

    internal static BaseLogicalIndexPointSelection? Derive(
        CollectionDefinition collection,
        FilterExpression? filter)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (filter is null || collection.Indexes is not { Length: > 0 }) return null;
        FieldDefinition[] fields = (collection.Fields ?? [])
            .OrderBy(static value => value.Id, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, FieldDefinition> fieldsById = fields.ToDictionary(
            static value => value.Id, StringComparer.Ordinal);
        var raw = new List<ProofLeaf>();
        if (!Flatten(filter, fieldsById, raw) || raw.Count is 0 or > 128) return null;

        ProofLeaf[] leaves = raw.OrderBy(static value => value.Encoding, UnsignedBytes.Instance)
            .Distinct(ProofLeafEncodingComparer.Instance)
            .Select((value, ordinal) => value with { Ordinal = ordinal })
            .ToArray();
        if (!TryBuildFieldProofs(leaves, out Dictionary<string, FieldProof>? proofs)) return null;

        return (collection.Indexes ?? [])
            .Where(static index => index.StoreRequired)
            .Select(index => TryCandidate(collection, fields, index, proofs, out BaseLogicalIndexPointSelection? candidate)
                ? candidate : null)
            .Where(static value => value is not null)
            .OrderBy(static value => value!.IndexId)
            .ThenBy(static value => value!.IndexVersion)
            .ThenBy(static value => value!.IndexChecksum.ToArray(), UnsignedBytes.Instance)
            .FirstOrDefault();
    }

    internal static BaseLogicalIndexPointSelection Clone(BaseLogicalIndexPointSelection value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with
        {
            IndexId = BaseLogicalIndexId.Create(value.IndexId.ToString()),
            IndexChecksum = BaseLogicalIndexChecksum.Create(value.IndexChecksum.ToArray()),
            EqualityParts = value.EqualityParts.Select(CloneValue).ToImmutableArray(),
            EqualityKey = value.EqualityKey.ToArray().ToImmutableArray(),
            PredicateConjunctChecksum = value.PredicateConjunctChecksum.ToArray().ToImmutableArray(),
        };
    }

    private static bool TryCandidate(
        CollectionDefinition collection,
        FieldDefinition[] fields,
        BaseLogicalIndexDefinition index,
        Dictionary<string, FieldProof> proofs,
        out BaseLogicalIndexPointSelection? candidate)
    {
        candidate = null;
        if (!index.Id.IsValid || index.Version <= 0 || !index.Checksum.IsValid
            || index.Parts.IsDefaultOrEmpty || index.Parts.Length > 8
            || !string.Equals(index.CollectionId, collection.Id, StringComparison.Ordinal)) return false;
        var parts = ImmutableArray.CreateBuilder<QueryValue>(index.Parts.Length);
        var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var usedOrdinals = new SortedSet<int>();
        foreach (BaseLogicalIndexPart part in index.Parts)
        {
            if (part.FieldOrdinal < 0 || part.FieldOrdinal >= fields.Length) return false;
            FieldDefinition field = fields[part.FieldOrdinal];
            if (!proofs.TryGetValue(field.Id, out FieldProof? proof) || proof.Value is null) return false;
            QueryValue value = proof.Value;
            if (value.Kind == QueryValueKind.Null)
            {
                if (field.Nullability != BaseFieldNullability.Nullable) return false;
                payload[field.WireName] = NullElement();
            }
            else
            {
                if (!TryElement(field, value, out JsonElement element)) return false;
                payload[field.WireName] = element;
            }
            parts.Add(CloneValue(value));
            usedOrdinals.Add(proof.ValueOrdinal);
        }

        var matchedNodes = new SortedSet<string>(StringComparer.Ordinal);
        Dictionary<BaseIndexPredicateId, BaseIndexPredicateNode> nodes;
        try { nodes = index.MembershipPredicate.Nodes.ToDictionary(static value => value.Id); }
        catch (ArgumentException) { return false; }
        if (nodes.Count is 0 or > 128 || !nodes.TryGetValue(index.MembershipPredicate.Root, out BaseIndexPredicateNode? root)
            || !Proves(root, nodes, fields, proofs, usedOrdinals, matchedNodes, new HashSet<BaseIndexPredicateId>()))
            return false;

        byte[] equalityKey;
        try
        {
            equalityKey = BaseLogicalIndexEvaluator.Key(collection, index, new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = payload,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException
            or ArgumentException or OverflowException or JsonException)
        {
            return false;
        }
        byte[] equalityKeyChecksum = SHA256.HashData(equalityKey);
        byte[] proofChecksum = ProofChecksum(
            index.Checksum.ToArray(), equalityKeyChecksum, usedOrdinals, matchedNodes);
        candidate = new BaseLogicalIndexPointSelection
        {
            IndexId = BaseLogicalIndexId.Create(index.Id.ToString()),
            IndexVersion = index.Version,
            IndexChecksum = BaseLogicalIndexChecksum.Create(index.Checksum.ToArray()),
            EqualityParts = parts.MoveToImmutable(),
            EqualityKey = equalityKey.ToImmutableArray(),
            PredicateConjunctChecksum = proofChecksum.ToImmutableArray(),
        };
        return true;
    }

    private static bool Flatten(
        FilterExpression expression,
        IReadOnlyDictionary<string, FieldDefinition> fields,
        List<ProofLeaf> leaves)
    {
        if (expression.Kind == FilterNodeKind.And)
        {
            if (expression.Children is not { Length: > 0 }) return false;
            foreach (FilterExpression child in expression.Children)
                if (child is null || !Flatten(child, fields, leaves)) return false;
            return true;
        }
        if (expression.Kind is not (FilterNodeKind.Compare or FilterNodeKind.IsNull or FilterNodeKind.IsDefined)
            || expression.Field is null || !fields.TryGetValue(expression.Field, out FieldDefinition? field)
            || expression.Kind == FilterNodeKind.Compare && expression.Operator != FilterOperator.Equal
            || expression.Kind == FilterNodeKind.Compare && expression.Value is null
            || expression.Kind == FilterNodeKind.Compare && expression.Value?.Kind == QueryValueKind.Null
            || expression.Kind != FilterNodeKind.Compare && expression.Value is not null
            || expression.Children is not null || expression.Values is not null
            || expression.ModuleId is not null || expression.Name is not null || expression.Arguments is not null)
            return false;
        QueryValue? value = expression.Kind switch
        {
            FilterNodeKind.Compare => expression.Value,
            FilterNodeKind.IsNull => new QueryValue { Kind = QueryValueKind.Null },
            _ => null,
        };
        if (value is not null && value.Kind != QueryValueKind.Null
            && !TryElement(field, value, out _)) return false;
        byte[] encoding = EncodeLeaf(field.Id, expression.Kind, expression.Operator, value, field);
        leaves.Add(new ProofLeaf(field.Id, expression.Kind, value is null ? null : CloneValue(value),
            value is null or { Kind: QueryValueKind.Null } ? [] : Canonical(field, value), encoding, -1));
        return true;
    }

    private static bool TryBuildFieldProofs(
        IEnumerable<ProofLeaf> leaves,
        out Dictionary<string, FieldProof> proofs)
    {
        proofs = new Dictionary<string, FieldProof>(StringComparer.Ordinal);
        foreach (IGrouping<string, ProofLeaf> group in leaves.GroupBy(static value => value.FieldId, StringComparer.Ordinal))
        {
            ProofLeaf[] values = group.Where(static value => value.Kind is FilterNodeKind.Compare or FilterNodeKind.IsNull).ToArray();
            if (values.Select(static value => value.Encoding).Distinct(UnsignedByteEquality.Instance).Count() > 1)
                return false;
            ProofLeaf? chosen = values.FirstOrDefault();
            int definedOrdinal = group.Where(static value => value.Kind == FilterNodeKind.IsDefined)
                .Select(static value => value.Ordinal).DefaultIfEmpty(-1).Min();
            proofs.Add(group.Key, new FieldProof(chosen?.Value, chosen?.CanonicalValue ?? [],
                chosen?.Ordinal ?? -1, definedOrdinal));
        }
        return true;
    }

    private static bool Proves(
        BaseIndexPredicateNode node,
        IReadOnlyDictionary<BaseIndexPredicateId, BaseIndexPredicateNode> nodes,
        FieldDefinition[] fields,
        IReadOnlyDictionary<string, FieldProof> proofs,
        SortedSet<int> ordinals,
        SortedSet<string> matchedNodes,
        HashSet<BaseIndexPredicateId> path)
    {
        if (!path.Add(node.Id)) return false;
        matchedNodes.Add(node.Id.ToString());
        bool result = node.Kind switch
        {
            BaseIndexPredicateNodeKind.True => true,
            BaseIndexPredicateNodeKind.IsDefined => TryProof(node, fields, proofs, out FieldProof defined)
                && AddOrdinal(ordinals, defined.ValueOrdinal >= 0 ? defined.ValueOrdinal : defined.DefinedOrdinal),
            BaseIndexPredicateNodeKind.IsNotNull => TryProof(node, fields, proofs, out FieldProof notNull)
                && notNull.Value is { Kind: not QueryValueKind.Null }
                && AddOrdinal(ordinals, notNull.ValueOrdinal),
            BaseIndexPredicateNodeKind.Equal => TryProof(node, fields, proofs, out FieldProof equal)
                && equal.Value is { Kind: not QueryValueKind.Null }
                && node.Literal is not null
                && equal.CanonicalValue.AsSpan().SequenceEqual(node.Literal.CanonicalBytes.AsSpan())
                && AddOrdinal(ordinals, equal.ValueOrdinal),
            BaseIndexPredicateNodeKind.And => node.Children.Length > 0 && node.Children.All(child =>
                nodes.TryGetValue(child, out BaseIndexPredicateNode? childNode)
                && Proves(childNode, nodes, fields, proofs, ordinals, matchedNodes, path)),
            _ => false,
        };
        path.Remove(node.Id);
        return result;
    }

    private static bool TryProof(
        BaseIndexPredicateNode node,
        FieldDefinition[] fields,
        IReadOnlyDictionary<string, FieldProof> proofs,
        out FieldProof proof)
    {
        proof = null!;
        if (node.FieldOrdinal is not { } ordinal || ordinal < 0 || ordinal >= fields.Length) return false;
        return proofs.TryGetValue(fields[ordinal].Id, out proof!);
    }

    private static bool AddOrdinal(SortedSet<int> ordinals, int ordinal)
    {
        if (ordinal < 0) return false;
        ordinals.Add(ordinal);
        return true;
    }

    private static byte[] ProofChecksum(
        byte[] indexChecksum,
        byte[] equalityKeyChecksum,
        IEnumerable<int> ordinals,
        IEnumerable<string> nodeIds)
    {
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(indexChecksum);
        writer.Write(equalityKeyChecksum);
        int[] ownedOrdinals = ordinals.ToArray();
        I32(writer, ownedOrdinals.Length);
        foreach (int ordinal in ownedOrdinals) I32(writer, ordinal);
        string[] ownedNodes = nodeIds.ToArray();
        I32(writer, ownedNodes.Length);
        foreach (string id in ownedNodes) Bytes(writer, BaseStrictUtf8.Encode(id));
        return SHA256.HashData([.. ProofPurpose, .. writer.WrittenSpan]);
    }

    private static byte[] EncodeLeaf(
        string fieldId,
        FilterNodeKind kind,
        FilterOperator @operator,
        QueryValue? value,
        FieldDefinition field)
    {
        var writer = new ArrayBufferWriter<byte>();
        Bytes(writer, BaseStrictUtf8.Encode(fieldId));
        I32(writer, (int)kind);
        I32(writer, kind == FilterNodeKind.Compare ? (int)@operator : 0);
        writer.Write([value is null ? (byte)0 : (byte)1]);
        if (value is not null)
        {
            I32(writer, (int)value.Kind);
            Bytes(writer, value.Kind == QueryValueKind.Null ? [] : Canonical(field, value));
        }
        return writer.WrittenSpan.ToArray();
    }

    private static byte[] Canonical(FieldDefinition field, QueryValue value)
    {
        if (!TryElement(field, value, out JsonElement element)
            || field.ScalarKind is not { } kind)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        return BaseScalarCanonical.Encode(kind, element);
    }

    private static bool TryElement(FieldDefinition field, QueryValue value, out JsonElement element)
    {
        element = default;
        if (field.ScalarKind is not { } kind || value.Kind == QueryValueKind.Null) return false;
        bool shape = kind switch
        {
            BaseScalarKind.String or BaseScalarKind.Binary or BaseScalarKind.ClosedEnum or BaseScalarKind.ModuleGeneration =>
                value.Kind == QueryValueKind.String && value.String is not null,
            BaseScalarKind.RecordId or BaseScalarKind.Guid =>
                value.Kind == QueryValueKind.Id && value.Id is not null,
            BaseScalarKind.Int32 or BaseScalarKind.Int64 or BaseScalarKind.UInt32 or BaseScalarKind.UInt64 =>
                value.Kind == QueryValueKind.Integer && value.Integer is not null,
            BaseScalarKind.Decimal => value.Kind == QueryValueKind.Decimal && value.Decimal is not null,
            BaseScalarKind.Boolean => value.Kind == QueryValueKind.Boolean && value.Boolean is not null,
            BaseScalarKind.UtcDateTime => value.Kind == QueryValueKind.DateTime && value.DateTime is not null,
            BaseScalarKind.CanonicalJson => value.Kind == QueryValueKind.CanonicalJson && !value.CanonicalJsonUtf8.IsDefault,
            _ => false,
        };
        if (!shape) return false;
        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                switch (value.Kind)
                {
                    case QueryValueKind.String: writer.WriteStringValue(value.String); break;
                    case QueryValueKind.Id: writer.WriteStringValue(value.Id); break;
                    case QueryValueKind.Boolean: writer.WriteBooleanValue(value.Boolean!.Value); break;
                    case QueryValueKind.Integer: writer.WriteNumberValue(value.Integer!.Value); break;
                    case QueryValueKind.Decimal: writer.WriteRawValue(value.Decimal!, skipInputValidation: false); break;
                    case QueryValueKind.DateTime:
                        writer.WriteStringValue(value.DateTime!.Value.UtcDateTime.ToString(
                            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case QueryValueKind.CanonicalJson:
                        writer.WriteRawValue(value.CanonicalJsonUtf8.AsSpan(), skipInputValidation: false);
                        break;
                    default: return false;
                }
            }
            using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
            element = document.RootElement.Clone();
            _ = BaseScalarCanonical.Encode(kind, element);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or ArgumentException or OverflowException)
        {
            element = default;
            return false;
        }
    }

    private static JsonElement NullElement()
    {
        using JsonDocument document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }

    private static QueryValue CloneValue(QueryValue value) => value with
    {
        String = value.String is null ? null : new string(value.String.AsSpan()),
        Decimal = value.Decimal is null ? null : new string(value.Decimal.AsSpan()),
        Id = value.Id is null ? null : new string(value.Id.AsSpan()),
        Array = value.Array?.Select(CloneValue).ToArray(),
        SubjectId = value.SubjectId is null ? null : new string(value.SubjectId.AsSpan()),
        SubjectAuthorityEpoch = value.SubjectAuthorityEpoch is null ? null : new string(value.SubjectAuthorityEpoch.AsSpan()),
        SubjectIncarnation = value.SubjectIncarnation is null ? null : new string(value.SubjectIncarnation.AsSpan()),
        CanonicalJsonUtf8 = value.CanonicalJsonUtf8.IsDefault
            ? default : value.CanonicalJsonUtf8.ToArray().ToImmutableArray(),
    };

    private static void Bytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        I32(writer, value.Length);
        writer.Write(value);
    }

    private static void I32(ArrayBufferWriter<byte> writer, int value)
    {
        Span<byte> span = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        writer.Advance(sizeof(int));
    }

    private sealed record ProofLeaf(
        string FieldId,
        FilterNodeKind Kind,
        QueryValue? Value,
        byte[] CanonicalValue,
        byte[] Encoding,
        int Ordinal);

    private sealed record FieldProof(
        QueryValue? Value,
        byte[] CanonicalValue,
        int ValueOrdinal,
        int DefinedOrdinal);

    private sealed class UnsignedBytes : IComparer<byte[]>
    {
        internal static UnsignedBytes Instance { get; } = new();
        public int Compare(byte[]? left, byte[]? right) => left.AsSpan().SequenceCompareTo(right);
    }

    private sealed class UnsignedByteEquality : IEqualityComparer<byte[]>
    {
        internal static UnsignedByteEquality Instance { get; } = new();
        public bool Equals(byte[]? left, byte[]? right) => left.AsSpan().SequenceEqual(right);
        public int GetHashCode(byte[] value)
        {
            var hash = new HashCode();
            hash.AddBytes(value);
            return hash.ToHashCode();
        }
    }

    private sealed class ProofLeafEncodingComparer : IEqualityComparer<ProofLeaf>
    {
        internal static ProofLeafEncodingComparer Instance { get; } = new();
        public bool Equals(ProofLeaf? left, ProofLeaf? right) =>
            left is not null && right is not null && left.Encoding.AsSpan().SequenceEqual(right.Encoding);
        public int GetHashCode(ProofLeaf value) => UnsignedByteEquality.Instance.GetHashCode(value.Encoding);
    }

}
