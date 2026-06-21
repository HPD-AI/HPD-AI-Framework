using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class HPDCookieOptionsTests
{
    [Fact]
    public void HPDCookieOptions_HttpOnly_DefaultsToTrue()
    {
        new HPDCookieOptions().HttpOnly.Should().BeTrue();
    }

    [Fact]
    public void HPDCookieOptions_SecurePolicy_DefaultsToTrue()
    {
        new HPDCookieOptions().SecurePolicy.Should().BeTrue();
    }

    [Fact]
    public void HPDCookieOptions_SlidingExpiration_DefaultsTo14Days()
    {
        new HPDCookieOptions().SlidingExpiration.Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public void HPDCookieOptions_CookieName_DefaultsToHPDAuth()
    {
        new HPDCookieOptions().CookieName.Should().Be(".HPD.Auth");
    }

    [Fact]
    public void HPDCookieOptions_SameSite_DefaultsToLax()
    {
        new HPDCookieOptions().SameSite.Should().Be(Microsoft.AspNetCore.Http.SameSiteMode.Lax);
    }

    [Fact]
    public void HPDCookieOptions_AbsoluteExpiration_DefaultsTo30Days()
    {
        new HPDCookieOptions().AbsoluteExpiration.Should().Be(TimeSpan.FromDays(30));
    }

    [Fact]
    public void HPDCookieOptions_UseSlidingExpiration_DefaultsToTrue()
    {
        new HPDCookieOptions().UseSlidingExpiration.Should().BeTrue();
    }

    [Fact]
    public void HPDCookieOptions_Domain_DefaultsToNull()
    {
        new HPDCookieOptions().Domain.Should().BeNull();
    }

    [Fact]
    public void HPDCookieOptions_Path_DefaultsToRoot()
    {
        new HPDCookieOptions().Path.Should().Be("/");
    }
}
