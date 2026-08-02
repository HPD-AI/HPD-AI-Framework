
namespace HPD.Base;

/// <summary>Defines the ibase descriptor provider contract.</summary>
public interface IBaseDescriptorProvider
{
    /// <summary>Executes the get manifest async operation.</summary>
    ValueTask<OperationResult<BaseManifest>> GetManifestAsync(
        BaseManifestRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the get expanded manifest async operation.</summary>
    ValueTask<OperationResult<ExpandedBaseManifest>> GetExpandedManifestAsync(
        BaseManifestExpansionRequest request,
        CancellationToken cancellationToken = default);
}
