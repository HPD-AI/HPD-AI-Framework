
namespace HPD.Base;

/// <summary>Defines the ibase diagnostic provider contract.</summary>
public interface IBaseDiagnosticProvider
{
    /// <summary>Executes the get diagnostics async operation.</summary>
    ValueTask<OperationResult<DiagnosticDescriptor[]>> GetDiagnosticsAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);
}
