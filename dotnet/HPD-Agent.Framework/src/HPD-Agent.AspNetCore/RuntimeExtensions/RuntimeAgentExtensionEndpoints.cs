using HPD.Agent.AspNetCore.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.RuntimeExtensions;

public static class RuntimeAgentExtensionEndpoints
{
    public static RouteGroupBuilder MapRuntimeAgentExtensionManagement(
        this RouteGroupBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(
                "/runtime-extension-schemas",
                RegisterAsync)
            .WithName("RegisterRuntimeAgentExtensionSchema")
            .WithSummary("Register one authorized immutable runtime extension schema");
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RuntimeAgentExtensionSchema schema,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var registry = context.RequestServices.GetService<RuntimeAgentExtensionRegistry>();
        if (registry is null)
        {
            return TypedResults.Problem(
                title: "Runtime extensions are not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        var authorizer = context.RequestServices.GetService<IRuntimeAgentExtensionMutationAuthorizer>();
        if (authorizer is null)
        {
            return TypedResults.Problem(
                title: "Runtime schema management has no authorizer.",
                statusCode: StatusCodes.Status403Forbidden);
        }
        var authorization = await authorizer.AuthorizeAsync(context, schema, cancellationToken)
            .ConfigureAwait(false);
        if (!authorization.Allowed)
        {
            return TypedResults.Problem(
                title: "Runtime schema registration was denied.",
                detail: authorization.Reason,
                statusCode: StatusCodes.Status403Forbidden);
        }
        if (schema.Usage.HasFlag(RuntimeAgentExtensionUsage.Input))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(schema.Usage)] =
                [
                    "Input schemas must be registered by trusted host code together with an input handler."
                ]
            });
        }

        try
        {
            var descriptor = await registry.RegisterManagedAsync(schema, cancellationToken)
                .ConfigureAwait(false);
            return Results.Json(
                descriptor,
                HPDAgentAspNetCoreJsonSerializerContext.Default.RuntimeAgentExtensionDescriptor,
                statusCode: StatusCodes.Status201Created);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["schema"] = [exception.Message]
            });
        }
    }
}
