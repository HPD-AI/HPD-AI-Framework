
namespace HPD.Base;

public interface IBaseDescriptorProvider
{
    ValueTask<OperationResult<BaseManifest>> GetManifestAsync(
        BaseManifestRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<ExpandedBaseManifest>> GetExpandedManifestAsync(
        BaseManifestExpansionRequest request,
        CancellationToken cancellationToken = default);
}
