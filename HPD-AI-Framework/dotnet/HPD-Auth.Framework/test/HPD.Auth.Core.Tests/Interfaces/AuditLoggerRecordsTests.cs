using FluentAssertions;
using HPD.Auth.Core.Interfaces;
using Xunit;

namespace HPD.Auth.Core.Tests.Interfaces;

[Trait("Category", "Interface")]
public class AuditLoggerRecordsTests
{
    [Fact]
    public void AuditLogEntry_Success_DefaultsToTrue()
    {
        new AuditLogEntry("user.login", "authentication").Success.Should().BeTrue();
    }

    [Fact]
    public void AuditLogEntry_UserId_DefaultsToNull()
    {
        new AuditLogEntry("user.login", "authentication").UserId.Should().BeNull();
    }

    [Fact]
    public void AuditLogQuery_Page_DefaultsToOne()
    {
        new AuditLogQuery().Page.Should().Be(1);
    }

    [Fact]
    public void AuditLogQuery_PageSize_DefaultsToFifty()
    {
        new AuditLogQuery().PageSize.Should().Be(50);
    }
}
