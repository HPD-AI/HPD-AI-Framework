using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HPD.Auth.Infrastructure.Tests.DbContext;

/// <summary>
/// Section 2: DbContext — Tenant-Scoped Unique Indexes
/// Section 3: DbContext — TenantSettings Primary Key
/// </summary>
public class TenantScopedIndexTests
{
    // ── 2.1 Same email allowed across different tenants ──────────────────────

    [Fact]
    public async Task SameEmail_DifferentTenants_AllowedNoException()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var email = "alice@example.com";

        await using (var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            ctxA.Users.Add(UserFactory.CreateUser(tenantA.InstanceId, email));
            await ctxA.SaveChangesAsync();
        }
        await using (var ctxB = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            ctxB.Users.Add(UserFactory.CreateUser(tenantB.InstanceId, email));
            var act = async () => await ctxB.SaveChangesAsync();
            await act.Should().NotThrowAsync();
        }

        await using var verify = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var all = await verify.Users.IgnoreQueryFilters().ToListAsync();
        all.Should().HaveCount(2);
    }

    // ── 2.2 Same email within same tenant is rejected (model-level verification) ──
    // NOTE: The EF Core in-memory provider does not enforce non-PK unique indexes at
    // runtime. Uniqueness is enforced at the database level (PostgreSQL/SQL Server).
    // This test verifies the index is correctly configured in the model.

    [Fact]
    public void SameEmail_SameTenant_UniqueIndexConfiguredOnModel()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var entityType = ctx.Model.FindEntityType(typeof(ApplicationUser))!;

        var compositeEmailIndex = entityType.GetIndexes()
            .SingleOrDefault(ix => ix.Properties.Count == 2 &&
                                   ix.Properties.Any(p => p.Name == nameof(ApplicationUser.InstanceId)) &&
                                   ix.Properties.Any(p => p.Name == nameof(ApplicationUser.NormalizedEmail)));

        compositeEmailIndex.Should().NotBeNull("composite (InstanceId, NormalizedEmail) unique index must exist");
        compositeEmailIndex!.IsUnique.Should().BeTrue("same email within same tenant must be unique");
    }

    // ── 2.3 Same username allowed across different tenants ────────────────────

    [Fact]
    public async Task SameUsername_DifferentTenants_AllowedNoException()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var userA = UserFactory.CreateUser(tenantA.InstanceId);
        userA.UserName = "alice";
        userA.NormalizedUserName = "ALICE";
        userA.Email = "alice@tenanta.com";
        userA.NormalizedEmail = "ALICE@TENANTA.COM";

        var userB = UserFactory.CreateUser(tenantB.InstanceId);
        userB.UserName = "alice";
        userB.NormalizedUserName = "ALICE";
        userB.Email = "alice@tenantb.com";
        userB.NormalizedEmail = "ALICE@TENANTB.COM";

        await using (var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            ctxA.Users.Add(userA);
            await ctxA.SaveChangesAsync();
        }
        await using (var ctxB = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            ctxB.Users.Add(userB);
            var act = async () => await ctxB.SaveChangesAsync();
            await act.Should().NotThrowAsync();
        }
    }

    // ── 2.4 Same username within same tenant is rejected (model-level verification) ──
    // NOTE: The EF Core in-memory provider does not enforce non-PK unique indexes at
    // runtime. Uniqueness is enforced at the database level (PostgreSQL/SQL Server).
    // This test verifies the index is correctly configured in the model.

    [Fact]
    public void SameUsername_SameTenant_UniqueIndexConfiguredOnModel()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var entityType = ctx.Model.FindEntityType(typeof(ApplicationUser))!;

        var compositeUsernameIndex = entityType.GetIndexes()
            .SingleOrDefault(ix => ix.Properties.Count == 2 &&
                                   ix.Properties.Any(p => p.Name == nameof(ApplicationUser.InstanceId)) &&
                                   ix.Properties.Any(p => p.Name == nameof(ApplicationUser.NormalizedUserName)));

        compositeUsernameIndex.Should().NotBeNull("composite (InstanceId, NormalizedUserName) unique index must exist");
        compositeUsernameIndex!.IsUnique.Should().BeTrue("same username within same tenant must be unique");
    }

    // ── 2.5 Default Identity unique indexes are non-unique, composites are unique

    [Fact]
    public void IndexConfiguration_DefaultEmailIndex_IsNotUnique()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var entityType = ctx.Model.FindEntityType(typeof(ApplicationUser))!;

        var emailIndex = entityType.GetIndexes()
            .SingleOrDefault(ix => ix.Properties.Count == 1 &&
                                   ix.Properties[0].Name == nameof(ApplicationUser.NormalizedEmail));

        emailIndex.Should().NotBeNull("a single-column NormalizedEmail index should exist");
        emailIndex!.IsUnique.Should().BeFalse("the single-column email index must not be unique in multi-tenant setup");
    }

    [Fact]
    public void IndexConfiguration_DefaultUsernameIndex_IsNotUnique()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var entityType = ctx.Model.FindEntityType(typeof(ApplicationUser))!;

        var usernameIndex = entityType.GetIndexes()
            .SingleOrDefault(ix => ix.Properties.Count == 1 &&
                                   ix.Properties[0].Name == nameof(ApplicationUser.NormalizedUserName));

        usernameIndex.Should().NotBeNull("a single-column NormalizedUserName index should exist");
        usernameIndex!.IsUnique.Should().BeFalse();
    }

    [Fact]
    public void IndexConfiguration_CompositeEmailIndex_IsUnique()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var entityType = ctx.Model.FindEntityType(typeof(ApplicationUser))!;

        var compositeEmailIndex = entityType.GetIndexes()
            .SingleOrDefault(ix => ix.Properties.Count == 2 &&
                                   ix.Properties.Any(p => p.Name == nameof(ApplicationUser.InstanceId)) &&
                                   ix.Properties.Any(p => p.Name == nameof(ApplicationUser.NormalizedEmail)));

        compositeEmailIndex.Should().NotBeNull("composite (InstanceId, NormalizedEmail) index should exist");
        compositeEmailIndex!.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void IndexConfiguration_CompositeUsernameIndex_IsUnique()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var entityType = ctx.Model.FindEntityType(typeof(ApplicationUser))!;

        var compositeUsernameIndex = entityType.GetIndexes()
            .SingleOrDefault(ix => ix.Properties.Count == 2 &&
                                   ix.Properties.Any(p => p.Name == nameof(ApplicationUser.InstanceId)) &&
                                   ix.Properties.Any(p => p.Name == nameof(ApplicationUser.NormalizedUserName)));

        compositeUsernameIndex.Should().NotBeNull("composite (InstanceId, NormalizedUserName) index should exist");
        compositeUsernameIndex!.IsUnique.Should().BeTrue();
    }
}

/// <summary>
/// Section 3: DbContext — TenantSettings Primary Key
/// </summary>
public class TenantSettingsPrimaryKeyTests
{
    // ── 3.1 TenantSettings primary key is InstanceId ─────────────────────────

    [Fact]
    public void TenantSettings_PrimaryKey_IsInstanceId()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var entityType = ctx.Model.FindEntityType(typeof(TenantSettings))!;
        var pk = entityType.FindPrimaryKey()!;

        pk.Properties.Should().HaveCount(1);
        pk.Properties[0].Name.Should().Be(nameof(TenantSettings.InstanceId));
    }

    // ── 3.2 TenantSettings upsert pattern ────────────────────────────────────

    [Fact]
    public async Task TenantSettings_Upsert_UpdatesExistingRow()
    {
        var instanceId = Guid.NewGuid();
        var tenant = new FixedTenantContext(instanceId);
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated(tenant);

        var settings = new TenantSettings { InstanceId = instanceId, DisplayName = "Default" };
        ctx.TenantSettings.Add(settings);
        await ctx.SaveChangesAsync();

        // Use tracked entity directly (it's still tracked after SaveChangesAsync)
        settings.DisplayName = "Updated";
        await ctx.SaveChangesAsync();

        var count = await ctx.TenantSettings.CountAsync();
        count.Should().Be(1);

        ctx.ChangeTracker.Clear();
        var reloaded = await ctx.TenantSettings.FirstAsync();
        reloaded.DisplayName.Should().Be("Updated");
    }

    // ── 3.3 Two tenants cannot share the same InstanceId in TenantSettings ───

    [Fact]
    public async Task TenantSettings_DuplicateInstanceId_ThrowsOnSave()
    {
        var dbName = Guid.NewGuid().ToString();
        var instanceId = Guid.NewGuid();
        var tenant = new FixedTenantContext(instanceId);

        await using (var ctx = HPDAuthDbContextFactory.CreateInMemory(dbName, tenant))
        {
            ctx.TenantSettings.Add(new TenantSettings { InstanceId = instanceId });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = HPDAuthDbContextFactory.CreateInMemory(dbName, tenant);
        ctx2.TenantSettings.Add(new TenantSettings { InstanceId = instanceId });
        var act = async () => await ctx2.SaveChangesAsync();
        await act.Should().ThrowAsync<Exception>();
    }
}
