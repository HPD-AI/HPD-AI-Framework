using System.Net.Http.Json;
using HPD.Auth.Core.Entities;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace HPD.Auth.Tests.Endpoints;

public class DiagnosticTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Debug_2FA()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Diag_2FA");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "twofactor@example.com", password = "Password1!" });

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("twofactor@example.com");
        await userManager.ResetAuthenticatorKeyAsync(user!);
        await userManager.SetTwoFactorEnabledAsync(user!, true);

        // Check validity
        var providers = await userManager.GetValidTwoFactorProvidersAsync(user!);
        output.WriteLine($"Valid providers: {string.Join(", ", providers)}");

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "twofactor@example.com", password = "Password1!" });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"TOKEN {(int)response.StatusCode}: {body}");
    }
}
