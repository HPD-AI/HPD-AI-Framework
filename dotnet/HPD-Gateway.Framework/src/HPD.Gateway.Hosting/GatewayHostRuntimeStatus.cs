using System.Collections.Immutable;
using Microsoft.Extensions.Hosting;

namespace HPD.Gateway.Hosting;

public enum GatewayHostRealizationState : byte
{
    NotStarted = 0,
    Starting = 1,
    Ready = 2,
    RestartRequired = 3,
    Failed = 4,
    Stopping = 5,
    Stopped = 6
}

public sealed record GatewayHostStatusSnapshot(
    GatewayHostRealizationState State,
    GatewayHostId HostId,
    string RunningConfigurationHash,
    string? DesiredConfigurationHash,
    ImmutableArray<GatewayHostValidationError> Diagnostics);

public sealed class GatewayHostRuntimeStatus
{
    private readonly object _sync = new();
    private GatewayHostRealizationState _state = GatewayHostRealizationState.NotStarted;
    private string? _desiredHash;

    internal GatewayHostRuntimeStatus(GatewayHostCandidate running) => Running = running;

    internal GatewayHostCandidate Running { get; }

    public GatewayHostStatusSnapshot GetSnapshot()
    {
        lock (_sync)
            return new(_state, Running.Configuration.HostId, Running.Sha256, _desiredHash, []);
    }

    public GatewayHostStatusSnapshot EvaluateDesired(GatewayHostCandidate desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        lock (_sync)
        {
            _desiredHash = desired.Sha256;
            if (_state == GatewayHostRealizationState.Ready && !StringComparer.Ordinal.Equals(Running.Sha256, desired.Sha256))
                return new(GatewayHostRealizationState.RestartRequired, Running.Configuration.HostId, Running.Sha256, _desiredHash, []);
            return new(_state, Running.Configuration.HostId, Running.Sha256, _desiredHash, []);
        }
    }

    internal void SetState(GatewayHostRealizationState state) { lock (_sync) _state = state; }
}

internal sealed class GatewayHostLifetimeObserver(
    GatewayHostRuntimeStatus status,
    IHostApplicationLifetime lifetime) : IHostedService
{
    private IDisposable? _started;
    private IDisposable? _stopped;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        status.SetState(GatewayHostRealizationState.Starting);
        _started = lifetime.ApplicationStarted.Register(static state => ((GatewayHostRuntimeStatus)state!).SetState(GatewayHostRealizationState.Ready), status);
        _stopped = lifetime.ApplicationStopped.Register(static state => ((GatewayHostRuntimeStatus)state!).SetState(GatewayHostRealizationState.Stopped), status);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        status.SetState(GatewayHostRealizationState.Stopping);
        _started?.Dispose();
        _stopped?.Dispose();
        status.SetState(GatewayHostRealizationState.Stopped);
        return Task.CompletedTask;
    }
}
