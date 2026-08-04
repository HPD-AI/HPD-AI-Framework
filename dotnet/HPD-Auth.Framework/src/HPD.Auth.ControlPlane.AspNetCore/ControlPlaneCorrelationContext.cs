using HPD.Auth.Core.Audit;

namespace HPD.Auth.ControlPlane;

internal sealed class ControlPlaneCorrelationContext : IAuthCorrelationContext
{
    private string? _correlationId;
    public string? CorrelationId => _correlationId;

    public void Initialize(string correlationId)
    {
        if (Interlocked.CompareExchange(ref _correlationId, correlationId, null) is not null)
            throw new InvalidOperationException("The control-plane correlation context is already initialized.");
    }
}
