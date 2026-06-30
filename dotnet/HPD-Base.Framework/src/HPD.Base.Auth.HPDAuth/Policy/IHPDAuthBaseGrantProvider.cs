using HPD.Base.Policy;
using HPD.Base.Runtime;
using HPD.Base.Schema;

namespace HPD.Base.Auth.HPDAuth.Policy;

/// <summary>
/// Provides BASE grants derived from HPD.Auth-backed host state.
/// </summary>
public interface IHPDAuthBaseGrantProvider
{
    /// <summary>
    /// Gets grants for the supplied BASE policy evaluation request.
    /// </summary>
    /// <param name="request">The grant request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The grants available for the request.</returns>
    ValueTask<IReadOnlyList<AccessGrant>> GetGrantsAsync(
        HPDAuthBaseGrantRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes the context used to load HPD.Auth-derived BASE grants.
/// </summary>
public sealed record HPDAuthBaseGrantRequest
{
    /// <summary>
    /// Gets the BASE principal.
    /// </summary>
    public required PrincipalContext Principal { get; init; }

    /// <summary>
    /// Gets the BASE operation.
    /// </summary>
    public required OperationContext Operation { get; init; }

    /// <summary>
    /// Gets the target collection.
    /// </summary>
    public required CollectionDefinition Collection { get; init; }

    /// <summary>
    /// Gets the policy resource.
    /// </summary>
    public required PolicyResource Resource { get; init; }
}
