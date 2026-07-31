
namespace HPD.Base;

public interface IBaseCapabilityProvider
{
    ValueTask<OperationResult<CapabilityDescriptor>> GetCapabilitiesAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);

    bool SupportsFeature(string featureId, string? collectionId = null);
}
