using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace HPD.Base.Tests.Runtime.Operations;

public sealed class L39ProjectionIsolationTests
{
    [Fact]
    public void Contributor_clone_isolates_every_nested_immutable_array_backing_store()
    {
        var value = new BaseAtomicProjectionValue(BaseAtomicProjectionValueKind.Array, ImmutableArray.Create<byte>(91, 49, 93));
        var record = new BaseAtomicProjectionRecord(
            new RecordId("record-a"),
            new RevisionToken("sqlite:7"),
            [new BaseAtomicProjectionField("document.embedding", value)]);
        var fact = new BaseAtomicMutationProjectionFact(
            "item-a",
            BaseRecordMutationKind.Patch,
            BaseCommittedRecordMutationKind.Patch,
            null,
            "documents",
            "event-a",
            new BaseMutationJournalPosition(7),
            record,
            record,
            ["document.embedding"]);
        var canonical = new BaseAtomicMutationProjectionRequest([fact], new BaseCollectionPurgeProjectionFact("documents", 2, 3));

        BaseAtomicMutationProjectionRequest hostile = BaseAtomicMutationProjectionFactory.Clone(canonical);
        ImmutableCollectionsMarshal.AsArray(hostile.Mutations[0].ChangedFieldIds)!.AsSpan().Clear();
        ImmutableCollectionsMarshal.AsArray(hostile.Mutations[0].After!.Fields[0].Value.CanonicalJsonUtf8)!.AsSpan().Clear();
        ImmutableCollectionsMarshal.AsArray(hostile.Mutations[0].After!.Fields)!.AsSpan().Clear();
        ImmutableCollectionsMarshal.AsArray(hostile.Mutations)![0] = new BaseAtomicMutationProjectionFact(
            null, BaseRecordMutationKind.Delete, BaseCommittedRecordMutationKind.Delete, null,
            "changed", "changed", new BaseMutationJournalPosition(99), null, null, []);

        BaseAtomicMutationProjectionRequest later = BaseAtomicMutationProjectionFactory.Clone(canonical);

        later.Mutations.Should().ContainSingle();
        later.Mutations[0].CollectionId.Should().Be("documents");
        later.Mutations[0].After.Should().NotBeNull();
        BaseAtomicProjectionRecord laterAfter = later.Mutations[0].After!;
        laterAfter.Fields.Should().ContainSingle();
        laterAfter.Fields[0].Value.CanonicalJsonUtf8.Should().Equal(91, 49, 93);
        later.Mutations[0].ChangedFieldIds.Should().Equal("document.embedding");
        later.Purge!.PublishedGeneration.Should().Be(3);
    }
}
