namespace HPD.Gateway.ControlPlane;

/// <summary>Serializes identified Gateway management mutations within one process host.</summary>
internal sealed class GatewayManagementMutationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken) => _gate.WaitAsync(cancellationToken);

    public void Release() => _gate.Release();
}
