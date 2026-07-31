using HPD.Base.Descriptors;
using HPD.Base.Results;
using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Capabilities;

public interface IBaseCapabilityProvider
{
    ValueTask<OperationResult<CapabilityDescriptor>> GetCapabilitiesAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);

    bool SupportsFeature(string featureId, string? collectionId = null);
}
