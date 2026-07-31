using HPD.Base;
using HPD.Base.InMemory;
using Microsoft.Extensions.Options;

namespace HPD.Base.InMemory;

internal sealed class InMemoryDiagnosticContributor : IBaseDiagnosticContributor
{
    private readonly HPDBaseInMemoryOptions _options;

    public InMemoryDiagnosticContributor(IOptions<HPDBaseInMemoryOptions> options)
    {
        _options = options.Value;
    }

    public string Id => _options.DiagnosticRefId;

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
