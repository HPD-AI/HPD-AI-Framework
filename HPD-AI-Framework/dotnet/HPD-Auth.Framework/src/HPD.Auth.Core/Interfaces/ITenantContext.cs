namespace HPD.Auth.Core.Interfaces;

/// <summary>
/// Resolves the current tenant's InstanceId for multi-tenancy isolation.
/// In single-tenant apps, the default implementation always returns Guid.Empty.
/// In multi-tenant SaaS, implementations resolve the InstanceId from
/// the JWT claim, HTTP header, subdomain, or route parameter.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The InstanceId for the current request's tenant.
    /// Returns Guid.Empty in single-tenant mode.
    /// </summary>
    Guid InstanceId { get; }
}

/// <summary>
/// Default ITenantContext implementation for single-tenant applications.
/// Always returns Guid.Empty — no configuration required.
/// Registered as the default when AddHPDAuth() is called without multi-tenancy.
/// </summary>
public class SingleTenantContext : ITenantContext
{
    /// <inheritdoc />
    public Guid InstanceId => Guid.Empty;
}
