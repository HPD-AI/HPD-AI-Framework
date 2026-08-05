
namespace HPD.Base;

/// <summary>Defines the ibase health contributor contract.</summary>
public interface IBaseHealthContributor
{
    /// <summary>Gets the ID.</summary>
    string Id { get; }
    /// <summary>Executes the get health async operation.</summary>
    ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>Defines the ibase diagnostic contributor contract.</summary>
public interface IBaseDiagnosticContributor
{
    /// <summary>Gets the ID.</summary>
    string Id { get; }
    /// <summary>Executes the get diagnostics async operation.</summary>
    ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}
