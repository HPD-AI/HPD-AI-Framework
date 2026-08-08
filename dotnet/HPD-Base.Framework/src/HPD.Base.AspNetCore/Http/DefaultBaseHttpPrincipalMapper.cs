using System.Security.Claims;
using HPD.Base;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

internal sealed class DefaultBaseHttpPrincipalMapper(IOptions<HPDBaseAspNetCoreOptions> options)
    : IBaseHttpPrincipalMapper
{
    public ValueTask<PrincipalContext> MapAsync(
        HttpContext httpContext,
        HPDBaseEndpointDescriptor endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();
        if (endpoint.Audience == HPDBaseEndpointAudience.ControlPlane)
            throw new InvalidOperationException("Generic principal mapping cannot serve a control-plane endpoint.");

        ClaimsPrincipal user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return ValueTask.FromResult(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Anonymous,
                SubjectKind = AccessSubjectKind.Anonymous,
                AuthSource = "aspnet"
            });

        HPDBaseHttpAuthOptions auth = options.Value.Auth;
        string? subjectId = BasePrincipalProjectionGuard.Single(user, auth.SubjectIdClaimTypes, 256, "subject");
        string? displayName = BasePrincipalProjectionGuard.Single(user, auth.DisplayNameClaimTypes, 256, "display name");
        if (displayName is null && user.Identity.Name is { } identityName)
            displayName = BasePrincipalProjectionGuard.Owned(identityName, 256, "display name");
        string[] roles = BasePrincipalProjectionGuard.Multiple(user, auth.RoleClaimTypes, 128, auth.MaxRoles, "role");
        bool admin = roles.Any(role => auth.AdminRoleNames.Contains(role, StringComparer.Ordinal));
        string? serviceId = BasePrincipalProjectionGuard.Single(user, auth.ServicePrincipalClaimTypes, 256, "service principal");
        ClaimValue[] claims = BasePrincipalProjectionGuard.CopiedClaims(user, auth.CopiedClaimTypes, auth.MaxClaims);
        List<AccessSubject> subjects = [];
        if (subjectId is not null) subjects.Add(new() { Kind = AccessSubjectKind.User, Id = subjectId, Source = "aspnet" });
        if (serviceId is not null) subjects.Add(new() { Kind = AccessSubjectKind.ServicePrincipal, Id = serviceId, Source = "aspnet" });
        subjects.AddRange(roles.Select(static role => new AccessSubject { Kind = AccessSubjectKind.Role, Id = role, Source = "aspnet" }));
        if (admin) subjects.Add(new() { Kind = AccessSubjectKind.Admin, Id = subjectId ?? "admin", Source = "aspnet" });
        string? tenant = BasePrincipalProjectionGuard.Single(user, auth.TenantIdClaimType, 256, "tenant");
        string[] membershipValues = BasePrincipalProjectionGuard.Multiple(user, auth.TenantMembershipClaimType is null ? [] : [auth.TenantMembershipClaimType], 512, auth.MaxClaims, "tenant memberships");
        TenantMembership[] memberships = membershipValues
            .SelectMany(static value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(value => BasePrincipalProjectionGuard.Owned(value, 256, "tenant membership"))
            .Append(tenant).Where(static value => value is not null).Distinct(StringComparer.Ordinal)
            .Select(static value => new TenantMembership { TenantId = value!, Source = "aspnet" }).ToArray();
        return ValueTask.FromResult(new PrincipalContext
        {
            AuthenticationState = admin ? PrincipalAuthenticationState.Admin : serviceId is null ? PrincipalAuthenticationState.Authenticated : PrincipalAuthenticationState.Service,
            SubjectKind = admin ? AccessSubjectKind.Admin : serviceId is not null ? AccessSubjectKind.ServicePrincipal : subjectId is null ? AccessSubjectKind.Authenticated : AccessSubjectKind.User,
            SubjectId = subjectId,
            DisplayName = displayName,
            Roles = roles.Length == 0 ? null : roles,
            Claims = claims.Length == 0 ? null : claims,
            Subjects = subjects.Count == 0 ? null : [.. subjects],
            TenantMemberships = memberships.Length == 0 ? null : memberships,
            CurrentTenantId = tenant,
            SessionId = BasePrincipalProjectionGuard.Single(user, auth.SessionIdClaimType, 256, "session"),
            AuthSource = "aspnet"
        });
    }

}
