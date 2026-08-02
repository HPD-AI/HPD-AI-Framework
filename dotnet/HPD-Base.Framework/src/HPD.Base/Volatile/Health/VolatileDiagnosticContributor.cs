using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class VolatileDiagnosticContributor : IBaseDiagnosticContributor
{
    private readonly HPDBaseVolatileStoreOptions _options;

    /// <summary>Initializes a new instance.</summary>
    public VolatileDiagnosticContributor(IOptions<HPDBaseVolatileStoreOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Gets the ID.</summary>
    public string Id => _options.DiagnosticRefId;

    /// <summary>Executes the get diagnostics async operation.</summary>
    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new[]
        {
            new DiagnosticDescriptor
            {
                Id = _options.DiagnosticRefId,
                Code = "base.volatile.ready",
                Severity = DiagnosticSeverity.Info,
                TargetRef = _options.StoreId,
                Message = "HPD.BASE Volatile store is registered.",
                PublicMessage = "Volatile store is registered.",
                Category = DiagnosticCategory.Store,
                Visibility = VisibilityLevel.Admin,
                EmittedAt = DateTimeOffset.UtcNow
            }
        });
    }
}
