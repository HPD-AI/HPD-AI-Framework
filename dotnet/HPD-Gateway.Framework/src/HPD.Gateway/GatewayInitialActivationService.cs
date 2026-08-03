using Microsoft.Extensions.Hosting;

namespace HPD.Gateway;

internal sealed class GatewayInitialActivationService(
    GatewayCompositionState state,
    IGatewayNodeActivator activator) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (state.InitialCandidate is null) return;
        var result = await activator.ActivateAsync(state.InitialCandidate, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsActiveAcknowledged)
            throw new InvalidOperationException("The initial HPD Gateway candidate was not actively acknowledged.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
