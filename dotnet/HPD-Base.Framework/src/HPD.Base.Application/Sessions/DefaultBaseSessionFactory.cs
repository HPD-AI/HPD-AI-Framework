using HPD.Base.Runtime;
using HPD.Base.Runtime.Operations;

namespace HPD.Base.Application.Sessions;

internal sealed class DefaultBaseSessionFactory(
    IBaseRecordRuntime runtime,
    TimeProvider timeProvider) : IBaseSessionFactory
{
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

        return new BaseSession(
            runtime,
            timeProvider,
            Snapshot(principal),
            options);
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
