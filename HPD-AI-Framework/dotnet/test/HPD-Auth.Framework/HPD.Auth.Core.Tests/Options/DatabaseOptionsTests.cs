using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class DatabaseOptionsTests
{
    [Fact]
    public void DatabaseOptions_ConnectionString_DefaultsToEmpty()
    {
        new DatabaseOptions().ConnectionString.Should().Be(string.Empty);
    }

    [Fact]
    public void DatabaseOptions_Schema_DefaultsToPublic()
    {
        new DatabaseOptions().Schema.Should().Be("public");
    }

    [Fact]
    public void DatabaseOptions_AutoMigrate_DefaultsToFalse()
    {
        new DatabaseOptions().AutoMigrate.Should().BeFalse();
    }

    [Fact]
    public void DatabaseOptions_CommandTimeoutSeconds_DefaultsTo30()
    {
        new DatabaseOptions().CommandTimeoutSeconds.Should().Be(30);
    }
}
