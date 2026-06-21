using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Stores;
using HPD.Auth.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HPD.Auth.Infrastructure.Tests.Stores;

/// <summary>
/// Sections 12–14: RefreshTokenStore — Create, Retrieve, Update, RevokeAllForUser
/// </summary>
public class RefreshTokenStoreTests
{
    private static (HPDAuthDbContext ctx, RefreshTokenStore store) Create(ITenantContext? tenant = null)
    {
        tenant ??= new SingleTenantContext();
        var ctx = HPDAuthDbContextFactory.CreateIsolated(tenant);
        var store = new RefreshTokenStore(ctx);
        return (ctx, store);
    }

    // ── 12.1 Token is persisted after CreateAsync ─────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsToken()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);
        var token = UserFactory.CreateRefreshToken(userId);

        await store.CreateAsync(token);

        var found = await ctx.RefreshTokens.FindAsync(token.Id);
        found.Should().NotBeNull();
        found!.Token.Should().Be(token.Token);
        found.UserId.Should().Be(userId);

        await ctx.DisposeAsync();
    }

    // ── 12.2 GetByTokenAsync returns correct token ────────────────────────────

    [Fact]
    public async Task GetByTokenAsync_ExistingToken_ReturnsToken()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);
        var token = UserFactory.CreateRefreshToken(userId, token: "abc123xyz");

        await store.CreateAsync(token);

        var result = await store.GetByTokenAsync("abc123xyz");

        result.Should().NotBeNull();
        result!.Token.Should().Be("abc123xyz");

        await ctx.DisposeAsync();
    }

    // ── 12.3 GetByTokenAsync returns null for unknown token ───────────────────

    [Fact]
    public async Task GetByTokenAsync_UnknownToken_ReturnsNull()
    {
        var (ctx, store) = Create();

        var result = await store.GetByTokenAsync("this-token-does-not-exist");

        result.Should().BeNull();

        await ctx.DisposeAsync();
    }

    // ── 12.4 GetByTokenAsync returns null for empty/null input ────────────────

    [Fact]
    public async Task GetByTokenAsync_EmptyToken_ReturnsNull()
    {
        var (ctx, store) = Create();

        var result = await store.GetByTokenAsync(string.Empty);
        result.Should().BeNull();

        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task GetByTokenAsync_WhitespaceToken_ReturnsNull()
    {
        var (ctx, store) = Create();

        var result = await store.GetByTokenAsync("   ");
        result.Should().BeNull();

        await ctx.DisposeAsync();
    }

    // ── 12.5 GetByTokenAsync respects tenant query filter ─────────────────────

    [Fact]
    public async Task GetByTokenAsync_DifferentTenant_ReturnsNull()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());
        var tokenValue = Guid.NewGuid().ToString("N");

        // Insert token under tenantB
        await using (var ctxB = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            var userId = Guid.NewGuid();
            var user = UserFactory.CreateUser(tenantB.InstanceId);
            user.Id = userId;
            ctxB.Users.Add(user);
            var t = UserFactory.CreateRefreshToken(userId, tenantB.InstanceId, tokenValue);
            ctxB.RefreshTokens.Add(t);
            await ctxB.SaveChangesAsync();
        }

        // Query from tenantA context — should return null
        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var store = new RefreshTokenStore(ctxA);
        var result = await store.GetByTokenAsync(tokenValue);

        result.Should().BeNull();
    }

    // ── 13.1 UpdateAsync persists IsUsed = true ───────────────────────────────

    [Fact]
    public async Task UpdateAsync_IsUsed_PersistedCorrectly()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);
        var token = UserFactory.CreateRefreshToken(userId);
        await store.CreateAsync(token);
        ctx.ChangeTracker.Clear(); // detach so GetByTokenAsync AsNoTracking entity doesn't conflict

        var loaded = await store.GetByTokenAsync(token.Token);
        loaded!.IsUsed = true;
        await store.UpdateAsync(loaded);

        ctx.ChangeTracker.Clear();
        var reloaded = await ctx.RefreshTokens.FindAsync(token.Id);
        reloaded!.IsUsed.Should().BeTrue();

        await ctx.DisposeAsync();
    }

    // ── 13.2 UpdateAsync persists IsRevoked and RevokedAt ─────────────────────

    [Fact]
    public async Task UpdateAsync_IsRevokedAndRevokedAt_PersistedCorrectly()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);
        var token = UserFactory.CreateRefreshToken(userId);
        await store.CreateAsync(token);
        ctx.ChangeTracker.Clear(); // detach to avoid tracking conflict with AsNoTracking entity

        var loaded = await store.GetByTokenAsync(token.Token);
        var now = DateTime.UtcNow;
        loaded!.IsRevoked = true;
        loaded.RevokedAt = now;
        await store.UpdateAsync(loaded);

        ctx.ChangeTracker.Clear();
        var reloaded = await ctx.RefreshTokens.FindAsync(token.Id);
        reloaded!.IsRevoked.Should().BeTrue();
        reloaded.RevokedAt.Should().NotBeNull();

        await ctx.DisposeAsync();
    }

    // ── 13.3 UpdateAsync with a detached (AsNoTracking) entity works correctly ─

    [Fact]
    public async Task UpdateAsync_DetachedEntity_UpdatesWithoutTrackingException()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);
        var token = UserFactory.CreateRefreshToken(userId);
        await store.CreateAsync(token);
        ctx.ChangeTracker.Clear(); // detach so Update() re-attaches without conflict

        // GetByTokenAsync uses AsNoTracking — returned entity is not tracked
        var detached = await store.GetByTokenAsync(token.Token);
        detached!.IsUsed = true;

        var act = async () => await store.UpdateAsync(detached);
        await act.Should().NotThrowAsync();

        ctx.ChangeTracker.Clear();
        var reloaded = await ctx.RefreshTokens.FindAsync(token.Id);
        reloaded!.IsUsed.Should().BeTrue();

        await ctx.DisposeAsync();
    }

    // ── 14.1 RevokeAllForUserAsync marks all user tokens as revoked ───────────

    [Fact]
    public async Task RevokeAllForUserAsync_RevokesAllUserTokens()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);

        for (int i = 0; i < 3; i++)
            await store.CreateAsync(UserFactory.CreateRefreshToken(userId));

        await store.RevokeAllForUserAsync(userId);

        ctx.ChangeTracker.Clear();
        var all = await ctx.RefreshTokens.ToListAsync();
        all.Should().OnlyContain(t => t.IsRevoked);
        all.Should().OnlyContain(t => t.RevokedAt.HasValue);

        await ctx.DisposeAsync();
    }

    // ── 14.2 RevokeAllForUserAsync does not revoke other users' tokens ─────────

    [Fact]
    public async Task RevokeAllForUserAsync_DoesNotAffectOtherUsers()
    {
        var (ctx, store) = Create();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, user1);
        await UserFactory.SeedUserAsync(ctx, user2);

        await store.CreateAsync(UserFactory.CreateRefreshToken(user1));
        await store.CreateAsync(UserFactory.CreateRefreshToken(user2));

        await store.RevokeAllForUserAsync(user1);

        ctx.ChangeTracker.Clear();
        var user2Tokens = await ctx.RefreshTokens.Where(t => t.UserId == user2).ToListAsync();
        user2Tokens.Should().OnlyContain(t => !t.IsRevoked);

        await ctx.DisposeAsync();
    }

    // ── 14.3 RevokeAllForUserAsync skips already-revoked tokens ──────────────

    [Fact]
    public async Task RevokeAllForUserAsync_AlreadyRevokedToken_NotModifiedAgain()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);

        var t1 = UserFactory.CreateRefreshToken(userId);
        var t2 = UserFactory.CreateRefreshToken(userId);
        t2.IsRevoked = true;
        t2.RevokedAt = DateTime.UtcNow.AddMinutes(-10);
        var originalRevokedAt = t2.RevokedAt;

        ctx.RefreshTokens.AddRange(t1, t2);
        await ctx.SaveChangesAsync();

        var act = async () => await store.RevokeAllForUserAsync(userId);
        await act.Should().NotThrowAsync();

        ctx.ChangeTracker.Clear();
        var all = await ctx.RefreshTokens.ToListAsync();
        all.Should().OnlyContain(t => t.IsRevoked);

        var t2Loaded = await ctx.RefreshTokens.FindAsync(t2.Id);
        t2Loaded!.RevokedAt.Should().Be(originalRevokedAt);

        await ctx.DisposeAsync();
    }

    // ── 14.4 RevokeAllForUserAsync for user with no tokens is a no-op ─────────

    [Fact]
    public async Task RevokeAllForUserAsync_NoTokens_NoException()
    {
        var (ctx, store) = Create();

        var act = async () => await store.RevokeAllForUserAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();

        await ctx.DisposeAsync();
    }

    // ── 14.5 RevokeAllForUserAsync is tenant-scoped ───────────────────────────

    [Fact]
    public async Task RevokeAllForUserAsync_TenantScoped_DoesNotAffectOtherTenantTokens()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());
        var userId = Guid.NewGuid();
        var tokenValue = Guid.NewGuid().ToString("N");

        // Insert a token for userId under tenantA
        await using (var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            await UserFactory.SeedUserAsync(ctxA, userId, tenantA.InstanceId);
            var t = UserFactory.CreateRefreshToken(userId, tenantA.InstanceId, tokenValue);
            ctxA.RefreshTokens.Add(t);
            await ctxA.SaveChangesAsync();
        }

        // Revoke from tenantB context — should not see tenantA's tokens
        await using var ctxB = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB);
        var storeB = new RefreshTokenStore(ctxB);
        await storeB.RevokeAllForUserAsync(userId);

        // Verify tenantA's token is still not revoked
        await using var verify = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var tokens = await verify.RefreshTokens.IgnoreQueryFilters().ToListAsync();
        tokens.Should().HaveCount(1);
        tokens[0].IsRevoked.Should().BeFalse();
    }
}
