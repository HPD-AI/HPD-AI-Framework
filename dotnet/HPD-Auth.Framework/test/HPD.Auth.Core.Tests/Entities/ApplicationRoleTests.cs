using FluentAssertions;
using HPD.Auth.Core.Entities;
using Xunit;

namespace HPD.Auth.Core.Tests.Entities;

[Trait("Category", "Entity")]
public class ApplicationRoleTests
{
    [Fact]
    public void ApplicationRole_InstanceId_DefaultsToGuidEmpty()
    {
        new ApplicationRole().InstanceId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void ApplicationRole_Description_DefaultsToNull()
    {
        new ApplicationRole().Description.Should().BeNull();
    }

    [Fact]
    public void ApplicationRole_Created_IsSetToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var role = new ApplicationRole();
        var after = DateTime.UtcNow.AddSeconds(5);

        role.Created.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void ApplicationRole_Parameterized_Constructor_SetsName()
    {
        new ApplicationRole("Admin").Name.Should().Be("Admin");
    }
}
