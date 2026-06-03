using HPD.Agent.Audio.Media;

namespace HPD.Agent.Audio.Transports;

public interface ITransportAdapter : IAsyncDisposable
{
    TransportAdapterId Id { get; }

    TransportAdapterState State { get; }

    TransportCapability Capabilities { get; }

    IAsyncEnumerable<TransportEvent> ReadEventsAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<CanonicalMediaEnvelope> ReadMediaAsync(CancellationToken cancellationToken = default);

    ValueTask StartAsync(TransportOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask SendAsync(CanonicalMediaEnvelope envelope, CancellationToken cancellationToken = default);

    ValueTask ExecuteAsync(TransportCommand command, CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
