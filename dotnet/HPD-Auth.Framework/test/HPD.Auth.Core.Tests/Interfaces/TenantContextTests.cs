using System.Reflection;
using FluentAssertions;
using HPD.Auth.Core.Interfaces;
using Xunit;

namespace HPD.Auth.Core.Tests.Interfaces;

[Trait("Category", "Interface")]
public class TenantContextTests
{
    [Fact]
    public void SingleTenantContext_InstanceId_AlwaysReturnsGuidEmpty()
    {
        var ctx = new SingleTenantContext();
        for (int i = 0; i < 10; i++)
        {
            ctx.InstanceId.Should().Be(Guid.Empty);
        }
    }

    [Fact]
    public void SingleTenantContext_ImplementsITenantContext()
    {
        new SingleTenantContext().Should().BeAssignableTo<ITenantContext>();
    }

    [Fact]
    public void SingleTenantContext_IsNotMutable()
    {
        var prop = typeof(SingleTenantContext).GetProperty(nameof(ITenantContext.InstanceId));
        prop.Should().NotBeNull();
        prop!.SetMethod.Should().BeNull(because: "InstanceId must be get-only with no setter");
    }
}
