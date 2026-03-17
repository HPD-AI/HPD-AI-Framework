using FluentAssertions;
using HPD.Auth.Core.Interfaces;
using Xunit;

namespace HPD.Auth.Core.Tests.Interfaces;

[Trait("Category", "Interface")]
public class SessionContextTests
{
    [Fact]
    public void SessionContext_AAL_DefaultsToAal1()
    {
        new SessionContext(null, null).AAL.Should().Be("aal1");
    }

    [Fact]
    public void SessionContext_Lifetime_DefaultsToNull()
    {
        new SessionContext(null, null).Lifetime.Should().BeNull();
    }
}
