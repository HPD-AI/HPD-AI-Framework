using HPD.Base.Descriptors;
using HPD.Base.Observability;
using HPD.Base.Results;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Observability;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Runtime.Capabilities;

internal sealed class DefaultBaseCapabilityProvider : IBaseCapabilityProvider
{
    private readonly IBaseDescriptorRegistry _registry;

    public DefaultBaseCapabilityProvider(IBaseDescriptorRegistry registry)
    {
        _registry = registry;
    }

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

    public bool SupportsFeature(string featureId, string? collectionId = null)
    {
        _ = collectionId;
        return _registry.Current.Capabilities.Families
            .SelectMany(family => family.Features ?? [])
            .Any(feature => string.Equals(feature.FeatureId, featureId, StringComparison.Ordinal)
                && feature.Status == CapabilityStatus.Available);
    }
}
