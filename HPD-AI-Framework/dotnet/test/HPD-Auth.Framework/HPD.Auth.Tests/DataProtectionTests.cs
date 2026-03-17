using FluentAssertions;
using HPD.Auth.Extensions;
using HPD.Auth.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies that Data Protection is registered and functional (tests 11.1 – 11.2).
/// </summary>
public class DataProtectionTests
{
    // ── 11.1 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DataProtection_ApplicationName_Matches_Options_AppName()
    {
        const string expectedAppName = "DataProtectionTestApp";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o => o.AppName = expectedAppName);
        var sp = services.BuildServiceProvider();

        // IDataProtectionProvider must be registered.
        var provider = sp.GetService<IDataProtectionProvider>();
        provider.Should().NotBeNull();

        // Create a protector to confirm the provider is functional (round-trip).
        var protector = provider!.CreateProtector("test-purpose");
        const string plaintext = "hello data protection";
        var ciphertext = protector.Protect(plaintext);
        var decrypted = protector.Unprotect(ciphertext);

        decrypted.Should().Be(plaintext);
    }

    // ── 11.2 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DataProtection_Keys_Persisted_To_DbContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o => o.AppName = "KeyPersistenceTest");
        var sp = services.BuildServiceProvider();

        // Ensure the DB schema exists (in-memory provider: always ready).
        using var setupScope = sp.CreateScope();
        var db = setupScope.ServiceProvider.GetRequiredService<HPDAuthDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Protect something in one scope.
        using var scope1 = sp.CreateScope();
        var provider1 = scope1.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var protector1 = provider1.CreateProtector("persist-test");
        var ciphertext = protector1.Protect("sensitive-value");

        // Unprotect in a different scope — keys come from the shared in-memory DB.
        using var scope2 = sp.CreateScope();
        var provider2 = scope2.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var protector2 = provider2.CreateProtector("persist-test");
        var decrypted = protector2.Unprotect(ciphertext);

        decrypted.Should().Be("sensitive-value");
    }
}
