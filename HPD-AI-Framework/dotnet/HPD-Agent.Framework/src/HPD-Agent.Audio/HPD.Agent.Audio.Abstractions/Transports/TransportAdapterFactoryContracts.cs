using HPD.Agent.Audio.Media;

namespace HPD.Agent.Audio.Transports;

public interface ITransportAdapterFactory
{
    bool CanCreate(TransportBinding binding);

    ValueTask<ITransportAdapter> CreateAsync(
        TransportBinding binding,
        AudioTransportContext context,
        CancellationToken cancellationToken = default);
}

public interface ITransportAdapterRegistry
{
    ValueTask<ITransportAdapter> CreateAsync(
        TransportBinding binding,
        AudioTransportContext context,
        CancellationToken cancellationToken = default);
}
