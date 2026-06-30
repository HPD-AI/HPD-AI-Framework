using HPD.Base.AspNetCore.EndpointMapping;
using HPD.Base.AspNetCore.EndpointMapping.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Extension methods for mapping HPD.BASE ASP.NET Core endpoints.
/// </summary>
public static class HPDBaseEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps HPD.BASE endpoints using default endpoint options.
    /// </summary>
    public static RouteGroupBuilder MapHPDBaseApi(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHPDBaseApi(null);

    /// <summary>
    /// Maps HPD.BASE endpoints using caller-provided endpoint options.
    /// </summary>
    public static RouteGroupBuilder MapHPDBaseApi(
        this IEndpointRouteBuilder endpoints,
        Action<HPDBaseEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new HPDBaseEndpointOptions();
        configure?.Invoke(options);
        EndpointRouteBuilderValidation.Validate(options);

        var group = endpoints.MapGroup(options.RoutePrefix);

        if (options.MapMetadata)
            MetadataEndpoints.MapPublic(group);
        if (options.MapCollections)
            CollectionEndpoints.MapPublic(group);
        if (options.MapHealth)
            HealthEndpoints.MapPublic(group);
        if (options.MapDiagnostics)
            DiagnosticEndpoints.MapPublic(group);
        if (options.MapRecords)
            RecordEndpoints.Map(group);

        if (options.MapAdminMetadata)
        {
            var admin = group.MapGroup("/admin");
            if (options.RequireAuthorizationForAdminRoutes)
                admin.RequireAuthorization(options.AdminPolicyName);

            MetadataEndpoints.MapAdmin(admin);
            CollectionEndpoints.MapAdmin(admin);
            if (options.MapHealth)
                HealthEndpoints.MapAdmin(admin);
            if (options.MapDiagnostics)
                DiagnosticEndpoints.MapAdmin(admin);
        }

        options.ConfigureRoutes?.Invoke(group);
        return group;
    }
}
