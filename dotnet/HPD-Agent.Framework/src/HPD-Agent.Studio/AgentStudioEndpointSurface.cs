using System.Collections.Immutable;
using HPD.AI.Platform.Studio;
using HPD.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Studio;

/// <summary>Executes Agent Studio's bounded generated-client operations through current runtime services.</summary>
public sealed class AgentStudioEndpointSurface(IServiceProvider services, IAgentStudioInspectionAuthority inspection)
    : IStudioFrameworkEndpointSurface
{
    private const long MaximumResponseBytes = 8_388_608;

    public string EndpointSurfaceId => "agent.control-plane.v1";
    public StudioSha256 OperationInventoryChecksum => AgentStudioModuleRegistry.OperationInventory;
    public ImmutableArray<StudioFrameworkSurfaceOperation> Operations { get; } = CreateOperations();

    public async ValueTask<StudioFrameworkSurfaceResponse?> ExecuteAsync(
        StudioFrameworkSurfaceRequest request, CancellationToken cancellationToken)
    {
        StudioFrameworkSurfaceOperation? operation = Operations.SingleOrDefault(value => value.OperationId == request.OperationId);
        if (operation is null || operation.RequiredCapability != request.RequiredCapability)
            return null;
        byte[] requestBody = request.GetBody();
        bool observation = operation.Purpose == StudioTransportPurpose.Observation &&
            request.Method == StudioTransportMethod.Get && requestBody.Length == 0;
        bool preflight = operation.Purpose == StudioTransportPurpose.CommandPreview &&
            request.Method == StudioTransportMethod.Post && requestBody.Length > 0;
        if (!observation && !preflight) return null;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AuthorizationResult authorized = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(request.GetPrincipal(), null, operation.RequiredCapability).ConfigureAwait(false);
        if (!authorized.Succeeded) return Error(403, "agent.studio.authorizationDenied");
        string applicationId = scope.ServiceProvider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>().ApplicationId;
        if (preflight)
            return await inspection.PreflightAsync(request.OperationId, request.RelativePath,
                requestBody.AsMemory(), applicationId, cancellationToken).ConfigureAwait(false) ??
                Error(422, "agent.studio.preflightRejected");
        return await inspection.ObserveAsync(
                request.OperationId, request.RelativePath, request.Query, applicationId,
                request.GetPrincipal(), cancellationToken)
            .ConfigureAwait(false) ?? Error(404, "agent.studio.unavailable");
    }

    internal static ImmutableArray<StudioFrameworkSurfaceOperation> CreateOperations() =>
    [
        Operation("agent.configuration.get", "agents/{agentId}/configuration"),
        PreviewOperation("agent.configuration.preflight", "agents/{agentId}/configuration:preflight"),
        Operation("agent.get", "agents/{agentId}"),
        Operation("agent.list", "agents"),
        Operation("agent.sessions.list", "agents/{agentId}/sessions"),
        Operation("agent.thread.activity", "agents/{agentId}/sessions/{sessionId}/threads/{threadId}/activity"),
        Operation("event.get", "events/{eventId}"),
        Operation("events.list", "events"),
        Operation("execution.get", "agents/{agentId}/sessions/{sessionId}/threads/{threadId}/executions/{executionId}"),
        Operation("execution.list", "agents/{agentId}/sessions/{sessionId}/threads/{threadId}/executions"),
        Operation("function.action.get", "harnesses/{harnessId}/functions/{functionId}/actions/{actionId}"),
        Operation("function.get", "harnesses/{harnessId}/functions/{functionId}"),
        Operation("harness.get", "harnesses/{harnessId}"),
        Operation("harness.list", "harnesses"),
        Operation("metrics.query", "observability/metrics"),
        Operation("observability.summary", "observability/summary"),
        Operation("session.get", "sessions/{sessionId}"),
        Operation("session.list", "sessions"),
        Operation("thread.get", "sessions/{sessionId}/threads/{threadId}"),
        Operation("thread.list", "sessions/{sessionId}/threads"),
        Operation("trace.get", "traces/{traceId}"),
        Operation("trace.list", "traces"),
    ];

    private static StudioFrameworkSurfaceOperation Operation(string id, string path) =>
        StudioFrameworkSurfaceOperation.Create(id, StudioTransportMethod.Get, path,
            StudioTransportPurpose.Observation, "base.studio.resource.inspect", 0, MaximumResponseBytes,
            TimeSpan.FromSeconds(30), [], ["application/json"], [], []);

    private static StudioFrameworkSurfaceOperation PreviewOperation(string id, string path) =>
        StudioFrameworkSurfaceOperation.Create(id, StudioTransportMethod.Post, path,
            StudioTransportPurpose.CommandPreview, "base.studio.action.preview", 1_048_576, MaximumResponseBytes,
            TimeSpan.FromSeconds(30), ["application/json"], ["application/json"], [], []);

    private static StudioFrameworkSurfaceResponse Error(int status, string code) =>
        StudioFrameworkSurfaceResponse.Create(status, "application/json",
            System.Text.Encoding.UTF8.GetBytes($"{{\"code\":\"{code}\"}}"), MaximumResponseBytes);
}

/// <summary>Projects installed Agent runtime authority into bounded Agent Studio observations.</summary>
public interface IAgentStudioInspectionAuthority
{
    ValueTask<StudioFrameworkSurfaceResponse?> ObserveAsync(
        string operationId, string relativePath, string query, string applicationId,
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken);
    ValueTask<StudioFrameworkSurfaceResponse?> PreflightAsync(
        string operationId, string relativePath, ReadOnlyMemory<byte> requestBody,
        string applicationId, CancellationToken cancellationToken);
    ValueTask<bool> ExistsAsync(StudioResourceIdentity resource, CancellationToken cancellationToken);
}
