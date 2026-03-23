using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Extensions;
using HPD.Auth.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies that the in-memory database is scoped by AppName, so two registrations
/// with different AppNames get isolated stores that do not share data.
/// </summary>
public class DbIsolationTests
{
    [Fact]
    public async Task Two_AppNames_Get_Isolated_InMemory_Databases()
    {
        // Build two independent providers with different AppNames.
        var servicesA = new ServiceCollection();
        servicesA.AddLogging();
        servicesA.AddHttpContextAccessor();
        servicesA.AddHPDAuth(o => o.AppName = "IsolationTestApp_A");
        var spA = servicesA.BuildServiceProvider();

        var servicesB = new ServiceCollection();
        servicesB.AddLogging();
        servicesB.AddHttpContextAccessor();
        servicesB.AddHPDAuth(o => o.AppName = "IsolationTestApp_B");
        var spB = servicesB.BuildServiceProvider();

        // Write a user into provider A's database.
        using var scopeA = spA.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<HPDAuthDbContext>();
        await dbA.Database.EnsureCreatedAsync();
        dbA.Users.Add(new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "alice",
            NormalizedUserName = "ALICE",
            Email = "alice@example.com",
            NormalizedEmail = "ALICE@EXAMPLE.COM",
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await dbA.SaveChangesAsync();

        // Provider B's database must be empty — it uses a different in-memory DB.
        using var scopeB = spB.CreateScope();
        var dbB = scopeB.ServiceProvider.GetRequiredService<HPDAuthDbContext>();
        await dbB.Database.EnsureCreatedAsync();

        dbB.Users.Should().BeEmpty();
    }
}
