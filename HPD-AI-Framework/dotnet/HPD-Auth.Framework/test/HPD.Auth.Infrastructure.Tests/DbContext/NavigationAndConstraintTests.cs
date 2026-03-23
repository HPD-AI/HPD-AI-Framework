using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HPD.Auth.Infrastructure.Tests.DbContext;

/// <summary>
/// Section 15: Navigation Relationships — FK Integrity
/// Section 16: UserPasskey — CredentialId Unique Index
/// Section 17: UserIdentity — Composite Unique Index
/// Section 18: RequiredActions JSON Serialization
/// </summary>
public class NavigationRelationshipTests
{
    // ── 15.1 RefreshToken.User navigation loads the correct ApplicationUser ───

    [Fact]
    public async Task RefreshToken_UserNavigation_LoadsCorrectUser()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var user = UserFactory.CreateUser();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var token = UserFactory.CreateRefreshToken(user.Id);
        ctx.RefreshTokens.Add(token);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var loaded = await ctx.RefreshTokens
            .Include(t => t.User)
            .FirstAsync(t => t.Id == token.Id);

        loaded.User.Should().NotBeNull();
        loaded.User.Id.Should().Be(user.Id);
        loaded.User.Email.Should().Be(user.Email);
    }

    // ── 15.2 UserSession.User navigation loads the correct ApplicationUser ────

    [Fact]
    public async Task UserSession_UserNavigation_LoadsCorrectUser()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var user = UserFactory.CreateUser();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var session = new UserSession { Id = Guid.NewGuid(), UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(1) };
        ctx.UserSessions.Add(session);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var loaded = await ctx.UserSessions
            .Include(s => s.User)
            .FirstAsync(s => s.Id == session.Id);

        loaded.User.Should().NotBeNull();
        loaded.User.Id.Should().Be(user.Id);
    }

    // ── 15.3 UserIdentity.User navigation loads the correct ApplicationUser ───

    [Fact]
    public async Task UserIdentity_UserNavigation_LoadsCorrectUser()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var user = UserFactory.CreateUser();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var identity = new UserIdentity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = "google",
            ProviderId = "g-12345",
        };
        ctx.UserIdentities.Add(identity);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var loaded = await ctx.UserIdentities
            .Include(i => i.User)
            .FirstAsync(i => i.Id == identity.Id);

        loaded.User.Should().NotBeNull();
        loaded.User.Id.Should().Be(user.Id);
    }

    // ── 15.4 UserPasskey.User navigation loads the correct ApplicationUser ────

    [Fact]
    public async Task UserPasskey_UserNavigation_LoadsCorrectUser()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var user = UserFactory.CreateUser();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var passkey = new UserPasskey
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CredentialId = "cred-nav-test",
            PublicKey = "pk-nav-test",
        };
        ctx.UserPasskeys.Add(passkey);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var loaded = await ctx.UserPasskeys
            .Include(p => p.User)
            .FirstAsync(p => p.Id == passkey.Id);

        loaded.User.Should().NotBeNull();
        loaded.User.Id.Should().Be(user.Id);
    }

    // ── 15.5 Cascade delete — deleting ApplicationUser cascades to RefreshTokens

    [Fact]
    public async Task DeleteUser_CascadesToRefreshTokens()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var user = UserFactory.CreateUser();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var token = UserFactory.CreateRefreshToken(user.Id);
        ctx.RefreshTokens.Add(token);
        await ctx.SaveChangesAsync();

        ctx.Users.Remove(user);
        await ctx.SaveChangesAsync();

        var remaining = await ctx.RefreshTokens.IgnoreQueryFilters().CountAsync();
        remaining.Should().Be(0);
    }

    // ── 15.6 Cascade delete — deleting ApplicationUser cascades to UserSessions

    [Fact]
    public async Task DeleteUser_CascadesToUserSessions()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var user = UserFactory.CreateUser();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var session = new UserSession { Id = Guid.NewGuid(), UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(1) };
        ctx.UserSessions.Add(session);
        await ctx.SaveChangesAsync();

        ctx.Users.Remove(user);
        await ctx.SaveChangesAsync();

        var remaining = await ctx.UserSessions.IgnoreQueryFilters().CountAsync();
        remaining.Should().Be(0);
    }
}

/// <summary>
/// Section 16: UserPasskey — CredentialId Unique Index
/// </summary>
public class UserPasskeyConstraintTests
{
    // ── 16.1 CredentialId unique index configured on model ───────────────────
    // NOTE: The EF Core in-memory provider does not enforce non-PK unique indexes at
    // runtime. This test verifies the index is correctly configured in the model.

    [Fact]
    public void UserPasskey_CredentialId_UniqueIndexConfiguredOnModel()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var entityType = ctx.Model.FindEntityType(typeof(UserPasskey))!;

        var credIndex = entityType.GetIndexes()
            .SingleOrDefault(ix => ix.Properties.Count == 1 &&
                                   ix.Properties[0].Name == nameof(UserPasskey.CredentialId));

        credIndex.Should().NotBeNull("CredentialId unique index must exist");
        credIndex!.IsUnique.Should().BeTrue("CredentialId must be globally unique per FIDO2 spec");
    }

    // ── 16.2 Different users can have different passkeys ─────────────────────

    [Fact]
    public async Task UserPasskey_DifferentCredentialIds_NoException()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var user1 = UserFactory.CreateUser();
        var user2 = UserFactory.CreateUser();
        ctx.Users.AddRange(user1, user2);
        await ctx.SaveChangesAsync();

        ctx.UserPasskeys.Add(new UserPasskey { Id = Guid.NewGuid(), UserId = user1.Id, CredentialId = "cred-1", PublicKey = "pk1" });
        ctx.UserPasskeys.Add(new UserPasskey { Id = Guid.NewGuid(), UserId = user2.Id, CredentialId = "cred-2", PublicKey = "pk2" });

        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }
}

/// <summary>
/// Section 17: UserIdentity — Composite Unique Index
/// </summary>
public class UserIdentityConstraintTests
{
    // ── 17.1 Same Provider+ProviderId unique index configured on model ──────────
    // NOTE: The EF Core in-memory provider does not enforce non-PK unique indexes at
    // runtime. This test verifies the index is correctly configured in the model.

    [Fact]
    public void UserIdentity_DuplicateProviderInSameTenant_UniqueIndexConfiguredOnModel()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var entityType = ctx.Model.FindEntityType(typeof(UserIdentity))!;

        var compositeIndex = entityType.GetIndexes()
            .SingleOrDefault(ix => ix.Properties.Count == 3 &&
                                   ix.Properties.Any(p => p.Name == nameof(UserIdentity.InstanceId)) &&
                                   ix.Properties.Any(p => p.Name == nameof(UserIdentity.Provider)) &&
                                   ix.Properties.Any(p => p.Name == nameof(UserIdentity.ProviderId)));

        compositeIndex.Should().NotBeNull("composite (InstanceId, Provider, ProviderId) unique index must exist");
        compositeIndex!.IsUnique.Should().BeTrue("same provider+providerId must be unique within a tenant");
    }

    // ── 17.2 Same Provider+ProviderId allowed across different tenants ─────────

    [Fact]
    public async Task UserIdentity_SameProviderDifferentTenants_NoException()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        var userA = UserFactory.CreateUser(tenantA.InstanceId);
        var userB = UserFactory.CreateUser(tenantB.InstanceId);

        await using (var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            ctxA.Users.Add(userA);
            ctxA.UserIdentities.Add(new UserIdentity { Id = Guid.NewGuid(), InstanceId = tenantA.InstanceId, UserId = userA.Id, Provider = "google", ProviderId = "g-123" });
            await ctxA.SaveChangesAsync();
        }
        await using (var ctxB = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            ctxB.Users.Add(userB);
            ctxB.UserIdentities.Add(new UserIdentity { Id = Guid.NewGuid(), InstanceId = tenantB.InstanceId, UserId = userB.Id, Provider = "google", ProviderId = "g-123" });
            var act = async () => await ctxB.SaveChangesAsync();
            await act.Should().NotThrowAsync();
        }
    }
}

/// <summary>
/// Section 18: RequiredActions JSON Serialization
/// </summary>
public class RequiredActionsSerializationTests
{
    // ── 18.1 RequiredActions is serialized and deserialized correctly ─────────

    [Fact]
    public async Task RequiredActions_RoundTrip_PreservesValues()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.Empty;

        await using (var ctx = HPDAuthDbContextFactory.CreateInMemory(dbName))
        {
            var user = UserFactory.CreateUser();
            userId = user.Id;
            user.RequiredActions = new List<string> { "VERIFY_EMAIL", "UPDATE_PASSWORD" };
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = HPDAuthDbContextFactory.CreateInMemory(dbName);
        var loaded = await ctx2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        loaded.RequiredActions.Should().BeEquivalentTo(new[] { "VERIFY_EMAIL", "UPDATE_PASSWORD" });
    }

    // ── 18.2 Empty RequiredActions list round-trips correctly ─────────────────

    [Fact]
    public async Task RequiredActions_EmptyList_RoundTrips()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.Empty;

        await using (var ctx = HPDAuthDbContextFactory.CreateInMemory(dbName))
        {
            var user = UserFactory.CreateUser();
            userId = user.Id;
            user.RequiredActions = new List<string>();
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = HPDAuthDbContextFactory.CreateInMemory(dbName);
        var loaded = await ctx2.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        loaded.RequiredActions.Should().BeEmpty();
    }
}
