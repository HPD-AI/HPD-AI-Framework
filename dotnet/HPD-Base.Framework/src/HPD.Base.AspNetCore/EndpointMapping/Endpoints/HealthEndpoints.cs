using HPD.Base;
using HPD.Base.AspNetCore.EndpointMapping;
using HPD.Base.AspNetCore.Http;
using HPD.Base.AspNetCore.Results;
using HPD.Base.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore.EndpointMapping.Endpoints;

internal static class HealthEndpoints
{
    public static void MapPublic(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/health", (RequestDelegate)Public).WithName(BaseRouteIds.Health);

    public static void MapAdmin(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/health", (RequestDelegate)Admin).WithName(BaseHttpRouteNames.AdminHealth);

    private static Task Public(HttpContext httpContext) => Execute(httpContext,
        services => Handle(httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Public, OperationMode.User, false, httpContext.RequestAborted));

    private static Task Admin(HttpContext httpContext) => Execute(httpContext,
        services => Handle(httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Admin, OperationMode.Admin, true, httpContext.RequestAborted));

    private static async Task<IResult> Handle(HttpContext httpContext, IHPDBaseRuntime runtime, IBaseHttpPrincipalContextFactory principalFactory, IBaseHttpOperationContextFactory operationFactory, IBaseHttpResultMapper resultMapper, VisibilityLevel view, OperationMode mode, bool isAdmin, CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, isAdmin ? HPDBaseEndpointKind.AdminMetadata : HPDBaseEndpointKind.PublicMetadata, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.SchemaRead, "base", mode: mode);
        var result = await runtime.Health.GetHealthAsync(principal, operation, view, cancellationToken);
        return resultMapper.ToHttpResult(result, httpContext, new HPDBaseHttpResultMappingContext { IsAdmin = isAdmin, CorrelationId = operation.CorrelationId });
    }

    private static async Task Execute(HttpContext httpContext, Func<IServiceProvider, Task<IResult>> handler)
    {
        var result = await handler(httpContext.RequestServices);
        await result.ExecuteAsync(httpContext);
    }
}
