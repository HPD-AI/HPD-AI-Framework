using HPD.Base.Tests.Volatile.TestDoubles;

namespace HPD.Base.Tests.Volatile.Streaming;

public sealed class StreamingTests
{
    [Fact]
    public async Task StreamYieldsSnapshotItems()
    {
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "one")) },
            VolatileTestData.Operation(BaseOperationKind.Create));

        var items = new List<RecordEnvelope>();
        var stream = await store.OpenStreamAsync(
            collection,
            new RecordQuery { Count = QueryCountMode.None },
            VolatileTestData.Operation(BaseOperationKind.List));

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
        var store = new VolatileRecordStore();
        var collection = VolatileTestData.Collection();
        await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "one")) },
            VolatileTestData.Operation(BaseOperationKind.Create));
        await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "two")) },
            VolatileTestData.Operation(BaseOperationKind.Create));

        var stream = await store.OpenStreamAsync(
            collection,
            new RecordQuery { Count = QueryCountMode.None },
            VolatileTestData.Operation(BaseOperationKind.List));
        stream.Status.Should().Be(OperationStatus.Ok);

        await using var enumerator = stream.Value!.Items.GetAsyncEnumerator();

        (await enumerator.MoveNextAsync()).Should().BeTrue();
        await VolatileMutationTestDriver.CreateAsync(store,
            collection,
            new RecordCreateRequest { Payload = VolatileTestData.Payload(("title", "three")) },
            VolatileTestData.Operation(BaseOperationKind.Create));

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
        var store = new VolatileRecordStore();

        var result = await store.OpenStreamAsync(
            VolatileTestData.Collection(),
            new RecordQuery { Count = QueryCountMode.Exact },
            VolatileTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Unsupported);
        result.Error.Should().NotBeNull();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task StreamFailsClosedWhenCapabilityIsDisabled()
    {
        var store = new VolatileRecordStore(new HPDBaseVolatileStoreOptions { EnableStreamingCapability = false });
        store.Capabilities.Streaming!.Supported.Should().BeFalse();

        var result = await store.OpenStreamAsync(
            VolatileTestData.Collection(),
            new RecordQuery { Count = QueryCountMode.None },
            VolatileTestData.Operation(BaseOperationKind.List));

        result.Status.Should().Be(OperationStatus.Unsupported);
        result.Error.Should().NotBeNull();
        result.Value.Should().BeNull();
    }
}
