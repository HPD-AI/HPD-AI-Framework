using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

/// <summary>Maps explicit BASE endpoint audiences.</summary>
public static class HPDBaseEndpointRouteBuilderExtensions
{
    /// <summary>Maps BASE endpoint families into an already secured control-plane group.</summary>
    /// <remarks>This advanced SPI is intended for owning security integration packages.</remarks>
    internal static RouteGroupBuilder MapHPDBaseControlPlaneEndpoints(
        this RouteGroupBuilder group,
        IEndpointRouteBuilder endpoints,
        HPDBaseControlPlaneEndpointSelection selection,
        Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor> convention)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(convention);
        if (!selection.MapRecords && !selection.MapRegisteredReads && !selection.MapAdministration &&
            !selection.MapArtifactAdministration && !selection.MapPolicyExplain && !selection.MapFiles && !selection.MapRealtime && !selection.MapClientGeneration)
            throw new ArgumentException("At least one ControlPlane endpoint family must be selected.", nameof(selection));

        AddReadinessFilter(group);
        if (selection.MapRecords)
            RecordEndpoints.Map(group, HPDBaseEndpointAudience.ControlPlane, convention);
        if (selection.MapRegisteredReads)
        {
            endpoints.ServiceProvider.GetRequiredService<HPDBaseEndpointFamilySelectionState>()
                .SelectRegisteredReads(BaseReadExposure.Public, HPDBaseEndpointAudience.ControlPlane);
            RegisteredReadEndpoints.Map(group, BaseReadExposure.Public, HPDBaseEndpointAudience.ControlPlane, convention);
        }
        if (selection.MapAdministration)
        {
            var admin = group.MapGroup("/admin");
            MetadataEndpoints.MapAdmin(admin, convention);
            CollectionEndpoints.MapAdmin(admin, convention);
            HealthEndpoints.MapAdmin(admin, convention);
            DiagnosticEndpoints.MapAdmin(admin, convention);
            if (selection.MapPolicyExplain)
                PolicyAdminExplainEndpoints.Map(admin, convention);
            if (selection.MapRegisteredReads)
            {
                endpoints.ServiceProvider.GetRequiredService<HPDBaseEndpointFamilySelectionState>()
                    .SelectRegisteredReads(BaseReadExposure.Admin, HPDBaseEndpointAudience.ControlPlane);
                RegisteredReadEndpoints.Map(admin, BaseReadExposure.Admin, HPDBaseEndpointAudience.ControlPlane, convention);
            }
        }
        if (selection.MapArtifactAdministration)
            BaseAdministrationEndpoints.Map(group, endpoints.ServiceProvider, convention);
        else if (!selection.MapAdministration && selection.MapPolicyExplain)
        {
            var admin = group.MapGroup("/admin");
            PolicyAdminExplainEndpoints.Map(admin, convention);
        }
        if (selection.MapFiles)
        {
            var files = group.MapGroup("/files");
            FileObjectEndpoints.Map(files, HPDBaseEndpointAudience.ControlPlane, convention);
        }
        if (selection.MapRealtime)
            HPDBaseRealtimeEndpointRouteBuilderExtensions.MapCore(group, HPDBaseEndpointAudience.ControlPlane, convention);
        if (selection.MapClientGeneration)
        {
            endpoints.ServiceProvider.GetRequiredService<HPDBaseEndpointFamilySelectionState>()
                .SelectGeneration(HPDBaseEndpointAudience.ControlPlane);
            BaseClientGenerationEndpoints.Map(group, HPDBaseEndpointAudience.ControlPlane, convention);
        }
        SelectionMutationEndpoints.Map(group, HPDBaseEndpointAudience.ControlPlane, convention);
        ModuleMutationEndpoints.Map(group, convention);
        SubjectRetirementEndpoints.MapControlPlane(group, convention);
        ActivationAdministrationEndpoints.Map(group, convention);
        ActivationScheduleEndpoints.Map(group, convention);
        return group;
    }

    /// <summary>Maps host-selected Public discovery endpoints.</summary>
    public static RouteGroupBuilder MapHPDBasePublicApi(
        this IEndpointRouteBuilder endpoints,
        Action<HPDBasePublicEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var draft = new MutablePublicOptions();
        if (configure is not null)
        {
            var configured = new HPDBasePublicEndpointOptions();
            configure(configured);
            draft = new MutablePublicOptions(configured);
        }

        string prefix = EndpointRouteBuilderValidation.RoutePrefix(draft.RoutePrefix);
        var group = endpoints.MapGroup(prefix);
        AddReadinessFilter(group);

        MetadataEndpoints.MapPublic(group, draft.MetadataMode);
        if (draft.MetadataMode == HPDBasePublicMetadataMode.Full)
            CollectionEndpoints.MapPublic(group);
        if (draft.MapHealth)
            HealthEndpoints.MapPublic(group);
        if (draft.MapDiagnostics)
            DiagnosticEndpoints.MapPublic(group);
        return group;
    }

    /// <summary>Maps host-authorized Application endpoints.</summary>
    public static RouteGroupBuilder MapHPDBaseApplicationApi(
        this IEndpointRouteBuilder endpoints,
        HPDBaseApplicationEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AuthorizationPolicy);
        if (!options.MapRecords && !options.MapRegisteredReads && !options.MapFiles && !options.MapRealtime && !options.MapSubjectLifecycle && !options.MapClientGeneration)
            throw new ArgumentException("At least one Application endpoint family must be selected.", nameof(options));

        string prefix = EndpointRouteBuilderValidation.RoutePrefix(options.RoutePrefix);
        var group = endpoints.MapGroup(prefix)
            .WithMetadata(new HPDBaseApplicationPolicyMetadata(new string(options.AuthorizationPolicy.AsSpan())))
            .RequireAuthorization(options.AuthorizationPolicy);
        AddReadinessFilter(group);

        if (options.MapRecords)
            RecordEndpoints.Map(group, HPDBaseEndpointAudience.Application);
        if (options.MapRegisteredReads)
        {
            endpoints.ServiceProvider.GetRequiredService<HPDBaseEndpointFamilySelectionState>()
                .SelectRegisteredReads(BaseReadExposure.Public, HPDBaseEndpointAudience.Application);
            RegisteredReadEndpoints.Map(group, BaseReadExposure.Public, HPDBaseEndpointAudience.Application);
        }
        if (options.MapFiles)
        {
            var files = group.MapGroup("/files");
            endpoints.ServiceProvider.GetRequiredService<FileAspNetCoreRouteMappingState>().MarkMapped(prefix + "/files");
            FileObjectEndpoints.Map(files, HPDBaseEndpointAudience.Application);
        }
        if (options.MapRealtime)
            HPDBaseRealtimeEndpointRouteBuilderExtensions.MapCore(group, HPDBaseEndpointAudience.Application);
        if (options.MapSubjectLifecycle)
        {
            SubjectLifecycleEndpoints.Map(group);
            SubjectRetirementEndpoints.MapApplication(group);
        }
        if (options.MapClientGeneration)
        {
            endpoints.ServiceProvider.GetRequiredService<HPDBaseEndpointFamilySelectionState>()
                .SelectGeneration(HPDBaseEndpointAudience.Application);
            BaseClientGenerationEndpoints.Map(group, HPDBaseEndpointAudience.Application);
        }
        SelectionMutationEndpoints.Map(group, HPDBaseEndpointAudience.Application);
        return group;
    }

    private static void AddReadinessFilter(RouteGroupBuilder group) =>
        group.AddEndpointFilter(static async (invocation, next) =>
        {
            try
            {
                IHPDBaseApplication? application = invocation.HttpContext.RequestServices.GetService<IHPDBaseApplication>();
                if (application is null || application.CurrentReadiness.State == BaseApplicationReadinessState.Ready)
                    return await next(invocation).ConfigureAwait(false);
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "HPD.BASE is not ready.",
                    extensions: new Dictionary<string, object?> { ["code"] = "base.application.notReady" });
            }
            catch (BaseHttpCorrelationException)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "The request correlation identifier is invalid.",
                    extensions: new Dictionary<string, object?> { ["code"] = "base.http.correlation.invalid" });
            }
        });

    private sealed class MutablePublicOptions
    {
        internal MutablePublicOptions() { }
        internal MutablePublicOptions(HPDBasePublicEndpointOptions options)
        {
            RoutePrefix = options.RoutePrefix;
            MetadataMode = options.MetadataMode;
            MapHealth = options.MapHealth;
            MapDiagnostics = options.MapDiagnostics;
        }
        internal string RoutePrefix { get; init; } = "/base";
        internal HPDBasePublicMetadataMode MetadataMode { get; init; } = HPDBasePublicMetadataMode.Minimal;
        internal bool MapHealth { get; init; } = true;
        internal bool MapDiagnostics { get; init; }
    }
}
