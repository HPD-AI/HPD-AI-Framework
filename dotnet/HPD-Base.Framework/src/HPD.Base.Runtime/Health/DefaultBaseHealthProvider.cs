using HPD.Base.Health;
using HPD.Base.Observability;
using HPD.Base.Results;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Observability;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Runtime.Health;

internal sealed class DefaultBaseHealthProvider : IBaseHealthProvider
{
    private readonly IBaseDescriptorRegistry _registry;
    private readonly IEnumerable<IBaseHealthContributor> _contributors;

    public DefaultBaseHealthProvider(
        IBaseDescriptorRegistry registry,
        IEnumerable<IBaseHealthContributor> contributors)
    {
        _registry = registry;
        _contributors = contributors;
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
                    health.AddRange(await contributor.GetHealthAsync(cancellationToken).ConfigureAwait(false));
                }

                return OperationResults.Ok(DescriptorViewFilter.Health(health.ToArray(), view));
            }).ConfigureAwait(false);
    }
}
