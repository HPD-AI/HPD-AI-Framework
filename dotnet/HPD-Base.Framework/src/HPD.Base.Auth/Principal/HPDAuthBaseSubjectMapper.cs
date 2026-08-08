using System.Security.Claims;
using HPD.Base.Auth;
using HPD.Base;
using Microsoft.Extensions.Options;

namespace HPD.Base.Auth;

/// <summary>
/// Maps HPD.Auth-compatible claims into BASE principal and subject contracts.
/// </summary>
internal sealed class HPDBaseAuthSubjectProjector
{
    private readonly HPDBaseAuthOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDBaseAuthSubjectProjector"/> class.
    /// </summary>
    /// <param name="options">The adapter options.</param>
    public HPDBaseAuthSubjectProjector(IOptions<HPDBaseAuthOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Maps a claims principal into a BASE principal context.
    /// </summary>
    /// <param name="principal">The claims principal to map.</param>
    /// <param name="tenantIdFallback">A tenant id fallback from HPD.Auth host services.</param>
    /// <returns>The mapped BASE principal context.</returns>
    public PrincipalContext Map(ClaimsPrincipal principal, string? tenantIdFallback = null)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var identity = principal.Identity;
        if (identity?.IsAuthenticated != true)
        {
            return new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Anonymous,
                SubjectKind = AccessSubjectKind.Anonymous,
                Subjects =
                [
                    new AccessSubject
                    {
                        Kind = AccessSubjectKind.Anonymous,
                        Source = HPDBaseAuthSources.Auth
                    }
                ],
                AuthSource = HPDBaseAuthSources.Auth
            };
        }

        var subjectId = FirstClaimValue(principal, _options.SubjectIdClaimTypes);
        var displayName = FirstClaimValue(principal, _options.DisplayNameClaimTypes) ?? identity.Name;
        var roles = ClaimsByTypes(principal, _options.RoleClaimTypes)
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(0, _options.MaxRoles))
            .ToArray();
        var servicePrincipalId = FirstClaimValue(principal, _options.ServicePrincipalClaimTypes);
        var tenantId = ClaimValue(principal, _options.TenantClaimType) ?? tenantIdFallback;
        var isAdmin = roles.Any(role => _options.AdminRoleNames.Contains(role, StringComparer.Ordinal));

        var subjects = new List<AccessSubject>
        {
            new()
            {
                Kind = AccessSubjectKind.Authenticated,
                Source = HPDBaseAuthSources.Auth
            }
        };

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            subjects.Add(new AccessSubject
            {
                Kind = AccessSubjectKind.User,
                Id = subjectId,
                TenantId = tenantId,
                Source = HPDBaseAuthSources.Auth
            });
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            subjects.Add(new AccessSubject
            {
                Kind = AccessSubjectKind.Tenant,
                Id = tenantId,
                TenantId = tenantId,
                Source = HPDBaseAuthSources.Auth
            });
        }

        if (!string.IsNullOrWhiteSpace(servicePrincipalId))
        {
            subjects.Add(new AccessSubject
            {
                Kind = AccessSubjectKind.ServicePrincipal,
                Id = servicePrincipalId,
                TenantId = tenantId,
                Source = HPDBaseAuthSources.Auth
            });
        }

        if (isAdmin)
        {
            subjects.Add(new AccessSubject
            {
                Kind = AccessSubjectKind.Admin,
                Id = subjectId ?? displayName ?? "admin",
                TenantId = tenantId,
                Source = HPDBaseAuthSources.Auth
            });
        }

        subjects.AddRange(roles.Select(role => new AccessSubject
        {
            Kind = AccessSubjectKind.Role,
            Id = role,
            TenantId = tenantId,
            Source = HPDBaseAuthSources.Auth
        }));

        var claims = principal.Claims
            .Where(claim => _options.CopiedClaimTypes.Contains(claim.Type, StringComparer.Ordinal))
            .Take(Math.Max(0, _options.MaxClaims))
            .Select(static claim => new ClaimValue
            {
                Type = claim.Type,
                Value = claim.Value,
                Issuer = claim.Issuer,
                ValueType = claim.ValueType
            })
            .ToArray();

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
            Claims = claims.Length == 0 ? null : claims,
            Roles = roles.Length == 0 ? null : roles,
            Subjects = subjects.ToArray(),
            TenantMemberships = string.IsNullOrWhiteSpace(tenantId)
                ? null
                :
                [
                    new TenantMembership
                    {
                        TenantId = tenantId,
                        Roles = roles.Length == 0 ? null : roles,
                        Source = HPDBaseAuthSources.Auth
                    }
                ],
            CurrentTenantId = tenantId,
            SessionId = ClaimValue(principal, _options.SessionIdClaimType),
            CredentialId = ClaimValue(principal, _options.CredentialIdClaimType),
            AuthSource = HPDBaseAuthSources.Auth
        };
    }

    private static string? FirstClaimValue(ClaimsPrincipal principal, IEnumerable<string> claimTypes) =>
        claimTypes.Select(type => ClaimValue(principal, type)).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static string? ClaimValue(ClaimsPrincipal principal, string? claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
            return null;

        return principal.Claims.FirstOrDefault(claim =>
            string.Equals(claim.Type, claimType, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static IEnumerable<string> ClaimsByTypes(ClaimsPrincipal principal, IEnumerable<string> claimTypes)
    {
        var allowed = claimTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return principal.Claims.Where(claim => allowed.Contains(claim.Type)).Select(static claim => claim.Value);
    }


    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>
/// Names source identifiers emitted by the HPD.Auth adapter.
/// </summary>
public static class HPDBaseAuthSources
{
    /// <summary>
    /// Source id used for HPD.Auth adapter mapped facts.
    /// </summary>
    public const string Auth = "hpd.auth";
}
