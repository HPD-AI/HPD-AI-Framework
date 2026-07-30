namespace HPD.Base.StoreConformance.Streaming;

public abstract class RecordStoreStreamingConformanceTests<TFixture> : RecordStoreConformanceTestBase<TFixture>
    where TFixture : IRecordStoreConformanceFixture, new()
{
    [Fact]
    public async Task StreamOpenAndEnumerationFollowAdvertisedCapability()
    {
        var store = await CreateStoreAsync();
        if (Capabilities.Streaming?.Supported != true)
        {
            if (store is IStreamingRecordStore disabledStreaming)
            {
                var disabled = await disabledStreaming.OpenStreamAsync(
                    Collection,
                    RecordStoreConformanceQueries.Empty,
                    Operation(BaseOperationKind.List));
                RecordStoreConformanceAssertions.Failure(disabled, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable);
            }

            return;
        }

        Assert.True(store is IStreamingRecordStore, "Store advertises streaming but does not implement IStreamingRecordStore.");
        if (!Capabilities.Mutation.Create)
        {
            return;
        }

        await CreateRecordAsync(store, "stream-one", ("title", "one"));
        var streaming = (IStreamingRecordStore)store;
        var open = await streaming.OpenStreamAsync(
            Collection,
            RecordStoreConformanceQueries.Empty,
            Operation(BaseOperationKind.List));

        RecordStoreConformanceAssertions.Success(open, OperationStatus.Ok);
        Assert.NotNull(open.Value!.Items);
        Assert.NotNull(open.Value.Descriptor);
        Assert.False(string.IsNullOrWhiteSpace(open.Value.Descriptor.StreamId));

        var items = new List<RecordEnvelope>();
        await foreach (var item in open.Value.Items)
        {
            items.Add(item);
        }

        Assert.Contains(items, item => item.Id.Value == "stream-one");
        var streamed = items.Single(item => item.Id.Value == "stream-one");
        streamed.Payload.Fields!["title"] = RecordStoreConformanceData.StringElement("mutated");

        if (Capabilities.Read.Get)
        {
            var get = await store.GetAsync(Collection, new RecordId("stream-one"), Operation(BaseOperationKind.Get, new RecordId("stream-one")));
            RecordStoreConformanceAssertions.Success(get, OperationStatus.Ok);
            RecordStoreConformanceAssertions.HasField(get.Value!, "title", "one");
        }
    }

    [Fact]
    public async Task UnsupportedStreamQueryFailsBeforeEnumeration()
    {
        var store = await CreateStoreAsync();
        if (Capabilities.Streaming?.Supported != true || store is not IStreamingRecordStore streaming)
        {
            return;
        }

        var result = await streaming.OpenStreamAsync(
            Collection,
            new RecordQuery { Count = QueryCountMode.Exact },
            Operation(BaseOperationKind.List));

        RecordStoreConformanceAssertions.Failure(result, OperationStatus.Unsupported, OperationStatus.CapabilityUnavailable, OperationStatus.ValidationFailed);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task StreamOpenObservesPreCanceledTokenWhenSupported()
    {
        var store = await CreateStoreAsync();
        if (Capabilities.Streaming?.Supported != true || store is not IStreamingRecordStore streaming)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await streaming.OpenStreamAsync(
                Collection,
                RecordStoreConformanceQueries.Empty,
                Operation(BaseOperationKind.List),
                cancellation.Token);
        });
    }

    [Fact]
    public async Task StreamUsesSnapshotSemanticsWhenProviderExpectsSnapshotStreams()
    {
        if (Capabilities.Streaming?.Supported != true ||
            Fixture is not IStreamingRecordStoreConformanceExpectations { ExpectsSnapshotStreams: true } ||
            !Capabilities.Mutation.Create)
        {
            return;
        }

        var store = await CreateStoreAsync();
        Assert.True(store is IStreamingRecordStore, "Store advertises streaming but does not implement IStreamingRecordStore.");
        await CreateRecordAsync(store, "stream-snapshot-one", ("title", "one"));
        await CreateRecordAsync(store, "stream-snapshot-two", ("title", "two"));

        var streaming = (IStreamingRecordStore)store;
        var open = await streaming.OpenStreamAsync(
            Collection,
            RecordStoreConformanceQueries.Empty,
            Operation(BaseOperationKind.List));
        RecordStoreConformanceAssertions.Success(open, OperationStatus.Ok);

        await using var enumerator = open.Value!.Items.GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        await CreateRecordAsync(store, "stream-snapshot-three", ("title", "three"));

        var ids = new List<string> { enumerator.Current.Id.Value };
        while (await enumerator.MoveNextAsync())
        {
            ids.Add(enumerator.Current.Id.Value);
        }

        Assert.Contains("stream-snapshot-one", ids);
        Assert.Contains("stream-snapshot-two", ids);
        Assert.DoesNotContain("stream-snapshot-three", ids);
    }

    [Fact]
    public async Task StreamEnumerationObservesCancellationWhenProviderExpectsEnumerationCancellation()
    {
        if (Capabilities.Streaming?.Supported != true ||
            Fixture is not IStreamingRecordStoreConformanceExpectations { ExpectsEnumerationCancellation: true } ||
            !Capabilities.Mutation.Create)
        {
            return;
        }

        var store = await CreateStoreAsync();
        Assert.True(store is IStreamingRecordStore, "Store advertises streaming but does not implement IStreamingRecordStore.");
        await CreateRecordAsync(store, "stream-cancel-one", ("title", "one"));
        await CreateRecordAsync(store, "stream-cancel-two", ("title", "two"));

        var streaming = (IStreamingRecordStore)store;
        var open = await streaming.OpenStreamAsync(
            Collection,
            RecordStoreConformanceQueries.Empty,
            Operation(BaseOperationKind.List));
        RecordStoreConformanceAssertions.Success(open, OperationStatus.Ok);

        using var cancellation = new CancellationTokenSource();
        await using var enumerator = open.Value!.Items.GetAsyncEnumerator(cancellation.Token);
        Assert.True(await enumerator.MoveNextAsync());
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await enumerator.MoveNextAsync();
        });
    }
}
