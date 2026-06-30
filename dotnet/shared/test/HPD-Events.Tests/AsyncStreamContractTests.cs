using HPD.Events;

namespace HPD.Events.Tests;

public class AsyncStreamContractTests
{
    [Fact]
    public async Task Opened_CarriesStreamMetadataAndItems()
    {
        var stream = new AsyncStream<int>
        {
            Items = Yield(1, 2, 3),
            Descriptor = new AsyncStreamDescriptor
            {
                StreamId = "numbers",
                Cursor = "cursor-1",
                Checkpoint = "checkpoint-1",
                Replayable = true,
                Resumable = true,
                Backpressure = AsyncStreamBackpressureMode.Wait,
                DeliveryGuarantee = AsyncStreamDeliveryGuarantee.Replayable
            }
        };

        var result = AsyncStreamOpenResult<AsyncStream<int>>.Opened(stream);

        Assert.True(result.Succeeded);
        Assert.Equal(AsyncStreamOpenStatus.Opened, result.Status);
        Assert.Null(result.Error);
        Assert.Equal("numbers", result.Value!.Descriptor.StreamId);
        Assert.Equal("cursor-1", result.Value.Descriptor.Cursor);
        Assert.Equal("checkpoint-1", result.Value.Descriptor.Checkpoint);
        Assert.True(result.Value.Descriptor.Replayable);
        Assert.True(result.Value.Descriptor.Resumable);
        Assert.Equal(AsyncStreamBackpressureMode.Wait, result.Value.Descriptor.Backpressure);
        Assert.Equal(AsyncStreamDeliveryGuarantee.Replayable, result.Value.Descriptor.DeliveryGuarantee);

        var items = new List<int>();
        await foreach (var item in result.Value.Items)
            items.Add(item);

        Assert.Equal([1, 2, 3], items);
    }

    [Fact]
    public void Failed_RequiresNonOpenedStatusAndCarriesError()
    {
        var error = new AsyncStreamError
        {
            Code = "stream.query.unsupported",
            Message = "This stream source does not support the requested query.",
            Target = "query.include",
            Category = AsyncStreamErrorCategory.Unsupported
        };

        var result = AsyncStreamOpenResult<AsyncStream<int>>.Failed(
            AsyncStreamOpenStatus.Unsupported,
            error);

        Assert.False(result.Succeeded);
        Assert.Equal(AsyncStreamOpenStatus.Unsupported, result.Status);
        Assert.Null(result.Value);
        Assert.Same(error, result.Error);
        Assert.Equal("query.include", result.Error!.Target);
        Assert.Equal(AsyncStreamErrorCategory.Unsupported, result.Error.Category);
    }

    [Fact]
    public void Failed_RejectsOpenedStatus()
    {
        var error = new AsyncStreamError
        {
            Code = "stream.invalid",
            Message = "Invalid stream result."
        };

        Assert.Throws<ArgumentException>(() =>
            AsyncStreamOpenResult<AsyncStream<int>>.Failed(AsyncStreamOpenStatus.Opened, error));
    }

    [Fact]
    public async Task Source_CanReturnValidationFailureBeforeEnumeration()
    {
        var source = new TestStreamSource();

        var result = await source.OpenAsync(new TestStreamRequest(Allow: false));

        Assert.Equal(AsyncStreamOpenStatus.ValidationFailed, result.Status);
        Assert.Equal("test.stream.denied", result.Error!.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Source_CanOpenTypedStream()
    {
        var source = new TestStreamSource();

        var result = await source.OpenAsync(new TestStreamRequest(Allow: true));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("test", result.Value!.Descriptor.StreamId);
    }

    private static async IAsyncEnumerable<int> Yield(params int[] values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }

    private sealed record TestStreamRequest(bool Allow);

    private sealed class TestStreamSource : IAsyncStreamSource<TestStreamRequest, int>
    {
        public ValueTask<AsyncStreamOpenResult<AsyncStream<int>>> OpenAsync(
            TestStreamRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.Allow)
            {
                return ValueTask.FromResult(AsyncStreamOpenResult<AsyncStream<int>>.Failed(
                    AsyncStreamOpenStatus.ValidationFailed,
                    new AsyncStreamError
                    {
                        Code = "test.stream.denied",
                        Message = "The test stream was denied.",
                        Category = AsyncStreamErrorCategory.Validation
                    }));
            }

            return ValueTask.FromResult(AsyncStreamOpenResult<AsyncStream<int>>.Opened(new AsyncStream<int>
            {
                Items = Yield(42),
                Descriptor = new AsyncStreamDescriptor { StreamId = "test" }
            }));
        }
    }
}
