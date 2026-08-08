using HPD.Base.AspNetCore;
using HPD.Base;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Maps admin policy explain endpoints.
/// </summary>
public static class PolicyAdminExplainEndpoints
{
    /// <summary>
    /// Maps <c>POST /base/admin/policy/explain</c> relative to the supplied admin route group.
    /// </summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder endpoints, Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor>? convention = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/policy/explain", (RequestDelegate)Explain)
            .WithHPDBaseEndpoint(BaseHttpRouteNames.AdminPolicyExplain, HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.PolicyExplain, HPDBaseCapabilities.PolicyExplain, convention)
            .WithHPDBaseOpenApi(BaseHttpRouteNames.AdminPolicyExplain)
            .WithName(BaseHttpRouteNames.AdminPolicyExplain);
        return endpoints;
    }

    private static Task Explain(HttpContext httpContext) => Execute(httpContext,
        services => Explain(
            httpContext,
            services.GetRequiredService<IBasePolicyExplainService>(),
            services.GetRequiredService<IBaseHttpPrincipalContextFactory>(),
            services.GetRequiredService<IBaseHttpOperationContextFactory>(),
            services.GetRequiredService<IBaseHttpResultMapper>(),
            httpContext.RequestAborted));

    private static async Task<IResult> Explain(
        HttpContext httpContext,
        IBasePolicyExplainService explainService,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpResultMapper resultMapper,
        CancellationToken cancellationToken)
    {
        BasePolicyExplainRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<BasePolicyExplainRequest>(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is BadHttpRequestException or System.Text.Json.JsonException)
        {
            return resultMapper.ToHttpResult(
                OperationResults.ValidationFailed<BasePolicyExplainResponse>(new BaseError
                {
                    Code = "base.policyExplain.request.invalidJson",
                    Message = "Policy explain request body must be valid JSON.",
                    Category = ErrorCategory.Validation,
                    Target = "body"
                }),
                httpContext,
                new HPDBaseHttpResultMappingContext { IsAdmin = true, CorrelationId = httpContext.TraceIdentifier });
        }

        if (request is null)
        {
            var problem = OperationResults.ValidationFailed<BasePolicyExplainResponse>(new BaseError
            {
                Code = "base.policyExplain.request.required",
                Message = "Policy explain request body is required.",
                Category = ErrorCategory.Validation,
                Target = "body"
            });
            return resultMapper.ToHttpResult(problem, httpContext, new HPDBaseHttpResultMappingContext { IsAdmin = true, CorrelationId = httpContext.TraceIdentifier });
        }

        var principal = await principalFactory.CreateAsync(httpContext, cancellationToken).ConfigureAwait(false);
        var operation = operationFactory.Create(
            httpContext,
            principal,
            BaseOperationKind.AdminInspect,
            request.CollectionId,
            request.RecordId,
            OperationMode.Admin);
        var result = await explainService.ExplainAsync(request, principal, operation, cancellationToken).ConfigureAwait(false);
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Remove(HeaderNames.ETag);
        return resultMapper.ToHttpResult(result, httpContext, new HPDBaseHttpResultMappingContext
        {
            IsAdmin = true,
            CorrelationId = operation.CorrelationId
        });
    }

    private static async Task Execute(HttpContext httpContext, Func<IServiceProvider, Task<IResult>> handler)
    {
        var result = await handler(httpContext.RequestServices).ConfigureAwait(false);
        await result.ExecuteAsync(httpContext).ConfigureAwait(false);
    }
}
