using System.Security.Claims;
using HPD.Base.AspNetCore.Configuration;
using HPD.Base.AspNetCore.EndpointMapping;
using HPD.Base.Policy;
using HPD.Base.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore.Http;

internal sealed class BaseHttpPrincipalContextFactory : IBaseHttpPrincipalContextFactory
{
    private readonly IEnumerable<IBaseHttpPrincipalMapper> _mappers;
    private readonly HPDBaseAspNetCoreOptions _options;

    public BaseHttpPrincipalContextFactory(
        IEnumerable<IBaseHttpPrincipalMapper> mappers,
        IOptions<HPDBaseAspNetCoreOptions> options)
    {
        _mappers = mappers;
        _options = options.Value;
    }

    public async ValueTask<PrincipalContext> CreateAsync(
        HttpContext httpContext,
        HPDBaseEndpointKind endpointKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        foreach (var mapper in _mappers)
        {
            var mapped = await mapper.TryMapAsync(httpContext, cancellationToken).ConfigureAwait(false);
            if (mapped is not null)
                return mapped;
        }

        var user = httpContext.User;
        var identity = user.Identity;
        if (identity?.IsAuthenticated != true)
        {
            return new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Anonymous,
                SubjectKind = AccessSubjectKind.Anonymous,
                AuthSource = "aspnet"
            };
        }

        var auth = _options.Auth;
        var subjectId = FirstClaimValue(user, auth.SubjectIdClaimTypes);
        var displayName = FirstClaimValue(user, auth.DisplayNameClaimTypes) ?? identity.Name;
        var roles = ClaimsByTypes(user, auth.RoleClaimTypes)
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(0, auth.MaxRoles))
            .ToArray();
        var isAdmin = roles.Any(role => auth.AdminRoleNames.Contains(role, StringComparer.Ordinal));
        var servicePrincipalId = FirstClaimValue(user, auth.ServicePrincipalClaimTypes);
        var claims = user.Claims
            .Where(claim => !IsSensitive(claim.Type, auth.SensitiveClaimTypeFragments))
            .Take(Math.Max(0, auth.MaxClaims))
            .Select(static claim => new ClaimValue
            {
                Type = claim.Type,
                Value = claim.Value,
                Issuer = claim.Issuer,
                ValueType = claim.ValueType
            })
            .ToArray();

        var subjects = new List<AccessSubject>();
        if (!string.IsNullOrWhiteSpace(subjectId))
            subjects.Add(new AccessSubject { Kind = AccessSubjectKind.User, Id = subjectId, Source = "aspnet" });
        if (!string.IsNullOrWhiteSpace(servicePrincipalId))
            subjects.Add(new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = servicePrincipalId, Source = "aspnet" });
        if (isAdmin)
            subjects.Add(new AccessSubject { Kind = AccessSubjectKind.Admin, Id = subjectId ?? displayName ?? "admin", Source = "aspnet" });

        subjects.AddRange(roles.Select(static role => new AccessSubject
        {
            Kind = AccessSubjectKind.Role,
            Id = role,
            Source = "aspnet"
        }));

        var currentTenantId = ClaimValue(user, auth.TenantIdClaimType);
        var tenantMemberships = TenantMemberships(user, auth, currentTenantId);

        return new PrincipalContext
        {
            AuthenticationState = isAdmin
                ? PrincipalAuthenticationState.Admin
                : string.IsNullOrWhiteSpace(servicePrincipalId)
                    ? PrincipalAuthenticationState.Authenticated
                    : PrincipalAuthenticationState.Service,
            SubjectKind = isAdmin
                ? AccessSubjectKind.Admin
                : string.IsNullOrWhiteSpace(servicePrincipalId)
                    ? string.IsNullOrWhiteSpace(subjectId) ? AccessSubjectKind.Authenticated : AccessSubjectKind.User
                    : AccessSubjectKind.ServicePrincipal,
            SubjectId = subjectId,
            DisplayName = Limit(displayName, 256),
            Roles = roles.Length == 0 ? null : roles,
            Claims = claims.Length == 0 ? null : claims,
            Subjects = subjects.Count == 0 ? null : subjects.ToArray(),
            TenantMemberships = tenantMemberships.Length == 0 ? null : tenantMemberships,
            CurrentTenantId = currentTenantId,
            SessionId = ClaimValue(user, auth.SessionIdClaimType),
            AuthSource = "aspnet"
        };
    }

    private static string? FirstClaimValue(ClaimsPrincipal user, IEnumerable<string> claimTypes) =>
        claimTypes.Select(type => ClaimValue(user, type)).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static string? ClaimValue(ClaimsPrincipal user, string? claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
            return null;

        return user.Claims.FirstOrDefault(claim => string.Equals(claim.Type, claimType, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static IEnumerable<string> ClaimsByTypes(ClaimsPrincipal user, IEnumerable<string> claimTypes)
    {
        var allowed = claimTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return user.Claims.Where(claim => allowed.Contains(claim.Type)).Select(static claim => claim.Value);
    }

    private static TenantMembership[] TenantMemberships(ClaimsPrincipal user, HPDBaseHttpAuthOptions auth, string? currentTenantId)
    {
        var tenants = new List<string>();
        if (!string.IsNullOrWhiteSpace(currentTenantId))
            tenants.Add(currentTenantId);

        if (!string.IsNullOrWhiteSpace(auth.TenantMembershipClaimType))
        {
            tenants.AddRange(user.Claims
                .Where(claim => string.Equals(claim.Type, auth.TenantMembershipClaimType, StringComparison.OrdinalIgnoreCase))
                .SelectMany(static claim => claim.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        }

        return tenants
            .Distinct(StringComparer.Ordinal)
            .Select(static tenantId => new TenantMembership { TenantId = tenantId, Source = "aspnet" })
            .ToArray();
    }

    private static bool IsSensitive(string claimType, IEnumerable<string> fragments) =>
        fragments.Any(fragment => claimType.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
