using HPD.Base.Descriptors;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Runtime.Descriptors;

internal sealed class DefaultBaseDescriptorProvider : IBaseDescriptorProvider
{
    private static readonly HashSet<string> ExpansionTokens = new(StringComparer.Ordinal)
    {
        "schema",
        "capabilities",
        "health",
        "diagnostics",
        "collections"
    };

    private readonly IBaseDescriptorRegistry _registry;

    public DefaultBaseDescriptorProvider(IBaseDescriptorRegistry registry)
    {
        _registry = registry;
    }

    public ValueTask<OperationResult<BaseManifest>> GetManifestAsync(
        BaseManifestRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok(DescriptorViewFilter.Manifest(_registry.Current, request.View)));
    }

    public ValueTask<OperationResult<ExpandedBaseManifest>> GetExpandedManifestAsync(
        BaseManifestExpansionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expand = request.Expand ?? [];
        if (expand.Any(token => !ExpansionTokens.Contains(token)))
        {
            return ValueTask.FromResult(OperationResults.ValidationFailed<ExpandedBaseManifest>(
                new BaseError
                {
                    Code = "base.runtime.manifest.expand.unknown",
                    Message = "Unknown manifest expansion token.",
                    Category = ErrorCategory.Validation
                }));
        }

        var snapshot = _registry.Current;
        var manifest = DescriptorViewFilter.Manifest(snapshot, request.View);
        var expanded = new ExpandedBaseManifest
        {
            Manifest = manifest,
            Schema = expand.Contains("schema", StringComparer.Ordinal) ? DescriptorViewFilter.Schema(snapshot, request.View) : null,
            Capabilities = expand.Contains("capabilities", StringComparer.Ordinal) ? DescriptorViewFilter.Capabilities(snapshot, request.View) : null,
            Health = expand.Contains("health", StringComparer.Ordinal) ? DescriptorViewFilter.Health(snapshot.Health, request.View) : null,
            Diagnostics = expand.Contains("diagnostics", StringComparer.Ordinal) ? DescriptorViewFilter.Diagnostics(snapshot.Diagnostics, request.View) : null,
            Collections = expand.Contains("collections", StringComparer.Ordinal) ? DescriptorViewFilter.Schema(snapshot, request.View).Collections : null,
            ETag = manifest.ETag
        };

        return ValueTask.FromResult(OperationResults.Ok(expanded));
    }
}
