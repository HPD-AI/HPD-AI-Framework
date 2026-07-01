using FluentAssertions;
using HPD.Auth.Extensions;
using HPD.Auth.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
    public async Task DataProtection_ApplicationName_Matches_Options_AppName()
    {
        const string expectedAppName = "DataProtectionTestApp";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services
            .AddHPDAuth(o => o.AppName = expectedAppName)
            .UseInMemorySqliteForTests();
        var sp = services.BuildServiceProvider();
        await sp.InitializeHPDAuthDevelopmentDatabaseAsync();

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
        services
            .AddHPDAuth(o => o.AppName = "KeyPersistenceTest")
            .UseInMemorySqliteForTests();
        var sp = services.BuildServiceProvider();

        await sp.InitializeHPDAuthDevelopmentDatabaseAsync();

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

    [Fact]
    public async Task DataProtection_Keys_Survive_FileBackedSqlite_ServiceProviderRestart()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hpd-auth-dp-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        try
        {
            await using (var firstProvider = BuildDurableSqliteProvider("DurableKeyPersistenceTest", connectionString))
            {
                await firstProvider.InitializeHPDAuthDevelopmentDatabaseAsync();

                var protector = firstProvider
                    .GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("durable-persist-test");
                var ciphertext = protector.Protect("restart-sensitive-value");

                await using (var scope = firstProvider.CreateAsyncScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<HPDAuthDbContext>();
                    var keyCount = await dbContext.DataProtectionKeys.CountAsync();
                    keyCount.Should().BeGreaterThan(0);
                }

                await using var secondProvider = BuildDurableSqliteProvider("DurableKeyPersistenceTest", connectionString);
                var restartedProtector = secondProvider
                    .GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("durable-persist-test");

                restartedProtector.Unprotect(ciphertext).Should().Be("restart-sensitive-value");
            }
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static ServiceProvider BuildDurableSqliteProvider(string appName, string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services
            .AddHPDAuth(o => o.AppName = appName)
            .UseSqlite(connectionString);

        return services.BuildServiceProvider();
    }
}
