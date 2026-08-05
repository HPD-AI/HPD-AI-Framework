using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal static class MetadataEndpoints
{
    /// <summary>Executes the map public operation.</summary>
    public static void MapPublic(IEndpointRouteBuilder endpoints, HPDBasePublicMetadataMode mode)
    {
        if (mode == HPDBasePublicMetadataMode.Disabled)
            return;

        endpoints.MapGet("/manifest", (RequestDelegate)ManifestPublic).WithHPDBaseOpenApi(BaseRouteIds.Manifest).WithName(BaseRouteIds.Manifest);
        endpoints.MapGet("/capabilities", (RequestDelegate)CapabilitiesPublic).WithHPDBaseOpenApi(BaseRouteIds.Capabilities).WithName(BaseRouteIds.Capabilities);

        if (mode == HPDBasePublicMetadataMode.Full)
            endpoints.MapGet("/schema", (RequestDelegate)SchemaPublic).WithHPDBaseOpenApi(BaseRouteIds.Schema).WithName(BaseRouteIds.Schema);
    }

    /// <summary>Executes the map admin operation.</summary>
    public static void MapAdmin(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/manifest", (RequestDelegate)ManifestAdmin).WithHPDBaseOpenApi(BaseHttpRouteNames.AdminManifest).WithName(BaseHttpRouteNames.AdminManifest);
        endpoints.MapGet("/capabilities", (RequestDelegate)CapabilitiesAdmin).WithHPDBaseOpenApi(BaseHttpRouteNames.AdminCapabilities).WithName(BaseHttpRouteNames.AdminCapabilities);
        endpoints.MapGet("/schema", (RequestDelegate)SchemaAdmin).WithHPDBaseOpenApi(BaseHttpRouteNames.AdminSchema).WithName(BaseHttpRouteNames.AdminSchema);
    }

    private static Task ManifestPublic(HttpContext httpContext) => Execute(httpContext,
        services => Manifest(httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpQueryBinder>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Public, OperationMode.User, false, httpContext.RequestAborted));

    private static Task ManifestAdmin(HttpContext httpContext) => Execute(httpContext,
        services => Manifest(httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpQueryBinder>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Admin, OperationMode.Admin, true, httpContext.RequestAborted));

    private static async Task<IResult> Manifest(
        HttpContext httpContext,
        IHPDBaseRuntime runtime,
        IBaseHttpPrincipalContextFactory principalFactory,
        IBaseHttpOperationContextFactory operationFactory,
        IBaseHttpQueryBinder queryBinder,
        IBaseHttpResultMapper resultMapper,
        VisibilityLevel view,
        OperationMode mode,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var principal = await principalFactory.CreateAsync(httpContext, isAdmin ? HPDBaseEndpointKind.AdminMetadata : HPDBaseEndpointKind.PublicMetadata, cancellationToken);
        var operation = operationFactory.Create(httpContext, principal, BaseOperationKind.SchemaRead, "base", mode: mode);
        var expand = queryBinder.BindManifestExpand(httpContext);
        if (expand.Status != OperationStatus.Ok)
            return resultMapper.ToHttpResult(expand, httpContext, Mapping(operation, isAdmin));

        if (expand.Value is { Length: > 0 })
        {
            var result = await runtime.Descriptors.GetExpandedManifestAsync(new BaseManifestExpansionRequest
            {
                Principal = principal,
                Operation = operation,
                View = view,
                Expand = expand.Value
            }, cancellationToken);
            return resultMapper.ToHttpResult(result, httpContext, Mapping(operation, isAdmin));
        }

        var compact = await runtime.Descriptors.GetManifestAsync(new BaseManifestRequest
        {
            Principal = principal,
            Operation = operation,
            View = view
        }, cancellationToken);
        return resultMapper.ToHttpResult(compact, httpContext, Mapping(operation, isAdmin));
    }

    private static Task CapabilitiesPublic(HttpContext httpContext) => Execute(httpContext,
        services => Capabilities(httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Public, OperationMode.User, false, httpContext.RequestAborted));

    private static Task CapabilitiesAdmin(HttpContext httpContext) => Execute(httpContext,
        services => Capabilities(httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Admin, OperationMode.Admin, true, httpContext.RequestAborted));

    private static async Task<IResult> Capabilities(
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
        var result = await runtime.Capabilities.GetCapabilitiesAsync(principal, operation, view, cancellationToken);
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation, isAdmin));
    }

    private static Task SchemaPublic(HttpContext httpContext) => Execute(httpContext,
        services => Schema(httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Public, OperationMode.User, false, httpContext.RequestAborted));

    private static Task SchemaAdmin(HttpContext httpContext) => Execute(httpContext,
        services => Schema(httpContext, services.GetRequiredService<IHPDBaseRuntime>(), services.GetRequiredService<IBaseHttpPrincipalContextFactory>(), services.GetRequiredService<IBaseHttpOperationContextFactory>(), services.GetRequiredService<IBaseHttpResultMapper>(), VisibilityLevel.Admin, OperationMode.Admin, true, httpContext.RequestAborted));

    private static async Task<IResult> Schema(
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
        var result = await runtime.Schema.GetSchemaAsync(principal, operation, view, cancellationToken);
        return resultMapper.ToHttpResult(result, httpContext, Mapping(operation, isAdmin));
    }

    private static HPDBaseHttpResultMappingContext Mapping(OperationContext operation, bool isAdmin) =>
        new() { IsAdmin = isAdmin, CorrelationId = operation.CorrelationId };

    private static async Task Execute(HttpContext httpContext, Func<IServiceProvider, Task<IResult>> handler)
    {
        var result = await handler(httpContext.RequestServices);
        await result.ExecuteAsync(httpContext);
    }
}
