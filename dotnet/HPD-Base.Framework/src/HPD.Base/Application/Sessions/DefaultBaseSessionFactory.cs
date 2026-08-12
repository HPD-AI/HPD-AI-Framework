using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBaseSessionFactory(
    IBaseRecordRuntime runtime,
    TimeProvider timeProvider,
    IServiceProvider services,
    IOptions<HPDBaseRuntimeOptions> runtimeOptions,
    IOptions<HPDBaseSchemaOptions> schemaOptions) : IBaseSessionFactory
{
    /// <summary>Executes the for operation.</summary>
    public BaseSession For(
        PrincipalContext principal,
        Action<BaseSessionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var options = new BaseSessionOptions
        {
            TenantId = principal.CurrentTenantId,
            Mode = principal.AuthenticationState switch
            {
                PrincipalAuthenticationState.System => OperationMode.System,
                PrincipalAuthenticationState.Admin => OperationMode.Admin,
                PrincipalAuthenticationState.Service => OperationMode.Service,
                _ => OperationMode.User,
            },
        };
        configure?.Invoke(options);

        IHPDBaseApplication? application = services.GetService<IHPDBaseApplication>();
        IBaseRecordRuntime sessionRuntime = application is null
            ? runtime
            : new ReadinessBoundRecordRuntime(runtime, application);
        return new BaseSession(
            sessionRuntime,
            timeProvider,
            Snapshot(principal),
            options,
            services.GetService<IFileObjectService>(),
            services.GetService<IBaseDependencyReferenceFactory>(),
            services.GetService<IBaseRealtimeFeedSource>(),
            services.GetService<IBaseLiveQueryCoordinator>(),
            services.GetService<IBaseRegisteredReadRuntime>(),
            runtimeOptions.Value.Limits.MaxPageSize,
            services,
            schemaOptions.Value.ApplicationId);
    }

    private static PrincipalContext Snapshot(PrincipalContext principal) =>
        principal with
        {
            Claims = principal.Claims?
                .Select(claim => claim with { })
                .ToArray(),
            Roles = principal.Roles?.ToArray(),
            Subjects = principal.Subjects?
                .Select(subject => subject with { })
                .ToArray(),
            TenantMemberships = principal.TenantMemberships?
                .Select(membership => membership with
                {
                    Roles = membership.Roles?.ToArray(),
                })
                .ToArray(),
        };
}
