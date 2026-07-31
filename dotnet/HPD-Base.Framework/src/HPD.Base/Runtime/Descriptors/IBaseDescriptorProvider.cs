using HPD.Base.Descriptors;
using HPD.Base.Results;
using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Descriptors;

public interface IBaseDescriptorProvider
{
    ValueTask<OperationResult<BaseManifest>> GetManifestAsync(
        BaseManifestRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<ExpandedBaseManifest>> GetExpandedManifestAsync(
        BaseManifestExpansionRequest request,
        CancellationToken cancellationToken = default);
}
