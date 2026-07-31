
namespace HPD.Base;

public interface IBaseDiagnosticProvider
{
    ValueTask<OperationResult<DiagnosticDescriptor[]>> GetDiagnosticsAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);
}
