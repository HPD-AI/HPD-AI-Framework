using HPD.Agent.Packages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.Packages;

public static class HpdPackageManagementEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapHPDAgentPackageManagement(
        this IEndpointRouteBuilder endpoints,
        string routePrefix = "/api/hpd-agent/packages")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(routePrefix);

        var group = endpoints.MapGroup(routePrefix)
            .WithTags("HPD Packages");

        group.MapGet("/", (HpdAspNetCorePackageRuntime runtime) =>
            Results.Ok(runtime.List()));

        group.MapGet("/{id}", (string id, HpdAspNetCorePackageRuntime runtime) =>
            runtime.Find(id) is { } package
                ? Results.Ok(package)
                : Results.NotFound(new HpdPackageErrorResponse($"Package '{id}' is not loaded.")));

        group.MapPost("/{id}/prepare", (string id, string? scope, HpdPackageChangeOperation? operation, HpdAspNetCorePackageRuntime runtime) =>
            ExecutePackagePrepare(() => runtime.PrepareRegistered(
                id,
                scope,
                operation ?? HpdPackageChangeOperation.Enable)));

        group.MapPost("/commit", (HpdPackagePrepareRequest request, HpdAspNetCorePackageRuntime runtime) =>
            ExecutePackageAction(() => runtime.CommitRegistered(request)));

        group.MapPost("/{id}/enable", (string id, string? scope, HpdAspNetCorePackageRuntime runtime) =>
            ExecutePackageAction(() => runtime.EnableRegistered(id, scope)));

        group.MapPost("/{id}/reload", (string id, string? scope, HpdAspNetCorePackageRuntime runtime) =>
            ExecutePackageAction(() => runtime.ReloadRegistered(id, scope)));

        group.MapPost("/{id}/disable", (string id, HpdAspNetCorePackageRuntime runtime) =>
            Results.Ok(runtime.Disable(id)));

        return group;
    }

    private static IResult ExecutePackageAction(Func<HpdPackageActionResponse> action)
    {
        try
        {
            return Results.Ok(action());
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new HpdPackageErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new HpdPackageErrorResponse(ex.Message));
        }
    }

    private static IResult ExecutePackagePrepare(Func<HpdPackagePrepareResponse> action)
    {
        try
        {
            return Results.Ok(action());
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new HpdPackageErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new HpdPackageErrorResponse(ex.Message));
        }
    }
}
