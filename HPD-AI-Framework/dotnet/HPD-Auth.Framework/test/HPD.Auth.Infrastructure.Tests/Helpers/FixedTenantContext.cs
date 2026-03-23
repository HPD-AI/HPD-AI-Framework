using HPD.Auth.Core.Interfaces;

namespace HPD.Auth.Infrastructure.Tests.Helpers;

/// <summary>
/// Test implementation of ITenantContext with a fixed InstanceId.
/// </summary>
public class FixedTenantContext : ITenantContext
{
    public FixedTenantContext(Guid instanceId) => InstanceId = instanceId;
    public Guid InstanceId { get; }
}
