using FluentAssertions;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies ITenantContext default behaviour (test 4).
/// </summary>
public class TenantContextTests
{
    [Fact]
    public void SingleTenantContext_Returns_Guid_Empty()
    {
        var sp = ServiceProviderBuilder.Build(appName: "TenantCtx_GuidEmpty");
        using var scope = sp.CreateScope();

        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        tenantContext.InstanceId.Should().Be(Guid.Empty);
    }
}
