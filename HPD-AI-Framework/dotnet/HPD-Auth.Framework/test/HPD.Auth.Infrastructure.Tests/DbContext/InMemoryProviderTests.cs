using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HPD.Auth.Infrastructure.Tests.DbContext;

/// <summary>
/// Section 5: In-Memory Provider — Basic Behavior
/// </summary>
public class InMemoryProviderTests
{
    // ── 5.1 DbContext can be created with the in-memory provider ─────────────

    [Fact]
    public void CreateIsolated_ReturnsNonNullContext()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        ctx.Should().NotBeNull();
    }

    // ── 5.2 All DbSets are accessible without migrations ─────────────────────

    [Fact]
    public async Task AllDbSets_Queryable_NoExceptionOnEmptyQuery()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();

        var users = await ctx.Users.ToListAsync();
        var roles = await ctx.Roles.ToListAsync();
        var refreshTokens = await ctx.RefreshTokens.ToListAsync();
        var sessions = await ctx.UserSessions.ToListAsync();
        var identities = await ctx.UserIdentities.ToListAsync();
        var passkeys = await ctx.UserPasskeys.ToListAsync();
        var auditLogs = await ctx.AuditLogs.ToListAsync();
        var ssoProviders = await ctx.SSOProviders.ToListAsync();
        var tenantSettings = await ctx.TenantSettings.ToListAsync();
        var dpKeys = await ctx.DataProtectionKeys.ToListAsync();

        users.Should().BeEmpty();
        roles.Should().BeEmpty();
        refreshTokens.Should().BeEmpty();
        sessions.Should().BeEmpty();
        identities.Should().BeEmpty();
        passkeys.Should().BeEmpty();
        auditLogs.Should().BeEmpty();
        ssoProviders.Should().BeEmpty();
        tenantSettings.Should().BeEmpty();
        dpKeys.Should().BeEmpty();
    }

    // ── 5.3 CreateIsolated produces independent databases ────────────────────

    [Fact]
    public async Task CreateIsolated_TwoCalls_ProduceIndependentDatabases()
    {
        await using var ctx1 = HPDAuthDbContextFactory.CreateIsolated();
        await using var ctx2 = HPDAuthDbContextFactory.CreateIsolated();

        var user = UserFactory.CreateUser();
        ctx1.Users.Add(user);
        await ctx1.SaveChangesAsync();

        var count = await ctx2.Users.IgnoreQueryFilters().CountAsync();
        count.Should().Be(0);
    }

    // ── 5.4 CreateInMemory with the same name shares state ───────────────────

    [Fact]
    public async Task CreateInMemory_SameName_SharesState()
    {
        var dbName = Guid.NewGuid().ToString();

        await using var db1 = HPDAuthDbContextFactory.CreateInMemory(dbName);
        await using var db2 = HPDAuthDbContextFactory.CreateInMemory(dbName);

        var user = UserFactory.CreateUser(Guid.Empty);
        db1.Users.Add(user);
        await db1.SaveChangesAsync();

        var result = await db2.Users.IgnoreQueryFilters().ToListAsync();
        result.Should().HaveCount(1);
        result[0].Id.Should().Be(user.Id);
    }
}
