namespace HPD.Agent.ToolHarness.Coding.Debugging;

public sealed record AuthorizedDebugEndpointDescriptor
{
    public required string EndpointId { get; init; }
    public required string EnvironmentId { get; init; }
    public required long EndpointCatalogRevision { get; init; }
    public required long PolicyRevision { get; init; }
    public required DebugAdapterTransportKind TransportKind { get; init; }
    public required string AuthorizedAddress { get; init; }
    public string? AuthorityReference { get; init; }
}

public interface IDebugEndpointResolver
{
    ValueTask<AuthorizedDebugEndpointDescriptor?> ResolveAsync(
        string endpointId,
        DebugAdapterResolutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class DenyAllDebugEndpointResolver : IDebugEndpointResolver
{
    public ValueTask<AuthorizedDebugEndpointDescriptor?> ResolveAsync(
        string endpointId,
        DebugAdapterResolutionContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<AuthorizedDebugEndpointDescriptor?>(null);
}
