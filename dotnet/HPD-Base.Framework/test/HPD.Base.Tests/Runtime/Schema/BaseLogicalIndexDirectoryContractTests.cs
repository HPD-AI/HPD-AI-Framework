using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Tests.Schema;

public sealed class BaseLogicalIndexDirectoryContractTests
{
    [Fact]
    public void Directory_owns_postings_and_uses_the_L54_comparator_order()
    {
        (CollectionDefinition collection, BaseLogicalIndexDefinition index) = Contract(unique: false);
        RecordPayload one = Payload("a", "x", 1);
        RecordPayload two = Payload("a", "x", 2);
        RecordPayload three = Payload("b", null, 3);
        RecordPayload four = Payload("b", "x", 4);
        var source = new List<(RecordId Id, RecordPayload Payload)>
        {
            (Id(1), one), (Id(2), two), (Id(3), three), (Id(4), four),
        };

        Assert.True(BaseLogicalIndexDirectoryContract.TryCreate(collection, index, source, Limits, out BaseLogicalIndexDirectory? directory));
        Assert.NotNull(directory);
        Assert.Equal([Id(3).Value, Id(4).Value, Id(1).Value, Id(2).Value],
            directory.ComparatorEntries.Select(static value => value.RecordId));
        Assert.Equal(3, directory.EqualityPostings.Length);
        Assert.Equal(4, directory.Accounting.Records);
        Assert.Equal(4, directory.Accounting.Postings);
        Assert.Equal(4, directory.Accounting.ComparatorEntries);
        Assert.Equal(directory.CanonicalEncoding.Length, directory.Accounting.RetainedDirectoryBytes);
        Assert.True(BaseLogicalIndexDirectoryContract.Compare(
            collection, index, directory.ComparatorEntries[0], directory.ComparatorEntries[1]) < 0);
        byte[] firstKey = directory.EqualityPostings[0].EqualityKey.ToArray();
        Assert.True(BaseLogicalIndexDirectoryContract.TryFindPosting(directory, firstKey,
            out BaseLogicalIndexDirectoryPosting? found));
        Assert.Equal(directory.EqualityPostings[0].Checksum.ToArray(), found!.Checksum.ToArray());
        firstKey.AsSpan().Fill(0xff);
        Assert.False(BaseLogicalIndexDirectoryContract.TryFindPosting(directory, firstKey, out _));

        byte[] retainedEncoding = directory.CanonicalEncoding.ToArray();
        byte[] retainedMemberSet = directory.MemberSetChecksum.ToArray();
        one.Fields!["tenant"] = Element("z");
        source.Clear();
        BaseLogicalIndexDirectory clone = directory.DeepClone();
        byte[] outward = clone.EqualityPostings[0].EqualityKey.ToArray();
        outward[0] ^= 0xff;
        clone.ComparatorEntries[0].Payload.Fields!["tenant"] = Element("z");

        Assert.Equal(retainedEncoding, directory.CanonicalEncoding.ToArray());
        Assert.Equal(retainedMemberSet, directory.MemberSetChecksum.ToArray());
        Assert.Equal(retainedEncoding, clone.CanonicalEncoding.ToArray());
        Assert.Equal(retainedMemberSet, clone.MemberSetChecksum.ToArray());
        Assert.NotEqual("z", directory.ComparatorEntries[0].Payload.Fields!["tenant"].GetString());
    }

    [Fact]
    public void Directory_framing_and_all_checksums_match_the_normative_encoding()
    {
        (CollectionDefinition collection, BaseLogicalIndexDefinition index) = Contract(unique: false);
        Assert.True(BaseLogicalIndexDirectoryContract.TryCreate(collection, index,
            [(Id(2), Payload("a", "x", 2)), (Id(1), Payload("a", "x", 1))], Limits,
            out BaseLogicalIndexDirectory? directory));
        Assert.NotNull(directory);

        foreach (BaseLogicalIndexDirectoryPosting posting in directory.EqualityPostings)
        {
            var body = new ArrayBufferWriter<byte>();
            Bytes(body, posting.EqualityKey.AsSpan());
            I32(body, posting.RecordIds.Length);
            foreach (string id in posting.RecordIds) Text(body, id);
            Assert.Equal(Hash("base.logicalIndex.posting.v1\0", body.WrittenSpan), posting.Checksum.ToArray());
        }
        foreach (BaseLogicalIndexComparatorEntry entry in directory.ComparatorEntries)
            Assert.Equal(Hash("base.logicalIndex.comparatorEntry.v1\0", entry.CanonicalEncoding.AsSpan()), entry.Checksum.ToArray());
        Assert.Equal(Hash("base.logicalIndex.memberSet.v1\0", directory.CanonicalEncoding.AsSpan()), directory.MemberSetChecksum.ToArray());

        var expected = new ArrayBufferWriter<byte>();
        I32(expected, directory.EqualityPostings.Length);
        foreach (BaseLogicalIndexDirectoryPosting posting in directory.EqualityPostings)
        {
            Bytes(expected, posting.EqualityKey.AsSpan()); I32(expected, posting.RecordIds.Length);
            foreach (string id in posting.RecordIds) Text(expected, id);
            expected.Write(posting.Checksum.AsSpan());
        }
        I32(expected, directory.ComparatorEntries.Length);
        foreach (BaseLogicalIndexComparatorEntry entry in directory.ComparatorEntries)
        { Bytes(expected, entry.CanonicalEncoding.AsSpan()); expected.Write(entry.Checksum.AsSpan()); }
        Assert.Equal(expected.WrittenSpan.ToArray(), directory.CanonicalEncoding.ToArray());
    }

    [Fact]
    public void Empty_and_transition_publications_use_separate_L54_and_directory_authority()
    {
        (CollectionDefinition collection, BaseLogicalIndexDefinition definition) = Contract(unique: false);
        Assert.True(BaseLogicalIndexDirectoryContract.TryCreate(collection, definition, [], Limits,
            out BaseLogicalIndexDirectory? emptyDirectory));
        Assert.NotNull(emptyDirectory);
        byte[] logicalBytes = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        byte[] nextLogicalBytes = Enumerable.Range(32, 32).Select(static value => (byte)value).ToArray();
        byte[] provisionalBytes = Enumerable.Range(64, 32).Select(static value => (byte)value).ToArray();
        byte[] member = Enumerable.Repeat((byte)0xa5, 32).ToArray();
        BaseSchemaAuthorityChecksum logical = BaseSchemaAuthorityChecksum.Create(logicalBytes);
        BaseSchemaAuthorityChecksum nextLogical = BaseSchemaAuthorityChecksum.Create(nextLogicalBytes);
        BaseSchemaAuthorityChecksum provisional = BaseSchemaAuthorityChecksum.Create(provisionalBytes);

        byte[] emptyEncoding = new byte[8];
        byte[] emptyMember = Hash("base.logicalIndex.memberSet.v1\0", emptyEncoding);
        byte[] initial = BaseLogicalIndexDirectoryContract.InitialDirectoryPublication(logical).ToArray();
        Assert.Equal(Hash("base.logicalIndex.directoryInitial.v1\0", logicalBytes, emptyMember), initial);

        byte[] next = BaseLogicalIndexDirectoryContract.NextDirectoryPublication(initial, nextLogical, member, provisional).ToArray();
        Assert.Equal(Hash("base.logicalIndex.directoryNext.v1\0", initial, nextLogicalBytes, member, provisionalBytes), next);
        Assert.Equal(emptyEncoding, BaseLogicalIndexDirectoryContract.EmptyDirectoryEncoding());
        Assert.Equal(emptyMember, BaseLogicalIndexDirectoryContract.EmptyMemberSetChecksum());

        BaseLogicalIndexDirectoryAuthority initialAuthority = BaseLogicalIndexDirectoryContract.CreateInitialAuthority(
            definition, logical, emptyDirectory);
        Assert.True(BaseLogicalIndexDirectoryContract.AuthorityShapeValid(initialAuthority));
        Assert.Equal(1, initialAuthority.Generation);
        Assert.Empty(initialAuthority.PreviousDirectoryPublicationChecksum);
        Assert.Equal(initial, initialAuthority.DirectoryPublicationChecksum.ToArray());

        Assert.True(BaseLogicalIndexDirectoryContract.TryCreate(collection, definition,
            [(Id(1), Payload("a", "x", 1))], Limits, out BaseLogicalIndexDirectory? populated));
        BaseLogicalIndexDirectoryAuthority resulting = BaseLogicalIndexDirectoryContract.NextAuthority(
            initialAuthority, nextLogical, provisional, populated!);
        Assert.True(BaseLogicalIndexDirectoryContract.AuthorityShapeValid(resulting));
        Assert.Equal(2, resulting.Generation);
        Assert.Equal(initial, resulting.PreviousDirectoryPublicationChecksum.ToArray());
        Assert.Equal(BaseLogicalIndexDirectoryContract.NextDirectoryPublication(
            initial, nextLogical, populated!.MemberSetChecksum.AsSpan(), provisional).ToArray(),
            resulting.DirectoryPublicationChecksum.ToArray());
    }

    [Fact]
    public void Unique_directory_rejects_a_duplicate_complete_key_without_returning_partial_state()
    {
        (CollectionDefinition collection, BaseLogicalIndexDefinition index) = Contract(unique: true);
        Assert.False(BaseLogicalIndexDirectoryContract.TryCreate(collection, index,
            [(Id(1), Payload("a", "x", 1)), (Id(2), Payload("a", "x", 2))], Limits,
            out BaseLogicalIndexDirectory? directory));
        Assert.Null(directory);
    }

    [Theory]
    [InlineData("records")]
    [InlineData("predicate")]
    [InlineData("keys")]
    [InlineData("keyBytes")]
    [InlineData("postingKeys")]
    [InlineData("postings")]
    [InlineData("postingRecords")]
    [InlineData("entries")]
    [InlineData("retained")]
    [InlineData("transient")]
    public void Maximum_plus_one_is_rejected_before_a_directory_escapes(string dimension)
    {
        (CollectionDefinition collection, BaseLogicalIndexDefinition index) = Contract(unique: false);
        BaseLogicalIndexDirectoryLimits limits = dimension switch
        {
            "records" => Limits with { MaximumRecords = 1 },
            "predicate" => Limits with { MaximumPredicateEvaluations = 1 },
            "keys" => Limits with { MaximumKeys = 1 },
            "keyBytes" => Limits with { MaximumKeyBytes = 1 },
            "postingKeys" => Limits with { MaximumPostingKeys = 1 },
            "postings" => Limits with { MaximumPostings = 1 },
            "postingRecords" => Limits with { MaximumPostingRecordsPerKey = 1 },
            "entries" => Limits with { MaximumComparatorEntries = 1 },
            "retained" => Limits with { MaximumRetainedDirectoryBytes = 8 },
            "transient" => Limits with { MaximumTransientBytes = 1 },
            _ => throw new InvalidOperationException(),
        };
        IEnumerable<(RecordId, RecordPayload)> records = dimension is "postingRecords"
            ? [(Id(1), Payload("a", "x", 1)), (Id(2), Payload("a", "x", 2))]
            : [(Id(1), Payload("a", "x", 1)), (Id(2), Payload("b", "y", 2))];

        Assert.False(BaseLogicalIndexDirectoryContract.TryCreate(collection, index, records, limits, out BaseLogicalIndexDirectory? directory));
        Assert.Null(directory);
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("captured")]
    [InlineData("staged")]
    public void Prospective_retained_work_is_admitted_before_a_directory_escapes(string dimension)
    {
        (CollectionDefinition collection, BaseLogicalIndexDefinition index) = Contract(unique: false);
        BaseLogicalIndexDirectoryProspectiveWork work = dimension switch
        {
            "evidence" => new() { CapturedOldDirectoryBytes = 0, StagedTransitionBytes = 0, EvidenceBytes = 2 },
            "captured" => new() { CapturedOldDirectoryBytes = 131_072, StagedTransitionBytes = 0, EvidenceBytes = 0 },
            "staged" => new() { CapturedOldDirectoryBytes = 0, StagedTransitionBytes = 131_072, EvidenceBytes = 0 },
            _ => throw new InvalidOperationException(),
        };
        BaseLogicalIndexDirectoryLimits limits = dimension == "evidence"
            ? Limits with { MaximumEvidenceBytes = 1 }
            : Limits;

        Assert.False(BaseLogicalIndexDirectoryContract.TryCreate(collection, index,
            [(Id(1), Payload("a", "x", 1))], limits, work, out BaseLogicalIndexDirectory? directory));
        Assert.Null(directory);
    }

    private static readonly BaseLogicalIndexDirectoryLimits Limits = new()
    {
        MaximumRecords = 16,
        MaximumPredicateEvaluations = 16,
        MaximumKeys = 16,
        MaximumKeyBytes = 65_536,
        MaximumCanonicalKeyBytes = 65_536,
        MaximumPostingKeys = 16,
        MaximumPostings = 16,
        MaximumPostingRecordsPerKey = 16,
        MaximumComparatorEntries = 16,
        MaximumEvidenceBytes = 65_536,
        MaximumRetainedDirectoryBytes = 65_536,
        MaximumTransientBytes = 131_072,
    };

    private static (CollectionDefinition Collection, BaseLogicalIndexDefinition Index) Contract(bool unique)
    {
        FieldDefinition[] fields =
        [
            Field("a-tenant", "tenant", BaseScalarKind.String),
            Field("b-code", "code", BaseScalarKind.String),
            Field("c-sequence", "sequence", BaseScalarKind.Int64),
        ];
        CollectionDefinition collection = new()
        {
            Id = "items", Name = "items", Kind = "record", SchemaMode = SchemaMode.Strict,
            UnknownFields = UnknownFieldPolicy.Reject, Fields = fields,
        };
        BaseLogicalIndexDefinition index = new()
        {
            Id = BaseLogicalIndexId.Create("items.by-tenant-code-sequence"), Version = 1,
            CollectionId = collection.Id, Unique = unique, StoreRequired = true,
            Parts =
            [
                new BaseLogicalIndexPart { FieldOrdinal = 0, Direction = BaseIndexSortDirection.Descending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue },
                new BaseLogicalIndexPart { FieldOrdinal = 1, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue },
            ],
            MembershipPredicate = new BaseIndexPredicateRegistry
            {
                Root = BaseIndexPredicateId.Create("p0"),
                Nodes = [new BaseIndexPredicateNode { Id = BaseIndexPredicateId.Create("p0"), Kind = BaseIndexPredicateNodeKind.True }],
                Checksum = BaseSchemaAuthorityChecksum.Create(new byte[32]),
            },
            Checksum = BaseLogicalIndexChecksum.Create(new byte[32]),
        };
        return (collection, index);
    }

    private static FieldDefinition Field(string id, string wire, BaseScalarKind kind) => new()
    {
        Id = id, ApplicationName = wire, WireName = wire,
        Type = kind == BaseScalarKind.Int64 ? "integer" : "string", ScalarKind = kind,
        ScalarCodec = BaseGeneratedSchemaRegistration.ScalarCodec(kind),
        ScalarConstraints = new(),
    };

    private static RecordPayload Payload(string tenant, string? code, long sequence)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["tenant"] = Element(tenant), ["sequence"] = Element(sequence),
        };
        fields["code"] = code is null ? Element((string?)null) : Element(code);
        return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
    }

    private static RecordId Id(int ordinal) => RecordId.Create($"00000000-0000-0000-0000-{ordinal:000000000000}");
    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static byte[] Hash(string purpose, params byte[][] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(purpose));
        foreach (byte[] value in values) hash.AppendData(value);
        return hash.GetHashAndReset();
    }

    private static byte[] Hash(string purpose, ReadOnlySpan<byte> value) => Hash(purpose, value.ToArray());
    private static void Bytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value) { I32(writer, value.Length); writer.Write(value); }
    private static void Text(ArrayBufferWriter<byte> writer, string value) => Bytes(writer, Encoding.UTF8.GetBytes(value));
    private static void I32(ArrayBufferWriter<byte> writer, int value)
    { Span<byte> span = writer.GetSpan(sizeof(int)); BinaryPrimitives.WriteInt32BigEndian(span, value); writer.Advance(sizeof(int)); }
}
