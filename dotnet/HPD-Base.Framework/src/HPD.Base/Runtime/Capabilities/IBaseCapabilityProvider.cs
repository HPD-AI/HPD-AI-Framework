
namespace HPD.Base;

/// <summary>Defines the ibase capability provider contract.</summary>
public interface IBaseCapabilityProvider
{
    /// <summary>Executes the get capabilities async operation.</summary>
    ValueTask<OperationResult<CapabilityDescriptor>> GetCapabilitiesAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the supports feature operation.</summary>
    bool SupportsFeature(string featureId, string? collectionId = null);
}
