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
        string? subjectId = First(user, auth.SubjectIdClaimTypes);
        string? displayName = First(user, auth.DisplayNameClaimTypes) ?? user.Identity.Name;
        string[] roles = Values(user, auth.RoleClaimTypes).Distinct(StringComparer.Ordinal).Take(auth.MaxRoles).ToArray();
        bool admin = roles.Any(role => auth.AdminRoleNames.Contains(role, StringComparer.Ordinal));
        string? serviceId = First(user, auth.ServicePrincipalClaimTypes);
        HashSet<string> copiedTypes = auth.CopiedClaimTypes.ToHashSet(StringComparer.Ordinal);
        ClaimValue[] claims = user.Claims.Where(claim => copiedTypes.Contains(claim.Type)).Take(auth.MaxClaims)
            .Select(static claim => new ClaimValue { Type = claim.Type, Value = claim.Value, Issuer = claim.Issuer, ValueType = claim.ValueType }).ToArray();
        List<AccessSubject> subjects = [];
        if (subjectId is not null) subjects.Add(new() { Kind = AccessSubjectKind.User, Id = subjectId, Source = "aspnet" });
        if (serviceId is not null) subjects.Add(new() { Kind = AccessSubjectKind.ServicePrincipal, Id = serviceId, Source = "aspnet" });
        subjects.AddRange(roles.Select(static role => new AccessSubject { Kind = AccessSubjectKind.Role, Id = role, Source = "aspnet" }));
        if (admin) subjects.Add(new() { Kind = AccessSubjectKind.Admin, Id = subjectId ?? "admin", Source = "aspnet" });
        string? tenant = Claim(user, auth.TenantIdClaimType);
        TenantMembership[] memberships = Values(user, auth.TenantMembershipClaimType is null ? [] : [auth.TenantMembershipClaimType])
            .SelectMany(static value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
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
            SessionId = Claim(user, auth.SessionIdClaimType),
            AuthSource = "aspnet"
        });
    }

    private static string? First(ClaimsPrincipal principal, IEnumerable<string> types) =>
        types.Select(type => Claim(principal, type)).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    private static string? Claim(ClaimsPrincipal principal, string? type) => type is null ? null :
        principal.Claims.FirstOrDefault(claim => string.Equals(claim.Type, type, StringComparison.OrdinalIgnoreCase))?.Value;
    private static IEnumerable<string> Values(ClaimsPrincipal principal, IEnumerable<string> types)
    {
        HashSet<string> allowed = types.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return principal.Claims.Where(claim => allowed.Contains(claim.Type)).Select(static claim => claim.Value);
    }
}
