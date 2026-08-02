using FluentAssertions;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteSchemaGenerationGateTests
{
    [Fact]
    public async Task ExclusiveLeaseWaitsForEverySharedLease()
    {
        var gate = new SqliteSchemaGenerationGate();
        IAsyncDisposable first = await gate.AcquireSharedAsync(CancellationToken.None);
        IAsyncDisposable second = await gate.AcquireSharedAsync(CancellationToken.None);

        Task<IAsyncDisposable> exclusive = gate.AcquireExclusiveAsync(CancellationToken.None).AsTask();
        await Task.Delay(50);
        exclusive.IsCompleted.Should().BeFalse();

        await first.DisposeAsync();
        await Task.Delay(50);
        exclusive.IsCompleted.Should().BeFalse();

        await second.DisposeAsync();
        await using IAsyncDisposable acquired = await exclusive.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitingExclusiveLeasePreventsNewSharedWorkFromPassingIt()
    {
        var gate = new SqliteSchemaGenerationGate();
        IAsyncDisposable active = await gate.AcquireSharedAsync(CancellationToken.None);
        Task<IAsyncDisposable> exclusive = gate.AcquireExclusiveAsync(CancellationToken.None).AsTask();
        await Task.Delay(50);
        Task<IAsyncDisposable> laterRead = gate.AcquireSharedAsync(CancellationToken.None).AsTask();

        await active.DisposeAsync();
        IAsyncDisposable migration = await exclusive.WaitAsync(TimeSpan.FromSeconds(1));
        laterRead.IsCompleted.Should().BeFalse();

        await migration.DisposeAsync();
        await using IAsyncDisposable resumed = await laterRead.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CancelledExclusiveWaitDoesNotBlockLaterWork()
    {
        var gate = new SqliteSchemaGenerationGate();
        await using IAsyncDisposable active = await gate.AcquireSharedAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Func<Task> act = async () =>
            await gate.AcquireExclusiveAsync(cancellation.Token).AsTask();
        await act.Should().ThrowAsync<OperationCanceledException>();

        await using IAsyncDisposable concurrent =
            await gate.AcquireSharedAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    }
}
