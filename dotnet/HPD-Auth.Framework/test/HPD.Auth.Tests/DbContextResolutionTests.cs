using FluentAssertions;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies HPDAuthDbContext resolvability and lifetime (tests 6.1 – 6.2).
/// </summary>
public class DbContextResolutionTests
{
    // ── 6.1 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HPDAuthDbContext_Is_Resolvable_And_Functional()
    {
        var sp = ServiceProviderBuilder.Build(appName: "DbCtx_Functional");
        using var scope = sp.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<HPDAuthDbContext>();

        // EnsureCreated is a no-op for in-memory; CanConnect always returns true.
        var canConnect = await db.Database.CanConnectAsync();

        canConnect.Should().BeTrue();
    }

    // ── 6.2 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void HPDAuthDbContext_Is_Scoped_Not_Singleton()
    {
        var sp = ServiceProviderBuilder.Build(appName: "DbCtx_Scoped");

        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();

        var db1 = scope1.ServiceProvider.GetRequiredService<HPDAuthDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<HPDAuthDbContext>();

        db1.Should().NotBeSameAs(db2);
    }
}
