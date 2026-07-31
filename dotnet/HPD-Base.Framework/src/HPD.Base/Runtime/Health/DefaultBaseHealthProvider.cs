using Microsoft.Extensions.Logging;

namespace HPD.Base;

internal sealed class DefaultBaseHealthProvider : IBaseHealthProvider
{
    private readonly IBaseDescriptorRegistry _registry;
    private readonly IEnumerable<IBaseHealthContributor> _contributors;
    private readonly ILogger<DefaultBaseHealthProvider> _logger;

    public DefaultBaseHealthProvider(
        IBaseDescriptorRegistry registry,
        IEnumerable<IBaseHealthContributor> contributors,
        ILogger<DefaultBaseHealthProvider> logger)
    {
        _registry = registry;
        _contributors = contributors;
        _logger = logger;
    }

    public async ValueTask<OperationResult<HealthDescriptor[]>> GetHealthAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = principal;

        return await HPDBaseRuntimeTelemetry.TraceRuntimeReadAsync(
            HPDBaseTelemetrySpans.RuntimeHealthGet,
            BaseOperationKind.AdminInspect,
            operation.CollectionId,
            view,
            !string.IsNullOrWhiteSpace(operation.CorrelationId),
            countAsHealthRead: true,
            countAsDiagnosticRead: false,
            async () =>
            {
                var health = new List<HealthDescriptor>(_registry.Current.Health);
                foreach (var contributor in _contributors)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        health.AddRange(await contributor.GetHealthAsync(cancellationToken).ConfigureAwait(false));
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        HPDBaseRuntimeLog.HealthContributorFailed(_logger);
                        throw;
                    }
                }

                return OperationResults.Ok(DescriptorViewFilter.Health(health.ToArray(), view));
            }).ConfigureAwait(false);
    }
}
