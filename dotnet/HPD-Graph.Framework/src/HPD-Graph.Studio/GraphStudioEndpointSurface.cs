using System.Collections.Immutable;
using HPD.AI.Platform.Studio;
using HPD.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Studio;

/// <summary>Executes Graph's bounded read-only Studio contract through current Graph authority.</summary>
public sealed class GraphStudioEndpointSurface(IServiceProvider services, IGraphStudioInspectionAuthority inspection)
    : IStudioFrameworkEndpointSurface
{
    private const long MaximumResponseBytes = 8_388_608;
    public string EndpointSurfaceId => "graph.control-plane.v1";
    public StudioSha256 OperationInventoryChecksum => GraphStudioModuleRegistry.OperationInventory;
    public ImmutableArray<StudioFrameworkSurfaceOperation> Operations { get; } = CreateOperations();

    public async ValueTask<StudioFrameworkSurfaceResponse?> ExecuteAsync(
        StudioFrameworkSurfaceRequest request, CancellationToken cancellationToken)
    {
        StudioFrameworkSurfaceOperation? operation = Operations.SingleOrDefault(value => value.OperationId == request.OperationId);
        if (operation is null || operation.RequiredCapability != request.RequiredCapability ||
            request.Method != StudioTransportMethod.Get || request.GetBody().Length != 0) return null;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AuthorizationResult authorized = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(request.GetPrincipal(), null, operation.RequiredCapability).ConfigureAwait(false);
        if (!authorized.Succeeded) return Error(403, "graph.studio.authorizationDenied");
        string applicationId = scope.ServiceProvider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>().ApplicationId;
        return await inspection.ObserveAsync(request.OperationId, request.RelativePath, applicationId, cancellationToken)
            .ConfigureAwait(false) ?? Error(404, "graph.studio.unavailable");
    }

    internal static ImmutableArray<StudioFrameworkSurfaceOperation> CreateOperations() =>
    [
        Operation("graph.checkpoint.get", "definitions/{graphId}/executions/{executionId}/checkpoints/{checkpointId}"),
        Operation("graph.definition.get", "definitions/{graphId}"), Operation("graph.definition.list", "definitions"),
        Operation("graph.execution.get", "definitions/{graphId}/executions/{executionId}"),
        Operation("graph.execution.list", "definitions/{graphId}/executions"),
        Operation("graph.execution.suspendedNodes", "definitions/{graphId}/executions/{executionId}/suspended-nodes"),
    ];
    private static StudioFrameworkSurfaceOperation Operation(string id, string path) =>
        StudioFrameworkSurfaceOperation.Create(id, StudioTransportMethod.Get, path,
            StudioTransportPurpose.Observation, "graph.studio.inspect", 0, MaximumResponseBytes,
            TimeSpan.FromSeconds(30), [], ["application/json"], [], []);
    private static StudioFrameworkSurfaceResponse Error(int status, string code) =>
        StudioFrameworkSurfaceResponse.Create(status, "application/json",
            System.Text.Encoding.UTF8.GetBytes($"{{\"code\":\"{code}\"}}"), MaximumResponseBytes);
}
