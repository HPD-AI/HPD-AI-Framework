using HPD.Base.Health;
using HPD.Base.Observability;
using HPD.Base.Results;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Observability;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Runtime.Health;

internal sealed class DefaultBaseDiagnosticProvider : IBaseDiagnosticProvider
{
    private readonly IBaseDescriptorRegistry _registry;
    private readonly IEnumerable<IBaseDiagnosticContributor> _contributors;

    public DefaultBaseDiagnosticProvider(
        IBaseDescriptorRegistry registry,
        IEnumerable<IBaseDiagnosticContributor> contributors)
    {
        _registry = registry;
        _contributors = contributors;
    }

    public async ValueTask<OperationResult<DiagnosticDescriptor[]>> GetDiagnosticsAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = principal;

        return await HPDBaseRuntimeTelemetry.TraceRuntimeReadAsync(
            HPDBaseTelemetrySpans.RuntimeDiagnosticsGet,
            BaseOperationKind.AdminInspect,
            operation.CollectionId,
            view,
            !string.IsNullOrWhiteSpace(operation.CorrelationId),
            countAsHealthRead: false,
            countAsDiagnosticRead: true,
            async () =>
            {
                var diagnostics = new List<DiagnosticDescriptor>(_registry.Current.Diagnostics);
                foreach (var contributor in _contributors)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    diagnostics.AddRange(await contributor.GetDiagnosticsAsync(cancellationToken).ConfigureAwait(false));
                }

                return OperationResults.Ok(DescriptorViewFilter.Diagnostics(diagnostics.ToArray(), view));
            }).ConfigureAwait(false);
    }
}
