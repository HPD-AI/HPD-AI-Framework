using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace HPD.Gateway.ControlPlane;

/// <summary>Owns the exact semantic handlers shared by the Admin endpoint and sealed Studio surface.</summary>
internal sealed class GatewayAdminHandlerCatalog
{
    private readonly ConcurrentDictionary<string, RequestDelegate> _handlers = new(StringComparer.Ordinal);

    internal void Register(string operationId, RequestDelegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(operationId, handler))
            throw new InvalidOperationException("A Gateway Admin semantic handler is duplicated.");
    }

    internal bool TryGet(string operationId, out RequestDelegate handler) =>
        _handlers.TryGetValue(operationId, out handler!);
}
