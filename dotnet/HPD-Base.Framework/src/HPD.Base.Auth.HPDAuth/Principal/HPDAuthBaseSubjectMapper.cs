using System.Security.Claims;
using HPD.Base.Auth.HPDAuth.Configuration;
using HPD.Base.Policy;
using HPD.Base.Runtime;
using Microsoft.Extensions.Options;

namespace HPD.Base.Auth.HPDAuth;

/// <summary>
/// Maps HPD.Auth-compatible claims into BASE principal and subject contracts.
/// </summary>
public sealed class HPDAuthBaseSubjectMapper
{
    private readonly HPDBaseHPDAuthOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDAuthBaseSubjectMapper"/> class.
    /// </summary>
    /// <param name="options">The adapter options.</param>
    public HPDAuthBaseSubjectMapper(IOptions<HPDBaseHPDAuthOptions> options)
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
                        Source = HPDAuthBaseSources.Auth
                    }
                ],
                AuthSource = HPDAuthBaseSources.Auth
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
                Source = HPDAuthBaseSources.Auth
            }
        };

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            subjects.Add(new AccessSubject
            {
                Kind = AccessSubjectKind.User,
                Id = subjectId,
                TenantId = tenantId,
                Source = HPDAuthBaseSources.Auth
            });
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            subjects.Add(new AccessSubject
            {
                Kind = AccessSubjectKind.Tenant,
                Id = tenantId,
                TenantId = tenantId,
                Source = HPDAuthBaseSources.Auth
            });
        }

        if (!string.IsNullOrWhiteSpace(servicePrincipalId))
        {
            subjects.Add(new AccessSubject
            {
                Kind = AccessSubjectKind.ServicePrincipal,
                Id = servicePrincipalId,
                TenantId = tenantId,
                Source = HPDAuthBaseSources.Auth
            });
        }

        if (isAdmin)
        {
            subjects.Add(new AccessSubject
            {
                Kind = AccessSubjectKind.Admin,
                Id = subjectId ?? displayName ?? "admin",
                TenantId = tenantId,
                Source = HPDAuthBaseSources.Auth
            });
        }

        subjects.AddRange(roles.Select(role => new AccessSubject
        {
            Kind = AccessSubjectKind.Role,
            Id = role,
            TenantId = tenantId,
            Source = HPDAuthBaseSources.Auth
        }));

        var claims = principal.Claims
            .Where(claim => !IsSensitive(claim.Type, _options.SensitiveClaimTypeFragments))
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
                        Source = HPDAuthBaseSources.Auth
                    }
                ],
            CurrentTenantId = tenantId,
            SessionId = ClaimValue(principal, _options.SessionIdClaimType),
            CredentialId = ClaimValue(principal, _options.CredentialIdClaimType),
            AuthSource = HPDAuthBaseSources.Auth
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

    private static bool IsSensitive(string claimType, IEnumerable<string> fragments) =>
        fragments.Any(fragment => claimType.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>
/// Names source identifiers emitted by the HPD.Auth adapter.
/// </summary>
public static class HPDAuthBaseSources
{
    /// <summary>
    /// Source id used for HPD.Auth adapter mapped facts.
    /// </summary>
    public const string Auth = "hpd.auth";
}
