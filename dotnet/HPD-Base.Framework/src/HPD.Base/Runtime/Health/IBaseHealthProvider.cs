using HPD.Base.Health;
using HPD.Base.Results;
using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Health;

public interface IBaseHealthProvider
{
    ValueTask<OperationResult<HealthDescriptor[]>> GetHealthAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);
}
