using HPD.Base.InMemory.Tests.TestDoubles;

namespace HPD.Base.InMemory.Tests.Streaming;

public sealed class StreamingTests
{
    [Fact]
    public async Task StreamYieldsSnapshotItems()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        await store.CreateAsync(
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "one")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        var items = new List<RecordEnvelope>();
        await foreach (var item in store.StreamAsync(
            collection,
            new RecordQuery { Count = QueryCountMode.None },
            InMemoryTestData.Operation(BaseOperationKind.List)))
        {
            items.Add(item);
        }

        items.Should().ContainSingle();
        items[0].Payload.Fields!["title"].GetString().Should().Be("one");
    }

    [Fact]
    public async Task StreamDoesNotExposeMutationsCommittedAfterEnumerationStarts()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        await store.CreateAsync(
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "one")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));
        await store.CreateAsync(
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "two")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        await using var enumerator = store.StreamAsync(
            collection,
            new RecordQuery { Count = QueryCountMode.None },
            InMemoryTestData.Operation(BaseOperationKind.List)).GetAsyncEnumerator();

        (await enumerator.MoveNextAsync()).Should().BeTrue();
        await store.CreateAsync(
            collection,
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "three")) },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        var titles = new List<string> { enumerator.Current.Payload.Fields!["title"].GetString()! };
        while (await enumerator.MoveNextAsync())
        {
            titles.Add(enumerator.Current.Payload.Fields!["title"].GetString()!);
        }

        titles.Should().Equal("one", "two");
    }

    [Fact]
    public async Task StreamRejectsCountModes()
    {
        var store = new InMemoryRecordStore();

        var act = async () =>
        {
            await foreach (var _ in store.StreamAsync(
                InMemoryTestData.Collection(),
                new RecordQuery { Count = QueryCountMode.Exact },
                InMemoryTestData.Operation(BaseOperationKind.List)))
            {
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StreamFailsClosedWhenCapabilityIsDisabled()
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryOptions { EnableStreamingCapability = false });
        store.Capabilities.Streaming!.Supported.Should().BeFalse();

        var act = async () =>
        {
            await foreach (var _ in store.StreamAsync(
                InMemoryTestData.Collection(),
                new RecordQuery { Count = QueryCountMode.None },
                InMemoryTestData.Operation(BaseOperationKind.List)))
            {
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
