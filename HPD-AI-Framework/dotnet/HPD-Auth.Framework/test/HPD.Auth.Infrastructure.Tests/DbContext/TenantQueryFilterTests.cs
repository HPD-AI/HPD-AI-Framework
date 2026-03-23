using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HPD.Auth.Infrastructure.Tests.DbContext;

/// <summary>
/// Section 1: DbContext — Tenant Query Filters
/// </summary>
public class TenantQueryFilterTests
{
    // ── 1.1 Query filter returns only current tenant's entities ──────────────

    [Fact]
    public async Task Users_QueryFilter_ReturnsOnlyCurrentTenantUsers()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var userA = UserFactory.CreateUser(tenantA.InstanceId);
        var userB = UserFactory.CreateUser(tenantB.InstanceId);

        // Insert both users using IgnoreQueryFilters so we can set up cross-tenant data
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setup.Users.Add(userA);
            await setup.SaveChangesAsync();
        }
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setup.Users.Add(userB);
            await setup.SaveChangesAsync();
        }

        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var result = await ctxA.Users.ToListAsync();

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(userA.Id);
    }

    [Fact]
    public async Task Roles_QueryFilter_ReturnsOnlyCurrentTenantRoles()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var roleA = new ApplicationRole("AdminA") { Id = Guid.NewGuid(), InstanceId = tenantA.InstanceId, NormalizedName = "ADMINA" };
        var roleB = new ApplicationRole("AdminB") { Id = Guid.NewGuid(), InstanceId = tenantB.InstanceId, NormalizedName = "ADMINB" };

        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setup.Roles.Add(roleA);
            await setup.SaveChangesAsync();
        }
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setup.Roles.Add(roleB);
            await setup.SaveChangesAsync();
        }

        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var result = await ctxA.Roles.ToListAsync();

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(roleA.Id);
    }

    [Fact]
    public async Task RefreshTokens_QueryFilter_ReturnsOnlyCurrentTenantTokens()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var userA = UserFactory.CreateUser(tenantA.InstanceId);
        var userB = UserFactory.CreateUser(tenantB.InstanceId);
        var tokenA = UserFactory.CreateRefreshToken(userA.Id, tenantA.InstanceId);
        var tokenB = UserFactory.CreateRefreshToken(userB.Id, tenantB.InstanceId);

        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setup.Users.Add(userA);
            setup.RefreshTokens.Add(tokenA);
            await setup.SaveChangesAsync();
        }
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setup.Users.Add(userB);
            setup.RefreshTokens.Add(tokenB);
            await setup.SaveChangesAsync();
        }

        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var result = await ctxA.RefreshTokens.ToListAsync();

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(tokenA.Id);
    }

    [Fact]
    public async Task UserSessions_QueryFilter_ReturnsOnlyCurrentTenantSessions()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var userA = UserFactory.CreateUser(tenantA.InstanceId);
        var userB = UserFactory.CreateUser(tenantB.InstanceId);
        var sessionA = new UserSession { Id = Guid.NewGuid(), InstanceId = tenantA.InstanceId, UserId = userA.Id, ExpiresAt = DateTime.UtcNow.AddDays(1) };
        var sessionB = new UserSession { Id = Guid.NewGuid(), InstanceId = tenantB.InstanceId, UserId = userB.Id, ExpiresAt = DateTime.UtcNow.AddDays(1) };

        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setup.Users.Add(userA);
            setup.UserSessions.Add(sessionA);
            await setup.SaveChangesAsync();
        }
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setup.Users.Add(userB);
            setup.UserSessions.Add(sessionB);
            await setup.SaveChangesAsync();
        }

        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var result = await ctxA.UserSessions.ToListAsync();

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(sessionA.Id);
    }

    [Fact]
    public async Task AuditLogs_QueryFilter_ReturnsOnlyCurrentTenantLogs()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var logA = new AuditLog { Id = Guid.NewGuid(), InstanceId = tenantA.InstanceId, Action = "a", Category = "c" };
        var logB = new AuditLog { Id = Guid.NewGuid(), InstanceId = tenantB.InstanceId, Action = "a", Category = "c" };

        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setup.AuditLogs.Add(logA);
            await setup.SaveChangesAsync();
        }
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setup.AuditLogs.Add(logB);
            await setup.SaveChangesAsync();
        }

        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var result = await ctxA.AuditLogs.ToListAsync();

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(logA.Id);
    }

    [Fact]
    public async Task TenantSettings_QueryFilter_ReturnsOnlyCurrentTenantSettings()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var settingsA = new TenantSettings { InstanceId = tenantA.InstanceId, DisplayName = "A" };
        var settingsB = new TenantSettings { InstanceId = tenantB.InstanceId, DisplayName = "B" };

        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setup.TenantSettings.Add(settingsA);
            await setup.SaveChangesAsync();
        }
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setup.TenantSettings.Add(settingsB);
            await setup.SaveChangesAsync();
        }

        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var result = await ctxA.TenantSettings.ToListAsync();

        result.Should().HaveCount(1);
        result[0].InstanceId.Should().Be(tenantA.InstanceId);
    }

    // ── 1.2 IgnoreQueryFilters bypasses tenant isolation ─────────────────────

    [Fact]
    public async Task Users_IgnoreQueryFilters_ReturnsBothTenants()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var userA = UserFactory.CreateUser(tenantA.InstanceId);
        var userB = UserFactory.CreateUser(tenantB.InstanceId);

        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setup.Users.Add(userA);
            await setup.SaveChangesAsync();
        }
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setup.Users.Add(userB);
            await setup.SaveChangesAsync();
        }

        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var result = await ctxA.Users.IgnoreQueryFilters().ToListAsync();

        result.Should().HaveCount(2);
        result.Select(u => u.Id).Should().Contain(userA.Id).And.Contain(userB.Id);
    }

    // ── 1.3 UserPasskey is NOT filtered by InstanceId ────────────────────────

    [Fact]
    public async Task UserPasskeys_NoQueryFilter_ReturnsPasskeysFromAnyTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var userA = UserFactory.CreateUser(tenantA.InstanceId);
        var userB = UserFactory.CreateUser(tenantB.InstanceId);
        var passkeyA = new UserPasskey { Id = Guid.NewGuid(), InstanceId = tenantA.InstanceId, UserId = userA.Id, CredentialId = "cred-a", PublicKey = "pk-a" };
        var passkeyB = new UserPasskey { Id = Guid.NewGuid(), InstanceId = tenantB.InstanceId, UserId = userB.Id, CredentialId = "cred-b", PublicKey = "pk-b" };

        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setup.Users.Add(userA);
            setup.UserPasskeys.Add(passkeyA);
            await setup.SaveChangesAsync();
        }
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setup.Users.Add(userB);
            setup.UserPasskeys.Add(passkeyB);
            await setup.SaveChangesAsync();
        }

        // Context is for tenantA but should return passkeys from both tenants
        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var result = await ctxA.UserPasskeys.ToListAsync();

        result.Should().HaveCount(2);
    }

    // ── 1.4 Single-tenant (Guid.Empty) context returns rows with InstanceId == Guid.Empty

    [Fact]
    public async Task Users_SingleTenantContext_ReturnsUsersWithEmptyInstanceId()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated(); // SingleTenantContext (Guid.Empty)

        var user = UserFactory.CreateUser(Guid.Empty);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var result = await ctx.Users.ToListAsync();

        result.Should().HaveCount(1);
        result[0].InstanceId.Should().Be(Guid.Empty);
    }
}
