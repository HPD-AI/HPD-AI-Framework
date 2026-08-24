using System.Collections.Immutable;
using HPD.AI.Platform.Studio;
using HPD.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Studio;

/// <summary>Executes Graph's bounded read-only Studio contract through current Graph authority.</summary>
public sealed class GraphStudioEndpointSurface(IServiceProvider services, IGraphStudioInspectionAuthority inspection)
    : IBaseStudioFrameworkEndpointSurface
{
    private const long MaximumResponseBytes = 8_388_608;
    public string EndpointSurfaceId => "graph.control-plane.v1";
    public BaseStudioSha256 OperationInventoryChecksum => GraphStudioModuleRegistry.OperationInventory;
    public ImmutableArray<BaseStudioFrameworkSurfaceOperation> Operations { get; } = CreateOperations();

    public async ValueTask<BaseStudioFrameworkSurfaceResponse?> ExecuteAsync(
        BaseStudioFrameworkSurfaceRequest request, CancellationToken cancellationToken)
    {
        BaseStudioFrameworkSurfaceOperation? operation = Operations.SingleOrDefault(value => value.OperationId == request.OperationId);
        if (operation is null || operation.RequiredCapability != request.RequiredCapability ||
            request.Method != BaseStudioTransportMethod.Get || request.GetBody().Length != 0) return null;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AuthorizationResult authorized = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(request.GetPrincipal(), null, operation.RequiredCapability).ConfigureAwait(false);
        if (!authorized.Succeeded) return Error(403, "graph.studio.authorizationDenied");
        string applicationId = scope.ServiceProvider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>().ApplicationId;
        return await inspection.ObserveAsync(request.OperationId, request.RelativePath, applicationId, cancellationToken)
            .ConfigureAwait(false) ?? Error(404, "graph.studio.unavailable");
    }

    internal static ImmutableArray<BaseStudioFrameworkSurfaceOperation> CreateOperations() =>
    [
        Operation("graph.checkpoint.get", "definitions/{graphId}/executions/{executionId}/checkpoints/{checkpointId}"),
        Operation("graph.definition.get", "definitions/{graphId}"), Operation("graph.definition.list", "definitions"),
        Operation("graph.execution.get", "definitions/{graphId}/executions/{executionId}"),
        Operation("graph.execution.list", "definitions/{graphId}/executions"),
        Operation("graph.execution.suspendedNodes", "definitions/{graphId}/executions/{executionId}/suspended-nodes"),
    ];
    private static BaseStudioFrameworkSurfaceOperation Operation(string id, string path) =>
        BaseStudioFrameworkSurfaceOperation.Create(id, BaseStudioTransportMethod.Get, path,
            BaseStudioTransportPurpose.Observation, "graph.studio.inspect", 0, MaximumResponseBytes,
            TimeSpan.FromSeconds(30), [], ["application/json"], [], []);
    private static BaseStudioFrameworkSurfaceResponse Error(int status, string code) =>
        BaseStudioFrameworkSurfaceResponse.Create(status, "application/json",
            System.Text.Encoding.UTF8.GetBytes($"{{\"code\":\"{code}\"}}"), MaximumResponseBytes);
}
