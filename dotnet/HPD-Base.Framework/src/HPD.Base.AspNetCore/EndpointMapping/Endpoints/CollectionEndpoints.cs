using HPD.Base;
using HPD.Base.AspNetCore.EndpointMapping;
using HPD.Base.AspNetCore.Http;
using HPD.Base.AspNetCore.OpenApi;
using HPD.Base.AspNetCore.Results;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore.EndpointMapping.Endpoints;

internal static class CollectionEndpoints
{
    public static void MapPublic(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/collections", (RequestDelegate)ListPublic).WithHPDBaseOpenApi(BaseHttpRouteNames.CollectionsList).WithName(BaseHttpRouteNames.CollectionsList);
        endpoints.MapGet("/collections/{collectionId}", (RequestDelegate)GetPublic).WithHPDBaseOpenApi(BaseHttpRouteNames.CollectionsGet).WithName(BaseHttpRouteNames.CollectionsGet);
    }

    public static void MapAdmin(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/collections", (RequestDelegate)ListAdmin).WithHPDBaseOpenApi(BaseHttpRouteNames.AdminCollectionsList).WithName(BaseHttpRouteNames.AdminCollectionsList);
        endpoints.MapGet("/collections/{collectionId}", (RequestDelegate)GetAdmin).WithHPDBaseOpenApi(BaseHttpRouteNames.AdminCollectionsGet).WithName(BaseHttpRouteNames.AdminCollectionsGet);
    }

    private static Task ListPublic(HttpContext httpContext) => Execute(httpContext,
        services => List(httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Public, OperationMode.User, false, httpContext.RequestAborted));

    private static Task ListAdmin(HttpContext httpContext) => Execute(httpContext,
        services => List(httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Admin, OperationMode.Admin, true, httpContext.RequestAborted));

    private static async Task<IResult> List(
        HttpContext httpContext,
        IHPDBaseRuntime runtime,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpResultMapper resultMapper,
        VisibilityLevel view,
        OperationMode mode,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, isAdmin ? HPDBaseEndpointKind.AdminMetadata : HPDBaseEndpointKind.PublicMetadata, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.SchemaRead, "base", mode: mode);
        var schema = await runtime.Schema.GetSchemaAsync(principal, operation, view, cancellationToken);
        var result = schema.Status == OperationStatus.Ok
            ? new OperationResult<CollectionDefinition[]> { Status = OperationStatus.Ok, Value = schema.Value?.Collections ?? [] }
            : new OperationResult<CollectionDefinition[]> { Status = schema.Status, Error = schema.Error, Warnings = schema.Warnings, Diagnostics = schema.Diagnostics, Revision = schema.Revision, Events = schema.Events };
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation, isAdmin));
    }

    private static Task GetPublic(HttpContext httpContext) => Execute(httpContext,
        services => Get(RouteValue(httpContext, "collectionId"), httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Public, OperationMode.User, false, httpContext.RequestAborted));

    private static Task GetAdmin(HttpContext httpContext) => Execute(httpContext,
        services => Get(RouteValue(httpContext, "collectionId"), httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Admin, OperationMode.Admin, true, httpContext.RequestAborted));

    private static async Task<IResult> Get(
        string collectionId,
        HttpContext httpContext,
        IHPDBaseRuntime runtime,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpResultMapper resultMapper,
        VisibilityLevel view,
        OperationMode mode,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, isAdmin ? HPDBaseEndpointKind.AdminMetadata : HPDBaseEndpointKind.PublicMetadata, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.SchemaRead, collectionId, mode: mode);
        var result = await runtime.Schema.GetCollectionAsync(collectionId, principal, operation, view, cancellationToken);
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation, isAdmin));
    }

    private static HPDBaseHttpResultMappingContext Mapping(OperationContext operation, bool isAdmin) =>
        new() { IsAdmin = isAdmin, CorrelationId = operation.CorrelationId };

    private static string RouteValue(HttpContext httpContext, string key) =>
        httpContext.Request.RouteValues[key]?.ToString() ?? string.Empty;

    private static async Task Execute(HttpContext httpContext, Func<IServiceProvider, Task<IResult>> handler)
    {
        var result = await handler(httpContext.RequestServices);
        await result.ExecuteAsync(httpContext);
    }
}
