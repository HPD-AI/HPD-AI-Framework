using FluentAssertions;
using HPD.Auth.Extensions;
using HPD.Auth.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies the explicit HPD.Auth database migration path.
/// </summary>
public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task MigrateHPDAuthDatabaseAsync_Applies_Initial_Sqlite_Migration()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hpd-auth-migrate-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        try
        {
            await using var provider = BuildDurableSqliteProvider(connectionString);

            await provider.MigrateHPDAuthDatabaseAsync();

            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HPDAuthDbContext>();
            var migrations = await dbContext.Database.GetAppliedMigrationsAsync();
            migrations.Should().Contain(static migration => migration.EndsWith("_InitialSqlite", StringComparison.Ordinal));

            var canConnect = await dbContext.Database.CanConnectAsync();
            canConnect.Should().BeTrue();
            var keyCount = await dbContext.DataProtectionKeys.CountAsync();
            keyCount.Should().Be(0);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task ValidateHPDAuthDatabaseMigratedAsync_Fails_When_Migrations_Are_Pending()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hpd-auth-pending-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        try
        {
            await using var provider = BuildDurableSqliteProvider(connectionString);

            var act = () => provider.ValidateHPDAuthDatabaseMigratedAsync();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*pending migrations*InitialSqlite*");
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task ValidateHPDAuthDatabaseMigratedAsync_Succeeds_After_Migration()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hpd-auth-current-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        try
        {
            await using var provider = BuildDurableSqliteProvider(connectionString);

            await provider.MigrateHPDAuthDatabaseAsync();
            await provider.ValidateHPDAuthDatabaseMigratedAsync();
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static ServiceProvider BuildDurableSqliteProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services
            .AddHPDAuth(o => o.AppName = "MigrationTest")
            .UseSqlite(connectionString);

        return services.BuildServiceProvider();
    }
}
