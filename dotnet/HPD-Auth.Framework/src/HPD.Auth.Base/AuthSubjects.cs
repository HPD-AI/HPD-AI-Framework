using HPD.Base;

namespace HPD.Auth.Base;

/// <summary>Provides the public logical-subject contracts exported by HPD Auth.</summary>
public static class AuthSubjects
{
    /// <summary>Gets the principal-bound exported Auth user subject contract.</summary>
    /// <param name="session">The current principal-bound Base session.</param>
    /// <returns>The installed Auth user subject contract.</returns>
    public static BaseExportedSubjectContract<AuthUserSubject> Users(BaseSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return AuthUserSubject.Contract(session);
    }

    /// <summary>Gets the principal-bound exported Auth role subject contract.</summary>
    /// <param name="session">The current principal-bound Base session.</param>
    /// <returns>The installed Auth role subject contract.</returns>
    public static BaseExportedSubjectContract<AuthRoleSubject> Roles(BaseSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return AuthRoleSubject.Contract(session);
    }
}
