
namespace HPD.Base;

internal sealed class DefaultBaseCapabilityProvider : IBaseCapabilityProvider
{
    private readonly IBaseDescriptorRegistry _registry;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBaseCapabilityProvider(IBaseDescriptorRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Executes the get capabilities async operation.</summary>
    public ValueTask<OperationResult<CapabilityDescriptor>> GetCapabilitiesAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = principal;
        return HPDBaseRuntimeTelemetry.TraceRuntimeReadAsync(
            HPDBaseTelemetrySpans.RuntimeCapabilitiesGet,
            BaseOperationKind.AdminInspect,
            operation.CollectionId,
            view,
            !string.IsNullOrWhiteSpace(operation.CorrelationId),
            countAsHealthRead: false,
            countAsDiagnosticRead: false,
            () => ValueTask.FromResult(OperationResults.Ok(DescriptorViewFilter.Capabilities(_registry.Current, view))));
    }

    /// <summary>Executes the supports feature operation.</summary>
    public bool SupportsFeature(string featureId, string? collectionId = null)
    {
        _ = collectionId;
        return _registry.Current.Capabilities.Families
            .SelectMany(family => family.Features ?? [])
            .Any(feature => string.Equals(feature.FeatureId, featureId, StringComparison.Ordinal)
                && feature.Status == CapabilityStatus.Available);
    }
}
