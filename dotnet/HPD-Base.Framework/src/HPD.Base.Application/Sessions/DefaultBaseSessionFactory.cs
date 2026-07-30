using HPD.Base.Runtime;
using HPD.Base.Runtime.Operations;
using HPD.Base.Dependencies;
using HPD.Base.Files.Objects;
using HPD.Base.LiveQuery;
using HPD.Base.Realtime.Feeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HPD.Base.Runtime.Configuration;

namespace HPD.Base.Application.Sessions;

internal sealed class DefaultBaseSessionFactory(
    IBaseRecordRuntime runtime,
    TimeProvider timeProvider,
    IServiceProvider services,
    IEnumerable<IBaseApplicationInitializer> initializers,
    IOptions<HPDBaseRuntimeOptions> runtimeOptions) : IBaseSessionFactory
{
    public BaseSession For(
        PrincipalContext principal,
        Action<BaseSessionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        foreach (IBaseApplicationInitializer initializer in initializers)
        {
            initializer.Initialize();
        }

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

        return new BaseSession(
            runtime,
            timeProvider,
            Snapshot(principal),
            options,
            services.GetService<IFileObjectService>(),
            services.GetService<IBaseDependencyReferenceFactory>(),
            services.GetService<IBaseRealtimeFeedSource>(),
            services.GetService<IBaseLiveQueryCoordinator>(),
            runtimeOptions.Value.Limits.MaxPageSize);
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

internal interface IBaseApplicationInitializer
{
    void Initialize();
}
