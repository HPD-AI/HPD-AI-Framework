using Microsoft.Extensions.Logging;

namespace HPD.Base;

internal sealed class DefaultBaseDiagnosticProvider : IBaseDiagnosticProvider
{
    private readonly IBaseDescriptorRegistry _registry;
    private readonly IEnumerable<IBaseDiagnosticContributor> _contributors;
    private readonly ILogger<DefaultBaseDiagnosticProvider> _logger;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBaseDiagnosticProvider(
        IBaseDescriptorRegistry registry,
        IEnumerable<IBaseDiagnosticContributor> contributors,
        ILogger<DefaultBaseDiagnosticProvider> logger)
    {
        _registry = registry;
        _contributors = contributors;
        _logger = logger;
    }

    /// <summary>Executes the get diagnostics async operation.</summary>
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
                    try
                    {
                        diagnostics.AddRange(await contributor.GetDiagnosticsAsync(cancellationToken).ConfigureAwait(false));
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        HPDBaseRuntimeLog.DiagnosticContributorFailed(_logger);
                        throw;
                    }
                }

                return OperationResults.Ok(DescriptorViewFilter.Diagnostics(diagnostics.ToArray(), view));
            }).ConfigureAwait(false);
    }
}
