using System.Threading.Channels;

namespace HPD.Base.Realtime.AspNetCore.EndpointMapping;

internal sealed class BaseRealtimeChannelOwner : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly IAsyncEnumerator<BaseRealtimeEvent> _events;
    private readonly Channel<BaseRealtimeEvent> _outbound;
    private readonly Func<string, BaseRealtimeEvent, CancellationToken, Task> _send;
    private readonly Action _producerFailed;
    private readonly Func<string, CancellationToken, Task> _slowConsumer;
    private readonly TaskCompletionSource _activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _stopSync = new();
    private readonly Task _producer;
    private readonly Task _sender;
    private Task? _stop;
    private int _slowConsumerRecorded;

    public BaseRealtimeChannelOwner(
        string channel,
        IAsyncEnumerable<BaseRealtimeEvent> events,
        int outboundCapacity,
        CancellationToken sessionCancellation,
        Func<string, BaseRealtimeEvent, CancellationToken, Task> send,
        Action producerFailed,
        Func<string, CancellationToken, Task> slowConsumer)
    {
        Channel = channel;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
        _events = events.GetAsyncEnumerator(_cancellation.Token);
        _outbound = System.Threading.Channels.Channel.CreateBounded<BaseRealtimeEvent>(
            new BoundedChannelOptions(outboundCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
        _send = send;
        _producerFailed = producerFailed;
        _slowConsumer = slowConsumer;
        _producer = ProduceAsync();
        _sender = SendAsync();
        Completion = ObserveAsync();
    }

    public string Channel { get; }

    public Task Completion { get; }

    public bool IsCompleted => Completion.IsCompleted;

    public void Activate() => _activation.TrySetResult();

    public Task StopAsync()
    {
        lock (_stopSync)
        {
            return _stop ??= StopCoreAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task StopCoreAsync()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);
        _activation.TrySetCanceled(_cancellation.Token);
        await Completion.ConfigureAwait(false);
        _cancellation.Dispose();
    }

    private async Task ProduceAsync()
    {
        try
        {
            await _activation.Task.WaitAsync(_cancellation.Token).ConfigureAwait(false);
            while (await _events.MoveNextAsync().ConfigureAwait(false))
            {
                if (_outbound.Writer.TryWrite(_events.Current))
                    continue;

                await TerminateSlowConsumerAsync().ConfigureAwait(false);
                return;
            }
        }
        finally
        {
            _outbound.Writer.TryComplete();
            await _events.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SendAsync()
    {
        await foreach (var item in _outbound.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
        {
            await _send(Channel, item, _cancellation.Token).ConfigureAwait(false);
        }
    }

    private async Task ObserveAsync()
    {
        Exception? producerFailure = null;
        var first = await Task.WhenAny(_producer, _sender).ConfigureAwait(false);
        if (ReferenceEquals(first, _sender) && !_producer.IsCompleted)
            await _cancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await _producer.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            producerFailure = exception;
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await _sender.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (BaseRealtimeSendTimeoutException)
        {
            await TerminateSlowConsumerAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }

        if (producerFailure is not null)
            _producerFailed();
    }

    private async Task TerminateSlowConsumerAsync()
    {
        if (Interlocked.Exchange(ref _slowConsumerRecorded, 1) != 0)
            return;

        _outbound.Writer.TryComplete();
        try
        {
            await _slowConsumer(Channel, _cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }
    }
}
