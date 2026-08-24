using Microsoft.AspNetCore.Http;

namespace HPD.AI.Platform.Studio;

/// <summary>Captures the exact authenticated, graph-pinned bootstrap invocation.</summary>
public sealed record BaseStudioBootstrapInvocation(
    HttpContext HttpContext,
    BaseStudioApplicationGraph ApplicationGraph,
    BaseStudioTransportAuthorization Authorization,
    BaseStudioBootstrapRequest Request);

/// <summary>Creates the principal-filtered bootstrap through the installed BASE Runtime.</summary>
public interface IBaseStudioBootstrapRuntime
{
    /// <summary>Authorizes and filters one finite, validated bootstrap snapshot.</summary>
    ValueTask<BaseStudioBootstrapSnapshot?> CreateAsync(
        BaseStudioBootstrapInvocation invocation,
        CancellationToken cancellationToken);
}
