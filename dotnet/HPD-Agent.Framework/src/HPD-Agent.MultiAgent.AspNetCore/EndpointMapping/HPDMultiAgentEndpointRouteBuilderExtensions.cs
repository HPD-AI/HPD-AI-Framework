using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.MultiAgent.AspNetCore.EndpointMapping;
using HPD.Agent.MultiAgent.AspNetCore.Serialization;
using HPD.Base;
using HPD.Base.AspNetCore;
using HPD.Graph.Base;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.MultiAgent.AspNetCore;

/// <summary>Maps multi-agent discovery and durable activation enqueue routes.</summary>
public static class HPDMultiAgentEndpointRouteBuilderExtensions
{
    /// <summary>Maps routes using the default prefix.</summary>
    public static RouteGroupBuilder MapHPDMultiAgentApi(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHPDMultiAgentApi(static _ => { });

    /// <summary>Maps routes using a selected prefix.</summary>
    public static RouteGroupBuilder MapHPDMultiAgentApi(this IEndpointRouteBuilder endpoints, string routePrefix) =>
        endpoints.MapHPDMultiAgentApi(options => options.RoutePrefix = routePrefix);

    /// <summary>Maps graph-installed workflow discovery and enqueue routes.</summary>
    public static RouteGroupBuilder MapHPDMultiAgentApi(
        this IEndpointRouteBuilder endpoints,
        Action<HPDMultiAgentEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = new HPDMultiAgentEndpointOptions();
        configure?.Invoke(options);
        BaseGraphActivationDefinition[] graphs = endpoints.ServiceProvider
            .GetServices<BaseGraphActivationDefinition>()
            .OrderBy(static graph => graph.GraphId, StringComparer.Ordinal)
            .ToArray();
        var group = endpoints.MapGroup(options.RoutePrefix);
        group.MapGet("/workflows", () => Results.Json(new MultiAgentWorkflowListResponse
        {
            Workflows = graphs.Select(static graph => new MultiAgentWorkflowSummaryDto
            {
                WorkflowId = graph.GraphId,
                GraphVersion = graph.GraphVersion,
                DefinitionVersion = graph.Registration.Definition.Version,
                GraphChecksum = Convert.ToHexStringLower(graph.GraphChecksum.Span),
            }).ToArray(),
        }, HPDMultiAgentAspNetCoreJsonSerializerContext.Default.MultiAgentWorkflowListResponse));
        foreach (BaseGraphActivationDefinition graph in graphs)
        {
            BaseGraphActivationDefinition captured = graph;
            group.MapPost($"/workflows/{graph.GraphId}/runs", (RequestDelegate)(context => StartRunAsync(context, captured)));
        }
        options.ConfigureRoutes?.Invoke(group);
        return group;
    }

    private static async Task StartRunAsync(HttpContext context, BaseGraphActivationDefinition graph)
    {
        MultiAgentRunRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                HPDMultiAgentAspNetCoreJsonSerializerContext.Default.MultiAgentRunRequest,
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false);
            return;
        }
        if (request is null || !context.Request.Headers.TryGetValue(BaseHttpHeaders.IdempotencyKey, out var keys)
            || keys.Count != 1 || request.DueAtUnixMilliseconds is < 0)
        {
            await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false);
            return;
        }
        using var inputBuffer = new MemoryStream();
        using (var inputWriter = new Utf8JsonWriter(inputBuffer)) request.Input.WriteTo(inputWriter);
        byte[] source = inputBuffer.ToArray();
        byte[] canonical;
        try { canonical = BaseGraphActivationRegistration.CanonicalJson(source); }
        catch (JsonException)
        {
            await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false);
            return;
        }
        string executionId = string.IsNullOrWhiteSpace(request.ExecutionId)
            ? $"run:{Convert.ToHexStringLower(SHA256.HashData(canonical))}:{keys[0]}"
            : request.ExecutionId;
        BaseGraphActivationInput input;
        BaseMutationRequestIdentity identity;
        try
        {
            input = graph.CreateInput(executionId, canonical);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData("hpd.multiAgent.activation.v1\0"u8);
            hash.AppendData(graph.GraphChecksum.Span);
            hash.AppendData(SHA256.HashData(canonical));
            if (request.DueAtUnixMilliseconds is long fingerprintDueAt)
                hash.AppendData(Encoding.UTF8.GetBytes(fingerprintDueAt.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            identity = BaseMutationRequestIdentity.Create(
                $"multi-agent:{graph.GraphId}", "enqueue", keys[0]!,
                BaseMutationRequestFingerprint.Create(hash.GetHashAndReset()));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            await Problem(context, 400, "base.activation.invalid").ConfigureAwait(false);
            return;
        }
        PrincipalContext principal = await context.RequestServices
            .GetRequiredService<IBaseHttpPrincipalContextFactory>()
            .CreateAsync(context, context.RequestAborted).ConfigureAwait(false);
        BaseSession session = context.RequestServices.GetRequiredService<IBaseSessionFactory>().For(principal);
        OperationResult<BaseActivationEnqueueResult> result = await session.Activations
            .Get(graph.Registration.Identity)
            .EnqueueAsync(input, identity, new BaseActivationEnqueueOptions
            {
                DueAt = request.DueAtUnixMilliseconds is long dueAt
                    ? DateTimeOffset.FromUnixTimeMilliseconds(dueAt)
                    : null,
            }, context.RequestAborted).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null)
        {
            await Problem(context, StatusCode(result.Status),
                result.Error?.Code ?? "base.activation.storeError").ConfigureAwait(false);
            return;
        }
        await Results.Json(new MultiAgentRunAcceptedResult
        {
            ExecutionId = executionId,
            ActivationId = result.Value.ActivationId,
            State = result.Value.State,
            Disposition = result.Value.Disposition,
        }, HPDMultiAgentAspNetCoreJsonSerializerContext.Default.MultiAgentRunAcceptedResult)
            .ExecuteAsync(context).ConfigureAwait(false);
    }

    private static Task Problem(HttpContext context, int status, string code) => Results.Problem(
        statusCode: status,
        title: "The durable multi-agent run request failed.",
        extensions: new Dictionary<string, object?> { ["hpd.error.code"] = code }).ExecuteAsync(context);

    private static int StatusCode(HPD.Base.OperationStatus status) => status switch
    {
        HPD.Base.OperationStatus.ValidationFailed => 400,
        HPD.Base.OperationStatus.PolicyDenied => 403,
        HPD.Base.OperationStatus.NotFound => 404,
        HPD.Base.OperationStatus.Conflict => 409,
        HPD.Base.OperationStatus.Unsupported or HPD.Base.OperationStatus.CapabilityUnavailable => 424,
        _ => 500,
    };
}
