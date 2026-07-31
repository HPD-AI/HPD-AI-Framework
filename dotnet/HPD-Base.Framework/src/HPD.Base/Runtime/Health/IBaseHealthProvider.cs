
namespace HPD.Base;

public interface IBaseHealthProvider
{
    ValueTask<OperationResult<HealthDescriptor[]>> GetHealthAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);
}
