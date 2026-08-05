
namespace HPD.Base;

/// <summary>Defines the ibase health provider contract.</summary>
public interface IBaseHealthProvider
{
    /// <summary>Executes the get health async operation.</summary>
    ValueTask<OperationResult<HealthDescriptor[]>> GetHealthAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default);
}
