using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class InMemoryDiagnosticContributor : IBaseDiagnosticContributor
{
    private readonly HPDBaseInMemoryStoreOptions _options;

    /// <summary>Initializes a new instance.</summary>
    public InMemoryDiagnosticContributor(IOptions<HPDBaseInMemoryStoreOptions> options)
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
                Code = "base.inmemory.ready",
                Severity = DiagnosticSeverity.Info,
                TargetRef = _options.StoreId,
                Message = "HPD.BASE InMemory store is registered.",
                PublicMessage = "InMemory store is registered.",
                Category = DiagnosticCategory.Store,
                Visibility = VisibilityLevel.Admin,
                EmittedAt = DateTimeOffset.UtcNow
            }
        });
    }
}
