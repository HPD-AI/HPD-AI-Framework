using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal static class CollectionEndpoints
{
    /// <summary>Executes the map public operation.</summary>
    public static void MapPublic(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/collections", (RequestDelegate)ListPublic).WithHPDBaseEndpoint(BaseHttpRouteNames.CollectionsList, HPDBaseEndpointAudience.Public, HPDBaseEndpointOperation.MetadataRead).WithHPDBaseOpenApi(BaseHttpRouteNames.CollectionsList).WithName(BaseHttpRouteNames.CollectionsList);
        endpoints.MapGet("/collections/{collectionId}", (RequestDelegate)GetPublic).WithHPDBaseEndpoint(BaseHttpRouteNames.CollectionsGet, HPDBaseEndpointAudience.Public, HPDBaseEndpointOperation.MetadataRead).WithHPDBaseOpenApi(BaseHttpRouteNames.CollectionsGet).WithName(BaseHttpRouteNames.CollectionsGet);
    }

    /// <summary>Executes the map admin operation.</summary>
    public static void MapAdmin(IEndpointRouteBuilder endpoints, Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor>? convention = null)
    {
        endpoints.MapGet("/collections", (RequestDelegate)ListAdmin).WithHPDBaseEndpoint(BaseHttpRouteNames.AdminCollectionsList, HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.MetadataRead, HPDBaseCapabilities.AdministrationMetadataRead, convention).WithHPDBaseOpenApi(BaseHttpRouteNames.AdminCollectionsList).WithName(BaseHttpRouteNames.AdminCollectionsList);
        endpoints.MapGet("/collections/{collectionId}", (RequestDelegate)GetAdmin).WithHPDBaseEndpoint(BaseHttpRouteNames.AdminCollectionsGet, HPDBaseEndpointAudience.ControlPlane, HPDBaseEndpointOperation.MetadataRead, HPDBaseCapabilities.AdministrationMetadataRead, convention).WithHPDBaseOpenApi(BaseHttpRouteNames.AdminCollectionsGet).WithName(BaseHttpRouteNames.AdminCollectionsGet);
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
        var principal = await principalFactory.CreateAsync(httpContext, cancellationToken);
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
        var principal = await principalFactory.CreateAsync(httpContext, cancellationToken);
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
