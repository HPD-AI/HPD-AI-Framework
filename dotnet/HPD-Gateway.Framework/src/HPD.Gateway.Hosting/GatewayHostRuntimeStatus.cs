using System.Collections.Immutable;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
            if (_state is GatewayHostRealizationState.Ready or GatewayHostRealizationState.RestartRequired)
                _state = StringComparer.Ordinal.Equals(Running.Sha256, desired.Sha256)
                    ? GatewayHostRealizationState.Ready
                    : GatewayHostRealizationState.RestartRequired;
            return new(_state, Running.Configuration.HostId, Running.Sha256, _desiredHash, []);
        }
    }

    internal void SetState(GatewayHostRealizationState state) { lock (_sync) _state = state; }
}

/// <summary>
/// Owns the observable startup and shutdown boundary for an HPD Gateway host.
/// Use these methods when lifecycle outcome reporting is required.
/// </summary>
public static class GatewayHostLifecycleExtensions
{
    public static async Task StartHpdGatewayAsync(
        this WebApplication application,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        var status = application.Services.GetRequiredService<GatewayHostRuntimeStatus>();
        status.SetState(GatewayHostRealizationState.Starting);
        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            status.SetState(GatewayHostRealizationState.Failed);
            throw;
        }
    }

    public static Task StopHpdGatewayAsync(
        this WebApplication application,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Services.GetRequiredService<GatewayHostRuntimeStatus>()
            .SetState(GatewayHostRealizationState.Stopping);
        return application.StopAsync(cancellationToken);
    }
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
        return Task.CompletedTask;
    }
}
