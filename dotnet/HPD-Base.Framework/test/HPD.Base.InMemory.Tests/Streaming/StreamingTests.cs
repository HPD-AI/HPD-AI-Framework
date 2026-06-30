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
        var stream = await store.OpenStreamAsync(
            collection,
            new RecordQuery { Count = QueryCountMode.None },
            InMemoryTestData.Operation(BaseOperationKind.List));

        stream.Status.Should().Be(OperationStatus.Ok);
        await foreach (var item in stream.Value!.Items)
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

        var stream = await store.OpenStreamAsync(
            collection,
            new RecordQuery { Count = QueryCountMode.None },
            InMemoryTestData.Operation(BaseOperationKind.List));
        stream.Status.Should().Be(OperationStatus.Ok);

        await using var enumerator = stream.Value!.Items.GetAsyncEnumerator();

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

        var result = await store.OpenStreamAsync(
            InMemoryTestData.Collection(),
            new RecordQuery { Count = QueryCountMode.Exact },
            InMemoryTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Unsupported);
        result.Error.Should().NotBeNull();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task StreamFailsClosedWhenCapabilityIsDisabled()
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryOptions { EnableStreamingCapability = false });
        store.Capabilities.Streaming!.Supported.Should().BeFalse();

        var result = await store.OpenStreamAsync(
            InMemoryTestData.Collection(),
            new RecordQuery { Count = QueryCountMode.None },
            InMemoryTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Unsupported);
        result.Error.Should().NotBeNull();
        result.Value.Should().BeNull();
    }
}
