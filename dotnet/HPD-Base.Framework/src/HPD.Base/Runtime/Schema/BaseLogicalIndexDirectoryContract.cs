using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

/// <summary>Owns one logical index's generation and directory publication authority.</summary>
internal sealed record BaseLogicalIndexDirectoryAuthority
{
    internal required BaseLogicalIndexId IndexId { get; init; }
    internal required long IndexVersion { get; init; }
    internal required BaseLogicalIndexChecksum IndexChecksum { get; init; }
    internal required long Generation { get; init; }
    internal required BaseSchemaAuthorityChecksum LogicalPublicationChecksum { get; init; }
    internal required ImmutableArray<byte> PreviousDirectoryPublicationChecksum { get; init; }
    internal required ImmutableArray<byte> DirectoryPublicationChecksum { get; init; }
    internal required ImmutableArray<byte> MemberSetChecksum { get; init; }

    internal BaseLogicalIndexDirectoryAuthority DeepClone() => new()
    {
        IndexId = BaseLogicalIndexId.Create(IndexId.ToString()),
        IndexVersion = IndexVersion,
        IndexChecksum = BaseLogicalIndexChecksum.Create(IndexChecksum.ToArray()),
        Generation = Generation,
        LogicalPublicationChecksum = BaseSchemaAuthorityChecksum.Create(LogicalPublicationChecksum.ToArray()),
        PreviousDirectoryPublicationChecksum = PreviousDirectoryPublicationChecksum.ToArray().ToImmutableArray(),
        DirectoryPublicationChecksum = DirectoryPublicationChecksum.ToArray().ToImmutableArray(),
        MemberSetChecksum = MemberSetChecksum.ToArray().ToImmutableArray(),
    };
}

/// <summary>Owns the canonical retained representation of one required logical-index directory.</summary>
internal sealed record BaseLogicalIndexDirectory
{
    internal required ImmutableArray<BaseLogicalIndexDirectoryPosting> EqualityPostings { get; init; }
    internal required ImmutableArray<BaseLogicalIndexComparatorEntry> ComparatorEntries { get; init; }
    internal required ImmutableArray<byte> CanonicalEncoding { get; init; }
    internal required ImmutableArray<byte> MemberSetChecksum { get; init; }
    internal required BaseLogicalIndexDirectoryAccounting Accounting { get; init; }

    internal BaseLogicalIndexDirectory DeepClone() => new()
    {
        EqualityPostings = [.. EqualityPostings.Select(static value => value.DeepClone())],
        ComparatorEntries = [.. ComparatorEntries.Select(static value => value.DeepClone())],
        CanonicalEncoding = CanonicalEncoding.ToArray().ToImmutableArray(),
        MemberSetChecksum = MemberSetChecksum.ToArray().ToImmutableArray(),
        Accounting = Accounting with { },
    };
}

/// <summary>Owns one canonical equality-key posting.</summary>
internal sealed record BaseLogicalIndexDirectoryPosting
{
    internal required ImmutableArray<byte> EqualityKey { get; init; }
    internal required ImmutableArray<string> RecordIds { get; init; }
    internal required ImmutableArray<byte> Checksum { get; init; }

    internal BaseLogicalIndexDirectoryPosting DeepClone() => new()
    {
        EqualityKey = EqualityKey.ToArray().ToImmutableArray(),
        RecordIds = [.. RecordIds.Select(static value => new string(value.AsSpan()))],
        Checksum = Checksum.ToArray().ToImmutableArray(),
    };
}

/// <summary>Owns one comparator-ordered directory member.</summary>
internal sealed record BaseLogicalIndexComparatorEntry
{
    internal required ImmutableArray<BaseLogicalIndexComparatorPart> Parts { get; init; }
    internal required string RecordId { get; init; }
    internal required ImmutableArray<byte> CanonicalEncoding { get; init; }
    internal required ImmutableArray<byte> Checksum { get; init; }
    internal required RecordPayload Payload { get; init; }

    internal BaseLogicalIndexComparatorEntry DeepClone() => new()
    {
        Parts = [.. Parts.Select(static value => value.DeepClone())],
        RecordId = new string(RecordId.AsSpan()),
        CanonicalEncoding = CanonicalEncoding.ToArray().ToImmutableArray(),
        Checksum = Checksum.ToArray().ToImmutableArray(),
        Payload = RecordCloneHelpers.ClonePayload(Payload),
    };
}

/// <summary>Owns one canonical comparator part.</summary>
internal sealed record BaseLogicalIndexComparatorPart
{
    internal required int FieldOrdinal { get; init; }
    internal required byte State { get; init; }
    internal required string CodecId { get; init; }
    internal required ImmutableArray<byte> CanonicalValue { get; init; }

    internal BaseLogicalIndexComparatorPart DeepClone() => new()
    {
        FieldOrdinal = FieldOrdinal,
        State = State,
        CodecId = new string(CodecId.AsSpan()),
        CanonicalValue = CanonicalValue.ToArray().ToImmutableArray(),
    };
}

/// <summary>Contains exact canonical directory accounting.</summary>
internal sealed record BaseLogicalIndexDirectoryAccounting
{
    internal required long Records { get; init; }
    internal required long PredicateEvaluations { get; init; }
    internal required long Keys { get; init; }
    internal required long KeyBytes { get; init; }
    internal required long PostingKeys { get; init; }
    internal required long Postings { get; init; }
    internal required long ComparatorEntries { get; init; }
    internal required long Comparisons { get; init; }
    internal required long EvidenceBytes { get; init; }
    internal required long RetainedDirectoryBytes { get; init; }
    internal required long TransientBytes { get; init; }
}

/// <summary>Contains the exact limits applied before retaining canonical directory work.</summary>
internal sealed record BaseLogicalIndexDirectoryLimits
{
    internal required long MaximumRecords { get; init; }
    internal required long MaximumPredicateEvaluations { get; init; }
    internal required long MaximumKeys { get; init; }
    internal required long MaximumKeyBytes { get; init; }
    internal required int MaximumCanonicalKeyBytes { get; init; }
    internal required long MaximumPostingKeys { get; init; }
    internal required long MaximumPostings { get; init; }
    internal required int MaximumPostingRecordsPerKey { get; init; }
    internal required long MaximumComparatorEntries { get; init; }
    internal required long MaximumEvidenceBytes { get; init; }
    internal required long MaximumRetainedDirectoryBytes { get; init; }
    internal required long MaximumTransientBytes { get; init; }
}

/// <summary>Contains canonical bytes already retained by one prospective directory operation.</summary>
internal sealed record BaseLogicalIndexDirectoryProspectiveWork
{
    internal required long CapturedOldDirectoryBytes { get; init; }
    internal required long StagedTransitionBytes { get; init; }
    internal required long EvidenceBytes { get; init; }
}

/// <summary>Builds and validates the canonical L80 directory representation.</summary>
internal static class BaseLogicalIndexDirectoryContract
{
    private static readonly byte[] PostingPurpose = "base.logicalIndex.posting.v1\0"u8.ToArray();
    private static readonly byte[] ComparatorPurpose = "base.logicalIndex.comparatorEntry.v1\0"u8.ToArray();
    private static readonly byte[] MemberSetPurpose = "base.logicalIndex.memberSet.v1\0"u8.ToArray();
    private static readonly byte[] InitialPurpose = "base.logicalIndex.directoryInitial.v1\0"u8.ToArray();
    private static readonly byte[] NextPurpose = "base.logicalIndex.directoryNext.v1\0"u8.ToArray();

    internal const long EmptyDirectoryRetainedBytes = sizeof(int) * 2L;

    internal static BaseLogicalIndexDirectoryLimits ProductionLimits()
    {
        return Limits(BaseLogicalIndexProviderContract.BuiltInCapability());
    }

    internal static BaseLogicalIndexDirectoryLimits Limits(
        BaseLogicalIndexProviderCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (!BaseLogicalIndexProviderContract.ValidateCapability(capability) || !capability.Supported)
            throw new ArgumentException("base.logicalIndex.providerCapabilityInvalid", nameof(capability));
        return new BaseLogicalIndexDirectoryLimits
        {
            MaximumRecords = capability.MaximumIndexedRecordsPerCollection,
            MaximumPredicateEvaluations = capability.MaximumDirectoryPredicateEvaluationsPerPublication,
            MaximumKeys = capability.MaximumPostingsPerIndex,
            MaximumKeyBytes = capability.MaximumDirectoryKeyBytesPerIndex,
            MaximumCanonicalKeyBytes = capability.MaximumCanonicalKeyBytes,
            MaximumPostingKeys = capability.MaximumPostingsPerIndex,
            MaximumPostings = capability.MaximumPostingsPerIndex,
            MaximumPostingRecordsPerKey = capability.MaximumPostingRecordsPerKey,
            MaximumComparatorEntries = capability.MaximumPostingsPerIndex,
            MaximumEvidenceBytes = 0,
            MaximumRetainedDirectoryBytes = capability.MaximumDirectoryBytesPerIndex,
            MaximumTransientBytes = capability.MaximumDirectoryTransientBytesPerOperation,
        };
    }

    internal static bool TryCreate(
        CollectionDefinition collection,
        BaseLogicalIndexDefinition index,
        IEnumerable<(RecordId Id, RecordPayload Payload)> records,
        BaseLogicalIndexDirectoryLimits limits,
        out BaseLogicalIndexDirectory? directory)
        => TryCreate(collection, index, records, limits, new BaseLogicalIndexDirectoryProspectiveWork
        {
            CapturedOldDirectoryBytes = 0,
            StagedTransitionBytes = 0,
            EvidenceBytes = 0,
        }, out directory);

    internal static bool TryCreate(
        CollectionDefinition collection,
        BaseLogicalIndexDefinition index,
        IEnumerable<(RecordId Id, RecordPayload Payload)> records,
        BaseLogicalIndexDirectoryLimits limits,
        BaseLogicalIndexDirectoryProspectiveWork prospective,
        out BaseLogicalIndexDirectory? directory)
    {
        directory = null;
        try { return TryCreateCore(collection, index, records, limits, prospective, out directory); }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException
            or FormatException or ArgumentException)
        {
            directory = null;
            return false;
        }
    }

    private static bool TryCreateCore(
        CollectionDefinition collection,
        BaseLogicalIndexDefinition index,
        IEnumerable<(RecordId Id, RecordPayload Payload)> records,
        BaseLogicalIndexDirectoryLimits limits,
        BaseLogicalIndexDirectoryProspectiveWork prospective,
        out BaseLogicalIndexDirectory? directory)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(prospective);
        directory = null;
        if (!ValidLimits(limits) || prospective.CapturedOldDirectoryBytes < 0
            || prospective.StagedTransitionBytes < 0 || prospective.EvidenceBytes < 0
            || prospective.EvidenceBytes > limits.MaximumEvidenceBytes)
            return false;

        var distinct = new Dictionary<string, (RecordId Id, RecordPayload Payload)>(StringComparer.Ordinal);
        foreach ((RecordId id, RecordPayload payload) in records)
        {
            if (!id.IsValid || payload is null || !distinct.TryAdd(id.Value, (id, RecordCloneHelpers.ClonePayload(payload))))
                return false;
            if (distinct.Count > limits.MaximumRecords) return false;
        }

        var members = new List<(RecordId Id, RecordPayload Payload, byte[] Key, BaseLogicalIndexComparatorEntry Entry)>();
        long predicateEvaluations = 0;
        long keyBytes = 0;
        long comparatorDirectoryBytes = sizeof(int);
        foreach ((RecordId id, RecordPayload payload) in distinct.Values.OrderBy(static value => value.Id.Value, StringComparer.Ordinal))
        {
            predicateEvaluations = checked(predicateEvaluations + 1);
            if (predicateEvaluations > limits.MaximumPredicateEvaluations) return false;
            if (!BaseLogicalIndexEvaluator.Includes(collection, index, payload)) continue;
            byte[] key = BaseLogicalIndexEvaluator.Key(collection, index, payload);
            if (key.Length > limits.MaximumCanonicalKeyBytes) return false;
            keyBytes = checked(keyBytes + sizeof(int) + key.LongLength);
            if (members.Count + 1L > limits.MaximumKeys || keyBytes > limits.MaximumKeyBytes) return false;
            BaseLogicalIndexComparatorEntry entry;
            try { entry = CreateComparatorEntry(collection, index, id, payload); }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException or FormatException) { return false; }
            keyBytes = checked(keyBytes + entry.Parts.Sum(static value => (long)value.CanonicalValue.Length));
            comparatorDirectoryBytes = checked(comparatorDirectoryBytes + sizeof(int) + entry.CanonicalEncoding.Length + 32L);
            if (keyBytes > limits.MaximumKeyBytes || members.Count + 1L > limits.MaximumComparatorEntries
                || comparatorDirectoryBytes + sizeof(int) > limits.MaximumRetainedDirectoryBytes
                || !TransientWithin(prospective, comparatorDirectoryBytes + sizeof(int), limits))
                return false;
            members.Add((id, payload, key, entry));
        }

        var postingGroups = new SortedDictionary<byte[], List<string>>(UnsignedByteArrayComparer.Instance);
        long postingDirectoryBytes = sizeof(int);
        foreach ((RecordId id, _, byte[] key, _) in members)
        {
            if (!postingGroups.TryGetValue(key, out List<string>? ids))
            {
                if (postingGroups.Count + 1L > limits.MaximumPostingKeys) return false;
                long withKey = checked(postingDirectoryBytes + sizeof(int) + key.LongLength + sizeof(int) + 32L);
                if (checked(withKey + comparatorDirectoryBytes) > limits.MaximumRetainedDirectoryBytes
                    || !TransientWithin(prospective, checked(withKey + comparatorDirectoryBytes), limits))
                    return false;
                postingGroups.Add(key.ToArray(), ids = []);
                postingDirectoryBytes = withKey;
            }
            if (index.Unique && ids.Count != 0) return false;
            if (ids.Count + 1 > limits.MaximumPostingRecordsPerKey || members.Count > limits.MaximumPostings) return false;
            long withPosting = checked(postingDirectoryBytes + sizeof(int) + BaseStrictUtf8.Encode(id.Value).LongLength);
            long prospectiveDirectoryBytes = checked(withPosting + comparatorDirectoryBytes);
            if (prospectiveDirectoryBytes > limits.MaximumRetainedDirectoryBytes
                || !TransientWithin(prospective, prospectiveDirectoryBytes, limits))
                return false;
            postingDirectoryBytes = withPosting;
            ids.Add(new string(id.Value.AsSpan()));
        }

        var postings = ImmutableArray.CreateBuilder<BaseLogicalIndexDirectoryPosting>(postingGroups.Count);
        foreach ((byte[] key, List<string> ids) in postingGroups)
        {
            ids.Sort(StringComparer.Ordinal);
            byte[] postingEncoding = EncodePostingBody(key, ids);
            postings.Add(new BaseLogicalIndexDirectoryPosting
            {
                EqualityKey = key.ToArray().ToImmutableArray(),
                RecordIds = [.. ids.Select(static value => new string(value.AsSpan()))],
                Checksum = Hash(PostingPurpose, postingEncoding).ToImmutableArray(),
            });
        }

        long comparisons = 0;
        members.Sort((left, right) =>
        {
            comparisons = checked(comparisons + 1);
            return BaseLogicalIndexEvaluator.Compare(collection, index, left.Payload, left.Id, right.Payload, right.Id);
        });
        ImmutableArray<BaseLogicalIndexComparatorEntry> entries = [.. members.Select(static value => value.Entry.DeepClone())];
        ImmutableArray<BaseLogicalIndexDirectoryPosting> ownedPostings = postings.MoveToImmutable();
        byte[] encoding = EncodeDirectory(ownedPostings, entries);
        long transientBytes = checked(prospective.CapturedOldDirectoryBytes + prospective.StagedTransitionBytes
            + encoding.LongLength);
        if (encoding.LongLength > limits.MaximumRetainedDirectoryBytes || transientBytes > limits.MaximumTransientBytes)
            return false;
        var accounting = new BaseLogicalIndexDirectoryAccounting
        {
            Records = distinct.Count,
            PredicateEvaluations = predicateEvaluations,
            Keys = members.Count,
            KeyBytes = keyBytes,
            PostingKeys = ownedPostings.Length,
            Postings = members.Count,
            ComparatorEntries = entries.Length,
            Comparisons = comparisons,
            EvidenceBytes = prospective.EvidenceBytes,
            RetainedDirectoryBytes = encoding.LongLength,
            TransientBytes = transientBytes,
        };
        if (!Within(accounting, limits)) return false;
        directory = new BaseLogicalIndexDirectory
        {
            EqualityPostings = [.. ownedPostings.Select(static value => value.DeepClone())],
            ComparatorEntries = [.. entries.Select(static value => value.DeepClone())],
            CanonicalEncoding = encoding.ToImmutableArray(),
            MemberSetChecksum = Hash(MemberSetPurpose, encoding).ToImmutableArray(),
            Accounting = accounting,
        };
        return true;
    }

    internal static ImmutableArray<byte> InitialDirectoryPublication(BaseSchemaAuthorityChecksum logicalInitialPublication)
    {
        byte[] logical = logicalInitialPublication.ToArray();
        if (logical.Length != 32) throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        byte[] empty = EmptyMemberSetChecksum();
        return Hash(InitialPurpose, logical, empty).ToImmutableArray();
    }

    internal static BaseLogicalIndexDirectoryAuthority CreateInitialAuthority(
        BaseLogicalIndexDefinition definition,
        BaseSchemaAuthorityChecksum logicalInitialPublication,
        BaseLogicalIndexDirectory emptyDirectory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(emptyDirectory);
        if (!definition.Id.IsValid || definition.Version <= 0 || !definition.Checksum.IsValid
            || emptyDirectory.EqualityPostings.Length != 0 || emptyDirectory.ComparatorEntries.Length != 0
            || !emptyDirectory.CanonicalEncoding.AsSpan().SequenceEqual(EmptyDirectoryEncoding())
            || !emptyDirectory.MemberSetChecksum.AsSpan().SequenceEqual(EmptyMemberSetChecksum()))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        return new BaseLogicalIndexDirectoryAuthority
        {
            IndexId = BaseLogicalIndexId.Create(definition.Id.ToString()),
            IndexVersion = definition.Version,
            IndexChecksum = BaseLogicalIndexChecksum.Create(definition.Checksum.ToArray()),
            Generation = 1,
            LogicalPublicationChecksum = BaseSchemaAuthorityChecksum.Create(logicalInitialPublication.ToArray()),
            PreviousDirectoryPublicationChecksum = ImmutableArray<byte>.Empty,
            DirectoryPublicationChecksum = InitialDirectoryPublication(logicalInitialPublication),
            MemberSetChecksum = emptyDirectory.MemberSetChecksum.ToArray().ToImmutableArray(),
        };
    }

    internal static BaseLogicalIndexDirectoryAuthority NextAuthority(
        BaseLogicalIndexDirectoryAuthority prior,
        BaseSchemaAuthorityChecksum resultingLogicalPublication,
        BaseSchemaAuthorityChecksum provisionalChecksum,
        BaseLogicalIndexDirectory resultingDirectory)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(resultingDirectory);
        if (!AuthorityShapeValid(prior)) throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        ImmutableArray<byte> next = NextDirectoryPublication(
            prior.DirectoryPublicationChecksum.AsSpan(), resultingLogicalPublication,
            resultingDirectory.MemberSetChecksum.AsSpan(), provisionalChecksum);
        return new BaseLogicalIndexDirectoryAuthority
        {
            IndexId = BaseLogicalIndexId.Create(prior.IndexId.ToString()),
            IndexVersion = prior.IndexVersion,
            IndexChecksum = BaseLogicalIndexChecksum.Create(prior.IndexChecksum.ToArray()),
            Generation = checked(prior.Generation + 1),
            LogicalPublicationChecksum = BaseSchemaAuthorityChecksum.Create(resultingLogicalPublication.ToArray()),
            PreviousDirectoryPublicationChecksum = prior.DirectoryPublicationChecksum.ToArray().ToImmutableArray(),
            DirectoryPublicationChecksum = next,
            MemberSetChecksum = resultingDirectory.MemberSetChecksum.ToArray().ToImmutableArray(),
        };
    }

    internal static bool AuthorityShapeValid(BaseLogicalIndexDirectoryAuthority value) =>
        value is not null && value.IndexId.IsValid && value.IndexVersion > 0 && value.IndexChecksum.IsValid
        && value.Generation > 0 && value.LogicalPublicationChecksum.IsValid
        && value.DirectoryPublicationChecksum.Length == 32 && value.MemberSetChecksum.Length == 32
        && (value.Generation == 1
            ? value.PreviousDirectoryPublicationChecksum.IsEmpty
            : value.PreviousDirectoryPublicationChecksum.Length == 32);

    internal static ImmutableArray<byte> NextDirectoryPublication(
        ReadOnlySpan<byte> priorDirectoryPublication,
        BaseSchemaAuthorityChecksum resultingLogicalPublication,
        ReadOnlySpan<byte> resultingMemberSetChecksum,
        BaseSchemaAuthorityChecksum provisionalChecksum)
    {
        byte[] logical = resultingLogicalPublication.ToArray();
        byte[] provisional = provisionalChecksum.ToArray();
        if (priorDirectoryPublication.Length != 32 || logical.Length != 32
            || resultingMemberSetChecksum.Length != 32 || provisional.Length != 32)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        return Hash(NextPurpose, priorDirectoryPublication.ToArray(), logical, resultingMemberSetChecksum.ToArray(), provisional).ToImmutableArray();
    }

    internal static byte[] EmptyDirectoryEncoding()
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteInt32(writer, 0); WriteInt32(writer, 0);
        return writer.WrittenSpan.ToArray();
    }

    internal static byte[] EmptyMemberSetChecksum() => Hash(MemberSetPurpose, EmptyDirectoryEncoding());

    internal static bool TryFindPosting(
        BaseLogicalIndexDirectory directory,
        ReadOnlySpan<byte> equalityKey,
        out BaseLogicalIndexDirectoryPosting? posting)
    {
        ArgumentNullException.ThrowIfNull(directory);
        int lower = 0, upper = directory.EqualityPostings.Length - 1;
        while (lower <= upper)
        {
            int middle = lower + ((upper - lower) / 2);
            BaseLogicalIndexDirectoryPosting candidate = directory.EqualityPostings[middle];
            int comparison = candidate.EqualityKey.AsSpan().SequenceCompareTo(equalityKey);
            if (comparison == 0) { posting = candidate.DeepClone(); return true; }
            if (comparison < 0) lower = middle + 1; else upper = middle - 1;
        }
        posting = null;
        return false;
    }

    internal static bool Validate(
        CollectionDefinition collection,
        BaseLogicalIndexDefinition index,
        BaseLogicalIndexDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(directory);
        try
        {
            if (!TryCreate(collection, index, directory.ComparatorEntries.Select(static entry =>
                    (RecordId.Create(entry.RecordId), entry.Payload)), ProductionLimits(),
                    out BaseLogicalIndexDirectory? recomputed)
                || recomputed is null
                || !recomputed.CanonicalEncoding.AsSpan().SequenceEqual(directory.CanonicalEncoding.AsSpan())
                || !recomputed.MemberSetChecksum.AsSpan().SequenceEqual(directory.MemberSetChecksum.AsSpan())
                || recomputed.EqualityPostings.Length != directory.EqualityPostings.Length
                || recomputed.ComparatorEntries.Length != directory.ComparatorEntries.Length)
                return false;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
            or FormatException or OverflowException)
        {
            return false;
        }
    }

    internal static int Compare(
        CollectionDefinition collection,
        BaseLogicalIndexDefinition index,
        BaseLogicalIndexComparatorEntry left,
        BaseLogicalIndexComparatorEntry right)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return BaseLogicalIndexEvaluator.Compare(
            collection, index, left.Payload, RecordId.Create(left.RecordId),
            right.Payload, RecordId.Create(right.RecordId));
    }

    internal static bool Within(BaseLogicalIndexDirectoryAccounting value, BaseLogicalIndexDirectoryLimits limits) =>
        value.Records is >= 0 && value.Records <= limits.MaximumRecords
        && value.PredicateEvaluations is >= 0 && value.PredicateEvaluations <= limits.MaximumPredicateEvaluations
        && value.Keys is >= 0 && value.Keys <= limits.MaximumKeys
        && value.KeyBytes is >= 0 && value.KeyBytes <= limits.MaximumKeyBytes
        && value.PostingKeys is >= 0 && value.PostingKeys <= limits.MaximumPostingKeys
        && value.Postings is >= 0 && value.Postings <= limits.MaximumPostings
        && value.ComparatorEntries is >= 0 && value.ComparatorEntries <= limits.MaximumComparatorEntries
        && value.Comparisons >= 0
        && value.EvidenceBytes is >= 0 && value.EvidenceBytes <= limits.MaximumEvidenceBytes
        && value.RetainedDirectoryBytes is >= 0 && value.RetainedDirectoryBytes <= limits.MaximumRetainedDirectoryBytes
        && value.TransientBytes is >= 0 && value.TransientBytes <= limits.MaximumTransientBytes;

    private static BaseLogicalIndexComparatorEntry CreateComparatorEntry(
        CollectionDefinition collection, BaseLogicalIndexDefinition index, RecordId id, RecordPayload payload)
    {
        FieldDefinition[] fields = (collection.Fields ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        var parts = ImmutableArray.CreateBuilder<BaseLogicalIndexComparatorPart>(index.Parts.Length);
        foreach (BaseLogicalIndexPart definition in index.Parts)
        {
            FieldDefinition field = fields[definition.FieldOrdinal];
            BaseScalarKind kind = field.ScalarKind ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
            BaseScalarCodecAuthority codec = field.ScalarCodec ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
            JsonElement value = default;
            bool present = payload.Fields?.TryGetValue(field.WireName, out value) == true;
            byte state = !present ? (byte)0 : value.ValueKind == JsonValueKind.Null ? (byte)1 : (byte)2;
            byte[] canonical = state == 2 ? BaseScalarCanonical.Encode(kind, value) : [];
            parts.Add(new BaseLogicalIndexComparatorPart
            {
                FieldOrdinal = definition.FieldOrdinal,
                State = state,
                CodecId = new string(codec.Id.ToString().AsSpan()),
                CanonicalValue = canonical.ToImmutableArray(),
            });
        }
        ImmutableArray<BaseLogicalIndexComparatorPart> owned = parts.MoveToImmutable();
        byte[] encoding = EncodeComparatorEntry(owned, id.Value);
        return new BaseLogicalIndexComparatorEntry
        {
            Parts = [.. owned.Select(static value => value.DeepClone())],
            RecordId = new string(id.Value.AsSpan()),
            CanonicalEncoding = encoding.ToImmutableArray(),
            Checksum = Hash(ComparatorPurpose, encoding).ToImmutableArray(),
            Payload = RecordCloneHelpers.ClonePayload(payload),
        };
    }

    private static byte[] EncodeDirectory(
        ImmutableArray<BaseLogicalIndexDirectoryPosting> postings,
        ImmutableArray<BaseLogicalIndexComparatorEntry> entries)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteInt32(writer, postings.Length);
        foreach (BaseLogicalIndexDirectoryPosting posting in postings)
        {
            WriteBytes(writer, posting.EqualityKey.AsSpan());
            WriteInt32(writer, posting.RecordIds.Length);
            foreach (string id in posting.RecordIds) WriteString(writer, id);
            writer.Write(posting.Checksum.AsSpan());
        }
        WriteInt32(writer, entries.Length);
        foreach (BaseLogicalIndexComparatorEntry entry in entries)
        {
            WriteBytes(writer, entry.CanonicalEncoding.AsSpan());
            writer.Write(entry.Checksum.AsSpan());
        }
        return writer.WrittenSpan.ToArray();
    }

    private static byte[] EncodePostingBody(byte[] key, IReadOnlyList<string> ids)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteBytes(writer, key); WriteInt32(writer, ids.Count);
        foreach (string id in ids) WriteString(writer, id);
        return writer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeComparatorEntry(ImmutableArray<BaseLogicalIndexComparatorPart> parts, string recordId)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteInt32(writer, parts.Length);
        foreach (BaseLogicalIndexComparatorPart part in parts)
        {
            WriteInt32(writer, part.FieldOrdinal);
            writer.Write([part.State]);
            WriteString(writer, part.CodecId);
            WriteBytes(writer, part.CanonicalValue.AsSpan());
        }
        WriteString(writer, recordId);
        return writer.WrittenSpan.ToArray();
    }

    private static bool ValidLimits(BaseLogicalIndexDirectoryLimits value) =>
        value.MaximumRecords >= 0 && value.MaximumPredicateEvaluations >= 0 && value.MaximumKeys >= 0
        && value.MaximumKeyBytes >= 0 && value.MaximumPostingKeys >= 0 && value.MaximumPostings >= 0
        && value.MaximumPostingRecordsPerKey >= 0 && value.MaximumComparatorEntries >= 0
        && value.MaximumCanonicalKeyBytes > 0
        && value.MaximumEvidenceBytes >= 0 && value.MaximumRetainedDirectoryBytes >= 8 && value.MaximumTransientBytes >= 0;

    private static bool TransientWithin(
        BaseLogicalIndexDirectoryProspectiveWork prospective,
        long proposedDirectoryBytes,
        BaseLogicalIndexDirectoryLimits limits)
    {
        try
        {
            return checked(prospective.CapturedOldDirectoryBytes + prospective.StagedTransitionBytes
                + proposedDirectoryBytes) <= limits.MaximumTransientBytes;
        }
        catch (OverflowException) { return false; }
    }

    private static byte[] Hash(byte[] purpose, params byte[][] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(purpose);
        foreach (byte[] value in values) hash.AppendData(value);
        return hash.GetHashAndReset();
    }

    private static void WriteBytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    { WriteInt32(writer, value.Length); writer.Write(value); }

    private static void WriteString(ArrayBufferWriter<byte> writer, string value)
    { WriteBytes(writer, BaseStrictUtf8.Encode(value)); }

    private static void WriteInt32(ArrayBufferWriter<byte> writer, int value)
    { Span<byte> span = writer.GetSpan(sizeof(int)); BinaryPrimitives.WriteInt32BigEndian(span, value); writer.Advance(sizeof(int)); }

    private sealed class UnsignedByteArrayComparer : IComparer<byte[]>
    {
        internal static UnsignedByteArrayComparer Instance { get; } = new();
        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
