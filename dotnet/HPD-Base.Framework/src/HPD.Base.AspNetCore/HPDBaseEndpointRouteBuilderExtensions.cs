using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Extension methods for mapping HPD.BASE ASP.NET Core endpoints.
/// </summary>
public static class HPDBaseEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps HPD.BASE endpoints using default endpoint options.
    /// </summary>
    /// <remarks>
    /// This low-level mapper preserves the configurable endpoint surface. Control-plane
    /// hosts should prefer <see cref="MapHPDBaseControlPlaneApi(IEndpointRouteBuilder, string, Action{HPDBaseEndpointOptions}?)"/>
    /// so record and admin routes are protected by default.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The route group containing the mapped HPD.BASE endpoints.</returns>
    public static RouteGroupBuilder MapHPDBaseApi(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHPDBaseApi(null);

    /// <summary>
    /// Maps HPD.BASE endpoints using caller-provided endpoint options.
    /// </summary>
    /// <remarks>
    /// This low-level mapper is intended for hosts that need complete endpoint control.
    /// Callers are responsible for selecting route authorization, diagnostics exposure,
    /// admin metadata exposure, and policy behavior.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <param name="configure">Optional endpoint customization applied before routes are mapped.</param>
    /// <returns>The route group containing the mapped HPD.BASE endpoints.</returns>
    public static RouteGroupBuilder MapHPDBaseApi(
        this IEndpointRouteBuilder endpoints,
        Action<HPDBaseEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new HPDBaseEndpointOptions();
        configure?.Invoke(options);
        EndpointRouteBuilderValidation.Validate(options);

        var group = endpoints.MapGroup(options.RoutePrefix);
        group.AddEndpointFilter(static async (invocation, next) =>
        {
            IHPDBaseApplication? application = invocation.HttpContext.RequestServices
                .GetService<IHPDBaseApplication>();
            if (application is null ||
                application.CurrentReadiness.State == BaseApplicationReadinessState.Ready)
            {
                return await next(invocation).ConfigureAwait(false);
            }

            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "HPD.BASE is not ready.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "base.application.notReady",
                });
        });

        if (options.MapMetadata)
            MetadataEndpoints.MapPublic(group, options.PublicMetadataMode);
        if (options.MapCollections && options.PublicMetadataMode == HPDBasePublicMetadataMode.Full)
            CollectionEndpoints.MapPublic(group);
        if (options.MapHealth)
            HealthEndpoints.MapPublic(group);
        if (options.MapDiagnostics)
            DiagnosticEndpoints.MapPublic(group);
        if (options.MapRecords)
        {
            var records = group.MapGroup(string.Empty);
            if (options.RequireAuthorizationForRecordRoutes)
                records.RequireAuthorization(options.RecordPolicyName);

            RecordEndpoints.Map(records);
            RegisteredReadEndpoints.Map(records);
        }

        if (options.MapAdminMetadata || options.MapAdminPolicyExplain)
        {
            var admin = group.MapGroup("/admin");
            if (options.RequireAuthorizationForAdminRoutes)
                admin.RequireAuthorization(options.AdminPolicyName);

            if (options.MapAdminMetadata)
            {
                MetadataEndpoints.MapAdmin(admin);
                CollectionEndpoints.MapAdmin(admin);
                if (options.MapHealth)
                    HealthEndpoints.MapAdmin(admin);
                if (options.MapDiagnostics)
                    DiagnosticEndpoints.MapAdmin(admin);
            }

            if (options.MapAdminPolicyExplain)
                PolicyAdminExplainEndpoints.Map(admin);
        }

        options.ConfigureRoutes?.Invoke(group);
        return group;
    }

    /// <summary>
    /// Maps HPD.BASE endpoints with secure control-plane defaults.
    /// </summary>
    /// <remarks>
    /// The preset requires authenticated users for record routes, requires the admin
    /// policy for admin routes, disables public diagnostics, and maps the admin policy
    /// explain endpoint behind admin authorization.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <param name="routePrefix">The route prefix used for all BASE control-plane endpoints.</param>
    /// <param name="configure">Optional endpoint customization applied after the secure defaults.</param>
    /// <returns>The route group containing the mapped HPD.BASE endpoints.</returns>
    public static RouteGroupBuilder MapHPDBaseControlPlaneApi(
        this IEndpointRouteBuilder endpoints,
        string routePrefix = "/base",
        Action<HPDBaseEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapHPDBaseApi(options =>
        {
            options.RoutePrefix = routePrefix;
            options.RequireAuthorizationForRecordRoutes = true;
            options.RecordPolicyName = HPDBasePolicies.Authenticated;
            options.RequireAuthorizationForAdminRoutes = true;
            options.AdminPolicyName = HPDBasePolicies.Admin;
            options.PublicMetadataMode = HPDBasePublicMetadataMode.Minimal;
            options.MapDiagnostics = false;
            options.MapAdminPolicyExplain = true;
            configure?.Invoke(options);
        });
    }
}
