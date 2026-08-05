using FluentAssertions;
using HPD.Auth.Admin.Tests.Helpers;
using Xunit;

namespace HPD.Auth.Admin.Tests;

public sealed class AdminStartupValidationTests
{
    [Fact]
    public async Task HPD_Auth_host_rejects_missing_control_plane_scheme()
    {
        await using var factory = new AdminWebFactory(registerAuthenticationScheme: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.StartAsync());

        exception.Message.Should().Be("hpd.auth.controlPlane.scheme.missing");
    }
}
