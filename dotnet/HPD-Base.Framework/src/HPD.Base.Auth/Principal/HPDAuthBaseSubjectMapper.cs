using System.Security.Claims;
using HPD.Base.Auth;
using HPD.Base;
using HPD.Base.AspNetCore;

namespace HPD.Base.Auth;

/// <summary>
/// Maps HPD.Auth-compatible claims into BASE principal and subject contracts.
/// </summary>
internal sealed class HPDBaseAuthSubjectProjector
{
    private readonly HPDBaseAuthSnapshot _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDBaseAuthSubjectProjector"/> class.
    /// </summary>
    /// <param name="options">The adapter options.</param>
    public HPDBaseAuthSubjectProjector(HPDBaseAuthSnapshot options)
    {
        _options = options;
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

        var subjectId = BasePrincipalProjectionGuard.Single(principal, _options.SubjectIdClaimTypes, 256, "subject");
        var displayName = BasePrincipalProjectionGuard.Single(principal, _options.DisplayNameClaimTypes, 256, "display name");
        if (displayName is null && identity.Name is { } identityName)
            displayName = BasePrincipalProjectionGuard.Owned(identityName, 256, "display name");
        var roles = BasePrincipalProjectionGuard.Multiple(principal, _options.RoleClaimTypes, 128, _options.MaxRoles, "role");
        var servicePrincipalId = BasePrincipalProjectionGuard.Single(principal, _options.ServicePrincipalClaimTypes, 256, "service principal");
        var tenantId = BasePrincipalProjectionGuard.Single(principal, _options.TenantClaimType, 256, "tenant");
        if (tenantIdFallback is not null)
        {
            tenantIdFallback = BasePrincipalProjectionGuard.Owned(tenantIdFallback, 256, "tenant");
            if (tenantId is not null && !string.Equals(tenantId, tenantIdFallback, StringComparison.Ordinal))
                throw new InvalidOperationException("base.auth.actor.projectionFailed");
            tenantId ??= tenantIdFallback;
        }
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

        var claims = BasePrincipalProjectionGuard.CopiedClaims(principal, _options.CopiedClaimTypes, _options.MaxClaims);

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
            DisplayName = displayName,
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
            SessionId = BasePrincipalProjectionGuard.Single(principal, _options.SessionIdClaimType, 256, "session"),
            CredentialId = BasePrincipalProjectionGuard.Single(principal, _options.CredentialIdClaimType, 256, "credential"),
            AuthSource = HPDBaseAuthSources.Auth
        };
    }

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
