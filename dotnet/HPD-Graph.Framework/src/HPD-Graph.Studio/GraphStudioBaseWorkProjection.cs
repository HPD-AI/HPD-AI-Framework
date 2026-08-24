using HPD.AI.Platform.Studio;

namespace HPD.Graph.Studio;

/// <summary>
/// Projects exact BASE-owned L51 identities already associated with Graph semantic authority.
/// Implementations are server-only integrations and must return identities issued from the installed BASE Runtime;
/// Graph Studio never derives activation or schedule authority from graph strings.
/// </summary>
public interface IGraphStudioBaseWorkProjection
{
    /// <summary>Returns the authoritative BASE schedule associated with a graph definition, when disclosed.</summary>
    ValueTask<BaseStudioScheduleResource?> ResolveScheduleAsync(
        BaseStudioGraphDefinitionResource definition,
        CancellationToken cancellationToken);

    /// <summary>Returns the authoritative BASE activation associated with a graph execution, when disclosed.</summary>
    ValueTask<BaseStudioActivationResource?> ResolveActivationAsync(
        BaseStudioGraphExecutionResource execution,
        CancellationToken cancellationToken);
}
