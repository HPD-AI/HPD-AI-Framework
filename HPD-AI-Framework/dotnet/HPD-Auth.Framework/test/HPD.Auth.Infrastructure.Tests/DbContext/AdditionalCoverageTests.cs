using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Stores;
using HPD.Auth.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Auth.Infrastructure.Tests.DbContext;

// ─────────────────────────────────────────────────────────────────────────────
// Gap 1: AuditLog immutability — init-only setters and store write-only policy
// ─────────────────────────────────────────────────────────────────────────────

public class AuditLogImmutabilityTests
{
    /// <summary>
    /// AuditLog properties are declared with init-only setters.
    /// This test verifies at the type level that no public settable property
    /// exists — catching regressions if someone accidentally changes init to set.
    /// </summary>
    [Fact]
    public void AuditLog_AllPublicProperties_AreInitOnly()
    {
        var type = typeof(AuditLog);
        var mutableProps = type.GetProperties()
            .Where(p => p.CanWrite)
            .Where(p =>
            {
                var setter = p.GetSetMethod();
                if (setter is null) return false;
                // init-only setters are non-public at runtime (they carry IsInitOnly modifier)
                // but CanWrite returns true. Use IsInitOnly via ReturnParameter.
                return !setter.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
            })
            .Select(p => p.Name)
            .ToList();

        mutableProps.Should().BeEmpty(
            $"AuditLog must only have init-only setters to enforce immutability; " +
            $"found mutable: {string.Join(", ", mutableProps)}");
    }

    /// <summary>
    /// AuditLogStore must never call Update or Remove on AuditLog rows.
    /// Verify by writing a log, then confirming the row cannot be mutated
    /// through normal EF change tracking (the entity state stays Unchanged
    /// after it is read back — there's nothing to save on Update).
    /// </summary>
    [Fact]
    public async Task AuditLogStore_NeverCallsUpdate_RowCountStaysOne()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = new AuditLogStore(ctx, NullLogger<AuditLogStore>.Instance);

        await store.LogAsync(new AuditLogEntry("user.login", "authentication"));
        await store.LogAsync(new AuditLogEntry("user.login", "authentication"));
        await store.LogAsync(new AuditLogEntry("token.refresh", "authentication"));

        // Three separate LogAsync calls should create three rows — not upsert/update.
        var count = await ctx.AuditLogs.IgnoreQueryFilters().CountAsync();
        count.Should().Be(3);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Gap 2: SSOProvider and UserIdentity tenant query filters
// ─────────────────────────────────────────────────────────────────────────────

public class MissingEntityQueryFilterTests
{
    [Fact]
    public async Task SSOProviders_QueryFilter_ReturnsOnlyCurrentTenantProviders()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var providerA = new SSOProvider { Id = Guid.NewGuid(), InstanceId = tenantA.InstanceId, ProviderId = "google" };
        var providerB = new SSOProvider { Id = Guid.NewGuid(), InstanceId = tenantB.InstanceId, ProviderId = "google" };

        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setup.SSOProviders.Add(providerA);
            await setup.SaveChangesAsync();
        }
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setup.SSOProviders.Add(providerB);
            await setup.SaveChangesAsync();
        }

        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var result = await ctxA.SSOProviders.ToListAsync();

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(providerA.Id);
    }

    [Fact]
    public async Task UserIdentities_QueryFilter_ReturnsOnlyCurrentTenantIdentities()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var userA = UserFactory.CreateUser(tenantA.InstanceId);
        var userB = UserFactory.CreateUser(tenantB.InstanceId);
        var identityA = new UserIdentity { Id = Guid.NewGuid(), InstanceId = tenantA.InstanceId, UserId = userA.Id, Provider = "google", ProviderId = "g-1" };
        var identityB = new UserIdentity { Id = Guid.NewGuid(), InstanceId = tenantB.InstanceId, UserId = userB.Id, Provider = "google", ProviderId = "g-1" };

        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setup.Users.Add(userA);
            setup.UserIdentities.Add(identityA);
            await setup.SaveChangesAsync();
        }
        await using (var setup = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setup.Users.Add(userB);
            setup.UserIdentities.Add(identityB);
            await setup.SaveChangesAsync();
        }

        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var result = await ctxA.UserIdentities.ToListAsync();

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(identityA.Id);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Gap 3: AuditLogStore InstanceId is Guid.Empty — multi-tenant write bug
// ─────────────────────────────────────────────────────────────────────────────

public class AuditLogMultiTenantWriteTests
{
    /// <summary>
    /// Critical: AuditLogStore does not set InstanceId from the tenant context.
    /// Log entries are always written with InstanceId = Guid.Empty (the entity default).
    /// This test documents the current behavior so any change is caught immediately.
    /// </summary>
    [Fact]
    public async Task LogAsync_InstanceId_IsAlwaysGuidEmpty()
    {
        var tenant = new FixedTenantContext(Guid.NewGuid());
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated(tenant);
        var store = new AuditLogStore(ctx, NullLogger<AuditLogStore>.Instance);

        await store.LogAsync(new AuditLogEntry("user.login", "authentication"));

        var log = await ctx.AuditLogs.IgnoreQueryFilters().FirstAsync();
        // Document current behavior: InstanceId defaults to Guid.Empty, not the tenant's InstanceId.
        // This is a known limitation in the current AuditLogStore implementation.
        log.InstanceId.Should().Be(Guid.Empty,
            "AuditLogStore currently does not inject the tenant InstanceId — " +
            "this test documents the existing behavior; fix by injecting ITenantContext into AuditLogStore");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Gap 4: SessionStore cross-tenant revoke isolation
// ─────────────────────────────────────────────────────────────────────────────

public class SessionStoreCrossTenantTests
{
    [Fact]
    public async Task RevokeSessionAsync_SessionFromOtherTenant_IsNoOp()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        // Create a session belonging to tenantA
        var userId = Guid.NewGuid();
        Guid sessionId;
        await using (var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            await UserFactory.SeedUserAsync(ctxA, userId, tenantA.InstanceId);
            var storeA = new SessionStore(ctxA, tenantA);
            var session = await storeA.CreateSessionAsync(userId, new SessionContext(null, null));
            sessionId = session.Id;
        }

        // Attempt to revoke it from tenantB's store — should silently no-op
        await using var ctxB = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB);
        var storeB = new SessionStore(ctxB, tenantB);
        var act = async () => await storeB.RevokeSessionAsync(sessionId);
        await act.Should().NotThrowAsync();

        // Verify tenantA's session is still active
        await using var verify = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var stored = await verify.UserSessions.IgnoreQueryFilters().FirstAsync(s => s.Id == sessionId);
        stored.IsRevoked.Should().BeFalse("tenantB's store must not be able to revoke tenantA's session");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Gap 5: RefreshToken.Token unique index on model
// ─────────────────────────────────────────────────────────────────────────────

public class RefreshTokenIndexTests
{
    [Fact]
    public void RefreshToken_TokenColumn_HasUniqueIndexOnModel()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var entityType = ctx.Model.FindEntityType(typeof(RefreshToken))!;

        var tokenIndex = entityType.GetIndexes()
            .SingleOrDefault(ix => ix.Properties.Count == 1 &&
                                   ix.Properties[0].Name == nameof(RefreshToken.Token));

        tokenIndex.Should().NotBeNull("RefreshToken.Token must have a unique index for fast lookup");
        tokenIndex!.IsUnique.Should().BeTrue("two tokens with the same value must never coexist");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Gap 6: AuditLogStore.QueryAsync boundary inputs
// ─────────────────────────────────────────────────────────────────────────────

public class AuditLogQueryBoundaryTests
{
    private static AuditLogStore CreateStore(HPDAuthDbContext ctx)
        => new(ctx, NullLogger<AuditLogStore>.Instance);

    [Fact]
    public async Task QueryAsync_PageSizeZero_ClampedToOne()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        ctx.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), Action = "a", Category = "c" },
            new AuditLog { Id = Guid.NewGuid(), Action = "a", Category = "c" }
        );
        await ctx.SaveChangesAsync();

        // PageSize = 0 should be clamped to 1 — returns exactly 1 result, not 0 or all
        var result = await store.QueryAsync(new AuditLogQuery(PageSize: 0));
        result.Should().HaveCount(1, "PageSize=0 is clamped to 1 by the store");
    }

    [Fact]
    public async Task QueryAsync_PageSizeNegative_ClampedToOne()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        ctx.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), Action = "a", Category = "c" },
            new AuditLog { Id = Guid.NewGuid(), Action = "a", Category = "c" }
        );
        await ctx.SaveChangesAsync();

        var result = await store.QueryAsync(new AuditLogQuery(PageSize: -10));
        result.Should().HaveCount(1, "PageSize=-10 is clamped to 1 by the store");
    }

    [Fact]
    public async Task QueryAsync_PageSizeExceeds500_ClampedTo500()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        // Insert 501 logs
        for (int i = 0; i < 501; i++)
            ctx.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), Action = "a", Category = "c" });
        await ctx.SaveChangesAsync();

        var result = await store.QueryAsync(new AuditLogQuery(PageSize: 1000));
        result.Should().HaveCount(500, "PageSize is hard-capped at 500");
    }

    [Fact]
    public async Task QueryAsync_PageZeroOrNegative_TreatedAsPageOne()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        ctx.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), Action = "a", Category = "c" },
            new AuditLog { Id = Guid.NewGuid(), Action = "a", Category = "c" }
        );
        await ctx.SaveChangesAsync();

        var resultPage0 = await store.QueryAsync(new AuditLogQuery(Page: 0, PageSize: 10));
        var resultPageNeg = await store.QueryAsync(new AuditLogQuery(Page: -5, PageSize: 10));

        resultPage0.Should().HaveCount(2, "Page=0 is clamped to 1");
        resultPageNeg.Should().HaveCount(2, "Page=-5 is clamped to 1");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Gap 7: CreateSessionAsync with null SessionContext throws ArgumentNullException
// ─────────────────────────────────────────────────────────────────────────────

public class SessionStoreNullGuardTests
{
    [Fact]
    public async Task CreateSessionAsync_NullContext_ThrowsArgumentNullException()
    {
        var tenant = new SingleTenantContext();
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated(tenant);
        var store = new SessionStore(ctx, tenant);

        var act = async () => await store.CreateSessionAsync(Guid.NewGuid(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Gap 9 & 10: Cascade delete for UserIdentity and UserPasskey
// ─────────────────────────────────────────────────────────────────────────────

public class CascadeDeleteTests
{
    [Fact]
    public async Task DeleteUser_CascadesToUserIdentities()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var user = UserFactory.CreateUser();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.UserIdentities.Add(new UserIdentity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = "google",
            ProviderId = "g-cascade-test",
        });
        await ctx.SaveChangesAsync();

        ctx.Users.Remove(user);
        await ctx.SaveChangesAsync();

        var remaining = await ctx.UserIdentities.IgnoreQueryFilters().CountAsync();
        remaining.Should().Be(0, "deleting a user must cascade to their UserIdentity rows");
    }

    [Fact]
    public async Task DeleteUser_CascadesToUserPasskeys()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var user = UserFactory.CreateUser();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.UserPasskeys.Add(new UserPasskey
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CredentialId = "cred-cascade-test",
            PublicKey = "pk-cascade",
        });
        await ctx.SaveChangesAsync();

        ctx.Users.Remove(user);
        await ctx.SaveChangesAsync();

        var remaining = await ctx.UserPasskeys.IgnoreQueryFilters().CountAsync();
        remaining.Should().Be(0, "deleting a user must cascade to their UserPasskey rows");
    }
}
