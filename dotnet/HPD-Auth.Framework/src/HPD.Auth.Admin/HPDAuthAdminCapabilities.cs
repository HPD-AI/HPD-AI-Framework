namespace HPD.Auth.Admin;

/// <summary>The complete L1 capability vocabulary owned by HPD.Auth Admin.</summary>
public static class HPDAuthAdminCapabilities
{
    public const string IdentityRead = "auth.identity.read";
    public const string IdentityCreate = "auth.identity.create";
    public const string IdentityWrite = "auth.identity.write";
    public const string IdentityDelete = "auth.identity.delete";
    public const string AuthorizationRead = "auth.authorization.read";
    public const string AuthorizationWrite = "auth.authorization.write";
    public const string CredentialsRead = "auth.credentials.read";
    public const string CredentialsWrite = "auth.credentials.write";
    public const string CredentialsIssue = "auth.credentials.issue";
    public const string SessionsRead = "auth.sessions.read";
    public const string SessionsWrite = "auth.sessions.write";
    public const string AuditRead = "auth.audit.read";
}
