using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Stores;
using HPD.Auth.Infrastructure.Tests.Helpers;

namespace HPD.Auth.Infrastructure.Tests.Stores;

/// <summary>
/// Section 9: SessionStore — Query Active Sessions
/// </summary>
public class SessionStoreQueryTests
{
    private static (HPDAuthDbContext ctx, SessionStore store) Create(ITenantContext? tenant = null)
    {
        tenant ??= new SingleTenantContext();
        var ctx = HPDAuthDbContextFactory.CreateIsolated(tenant);
        var store = new SessionStore(ctx, tenant);
        return (ctx, store);
    }

    private static UserSession ActiveSession(Guid userId) => new UserSession
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
        IsRevoked = false,
        SessionState = "active",
        LastActiveAt = DateTime.UtcNow,
    };

    // ── 9.1 Active session query excludes revoked sessions ───────────────────

    [Fact]
    public async Task GetActiveSessionsAsync_ExcludesRevokedSessions()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);

        var active = ActiveSession(userId);
        var revoked = ActiveSession(userId);
        revoked.IsRevoked = true;

        ctx.UserSessions.AddRange(active, revoked);
        await ctx.SaveChangesAsync();

        var result = await store.GetActiveSessionsAsync(userId);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(active.Id);

        await ctx.DisposeAsync();
    }

    // ── 9.2 Active session query excludes expired sessions ───────────────────

    [Fact]
    public async Task GetActiveSessionsAsync_ExcludesExpiredSessions()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);

        var active = ActiveSession(userId);
        var expired = ActiveSession(userId);
        expired.ExpiresAt = DateTime.UtcNow.AddDays(-1);

        ctx.UserSessions.AddRange(active, expired);
        await ctx.SaveChangesAsync();

        var result = await store.GetActiveSessionsAsync(userId);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(active.Id);

        await ctx.DisposeAsync();
    }

    // ── 9.3 Active session query excludes both revoked and expired ────────────

    [Fact]
    public async Task GetActiveSessionsAsync_ExcludesRevokedAndExpired()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);

        var active = ActiveSession(userId);

        var revokedNotExpired = ActiveSession(userId);
        revokedNotExpired.IsRevoked = true;

        var expiredNotRevoked = ActiveSession(userId);
        expiredNotRevoked.ExpiresAt = DateTime.UtcNow.AddDays(-1);

        var revokedAndExpired = ActiveSession(userId);
        revokedAndExpired.IsRevoked = true;
        revokedAndExpired.ExpiresAt = DateTime.UtcNow.AddDays(-1);

        ctx.UserSessions.AddRange(active, revokedNotExpired, expiredNotRevoked, revokedAndExpired);
        await ctx.SaveChangesAsync();

        var result = await store.GetActiveSessionsAsync(userId);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(active.Id);

        await ctx.DisposeAsync();
    }

    // ── 9.4 GetActiveSessionsAsync returns empty list when no active sessions ─

    [Fact]
    public async Task GetActiveSessionsAsync_NonExistentUser_ReturnsEmptyList()
    {
        var (ctx, store) = Create();

        var result = await store.GetActiveSessionsAsync(Guid.NewGuid());

        result.Should().NotBeNull();
        result.Should().BeEmpty();

        await ctx.DisposeAsync();
    }

    // ── 9.5 Active sessions are ordered by LastActiveAt descending ────────────

    [Fact]
    public async Task GetActiveSessionsAsync_OrderedByLastActiveAtDescending()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);
        var now = DateTime.UtcNow;

        var s1 = ActiveSession(userId); s1.LastActiveAt = now.AddMinutes(-2);
        var s2 = ActiveSession(userId); s2.LastActiveAt = now.AddMinutes(-1);
        var s3 = ActiveSession(userId); s3.LastActiveAt = now;

        ctx.UserSessions.AddRange(s1, s2, s3);
        await ctx.SaveChangesAsync();

        var result = await store.GetActiveSessionsAsync(userId);

        result[0].Id.Should().Be(s3.Id);
        result[1].Id.Should().Be(s2.Id);
        result[2].Id.Should().Be(s1.Id);

        await ctx.DisposeAsync();
    }
}
