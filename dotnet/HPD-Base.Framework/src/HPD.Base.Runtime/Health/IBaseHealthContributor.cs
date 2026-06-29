using HPD.Base.Health;

namespace HPD.Base.Runtime.Health;

public interface IBaseHealthContributor
{
    string Id { get; }
    ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default);
}

public interface IBaseDiagnosticContributor
{
    string Id { get; }
    ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}
