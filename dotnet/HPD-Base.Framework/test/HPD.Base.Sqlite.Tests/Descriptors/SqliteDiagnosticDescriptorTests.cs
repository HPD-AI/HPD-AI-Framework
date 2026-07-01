using FluentAssertions;
using HPD.Base.Runtime.Health;
using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Sqlite.Tests.Descriptors;

public sealed class SqliteDiagnosticDescriptorTests
{
    [Fact]
    public async Task AdminDiagnosticsIncludeNativeSqliteVersionAndPragmas()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-diag-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddHPDBaseSqliteStore(options =>
            {
                options.DataSource = path;
                options.BusyTimeout = TimeSpan.FromMilliseconds(250);
            });
            await using var provider = services.BuildServiceProvider();
            var diagnostics = await provider.GetRequiredService<IEnumerable<IBaseDiagnosticContributor>>().Single().GetDiagnosticsAsync();

            var configuration = diagnostics.Single(diagnostic => diagnostic.Code == "base.sqlite.configuration");
            configuration.Message.Should().Contain("SQLite version");
            configuration.Message.Should().Contain("journal mode");
            configuration.Message.Should().Contain("foreign_keys");
            configuration.Message.Should().Contain("busy_timeout");
            configuration.Message.Should().Contain("synchronous");
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }
}
