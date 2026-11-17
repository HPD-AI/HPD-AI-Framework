using Microsoft.AspNetCore.Http;

namespace HPD.AI.Platform.Studio;

/// <summary>Captures the exact authenticated, graph-pinned bootstrap invocation.</summary>
public sealed record StudioBootstrapInvocation(
    HttpContext HttpContext,
    StudioApplicationGraph ApplicationGraph,
    StudioTransportAuthorization Authorization,
    StudioBootstrapRequest Request);

/// <summary>Creates the principal-filtered bootstrap through the installed BASE Runtime.</summary>
public interface IStudioBootstrapRuntime
{
    /// <summary>Authorizes and filters one finite, validated bootstrap snapshot.</summary>
    ValueTask<StudioBootstrapSnapshot?> CreateAsync(
        StudioBootstrapInvocation invocation,
        CancellationToken cancellationToken);
}
