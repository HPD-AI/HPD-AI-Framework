using HPD.Base;

namespace HPD.Base.Testing;

/// <summary>Creates explicit bounded principals for application tests.</summary>
public static class BaseTestPrincipal
{
    /// <summary>Executes the system operation.</summary>
    public static PrincipalContext System(string subjectId, string? tenantId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        return new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = subjectId,
            CurrentTenantId = tenantId,
        };
    }

    /// <summary>Executes the user operation.</summary>
    public static PrincipalContext User(string subjectId, string? tenantId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        return new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Authenticated,
            SubjectId = subjectId,
            CurrentTenantId = tenantId,
        };
    }

    /// <summary>Executes the anonymous operation.</summary>
    public static PrincipalContext Anonymous() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Anonymous,
    };
}
