using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Stores;
using HPD.Auth.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HPD.Auth.Infrastructure.Tests.Stores;

/// <summary>
/// Section 10: SessionStore — Revoke Single Session
/// Section 11: SessionStore — Revoke All Sessions
/// </summary>
public class SessionStoreRevokeTests
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

    // ── 10.1 RevokeSessionAsync marks session as revoked ─────────────────────

    [Fact]
    public async Task RevokeSessionAsync_MarksSessionRevoked()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);
        var session = ActiveSession(userId);
        ctx.UserSessions.Add(session);
        await ctx.SaveChangesAsync();

        var before = DateTime.UtcNow;
        await store.RevokeSessionAsync(session.Id);
        var after = DateTime.UtcNow;

        ctx.ChangeTracker.Clear();
        var loaded = await ctx.UserSessions.FirstAsync(s => s.Id == session.Id);
        loaded.IsRevoked.Should().BeTrue();
        loaded.RevokedAt.Should().NotBeNull();
        loaded.RevokedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after.AddSeconds(1));
        loaded.SessionState.Should().Be("logged_out");

        await ctx.DisposeAsync();
    }

    // ── 10.2 RevokeSessionAsync is idempotent ─────────────────────────────────

    [Fact]
    public async Task RevokeSessionAsync_CalledTwice_NoException()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);
        var session = ActiveSession(userId);
        ctx.UserSessions.Add(session);
        await ctx.SaveChangesAsync();

        await store.RevokeSessionAsync(session.Id);

        var act = async () => await store.RevokeSessionAsync(session.Id);
        await act.Should().NotThrowAsync();

        ctx.ChangeTracker.Clear();
        var loaded = await ctx.UserSessions.FirstAsync(s => s.Id == session.Id);
        loaded.IsRevoked.Should().BeTrue();

        await ctx.DisposeAsync();
    }

    // ── 10.3 RevokeSessionAsync on non-existent ID is a no-op ────────────────

    [Fact]
    public async Task RevokeSessionAsync_NonExistentId_NoException()
    {
        var (ctx, store) = Create();

        var act = async () => await store.RevokeSessionAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();

        await ctx.DisposeAsync();
    }

    // ── 10.4 RevokeSessionAsync does not affect other users' sessions ─────────

    [Fact]
    public async Task RevokeSessionAsync_DoesNotAffectOtherUsersSessions()
    {
        var (ctx, store) = Create();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, user1);
        await UserFactory.SeedUserAsync(ctx, user2);

        var s1 = ActiveSession(user1);
        var s2 = ActiveSession(user2);
        ctx.UserSessions.AddRange(s1, s2);
        await ctx.SaveChangesAsync();

        await store.RevokeSessionAsync(s1.Id);

        ctx.ChangeTracker.Clear();
        var user2Session = await ctx.UserSessions.FirstAsync(s => s.Id == s2.Id);
        user2Session.IsRevoked.Should().BeFalse();

        await ctx.DisposeAsync();
    }

    // ── 11.1 RevokeAllSessionsAsync revokes all sessions for a user ───────────

    [Fact]
    public async Task RevokeAllSessionsAsync_RevokesAllUserSessions()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);

        var sessions = Enumerable.Range(0, 3).Select(_ => ActiveSession(userId)).ToList();
        ctx.UserSessions.AddRange(sessions);
        await ctx.SaveChangesAsync();

        await store.RevokeAllSessionsAsync(userId);

        ctx.ChangeTracker.Clear();
        var all = await ctx.UserSessions.ToListAsync();
        all.Should().OnlyContain(s => s.IsRevoked);
        all.Should().OnlyContain(s => s.RevokedAt.HasValue);

        await ctx.DisposeAsync();
    }

    // ── 11.2 RevokeAllSessionsAsync with exceptSessionId keeps one active ──────

    [Fact]
    public async Task RevokeAllSessionsAsync_ExceptCurrent_KeepsCurrentActive()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);

        var current = ActiveSession(userId);
        var other1 = ActiveSession(userId);
        var other2 = ActiveSession(userId);
        ctx.UserSessions.AddRange(current, other1, other2);
        await ctx.SaveChangesAsync();

        await store.RevokeAllSessionsAsync(userId, exceptSessionId: current.Id);

        ctx.ChangeTracker.Clear();
        var currentLoaded = await ctx.UserSessions.FirstAsync(s => s.Id == current.Id);
        currentLoaded.IsRevoked.Should().BeFalse();

        var others = await ctx.UserSessions.Where(s => s.Id != current.Id).ToListAsync();
        others.Should().OnlyContain(s => s.IsRevoked);

        await ctx.DisposeAsync();
    }

    // ── 11.3 RevokeAllSessionsAsync does not affect other users ──────────────

    [Fact]
    public async Task RevokeAllSessionsAsync_DoesNotAffectOtherUsers()
    {
        var (ctx, store) = Create();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, user1);
        await UserFactory.SeedUserAsync(ctx, user2);

        ctx.UserSessions.AddRange(ActiveSession(user1), ActiveSession(user2));
        await ctx.SaveChangesAsync();

        await store.RevokeAllSessionsAsync(user1);

        ctx.ChangeTracker.Clear();
        var user2Sessions = await ctx.UserSessions.Where(s => s.UserId == user2).ToListAsync();
        user2Sessions.Should().OnlyContain(s => !s.IsRevoked);

        await ctx.DisposeAsync();
    }

    // ── 11.4 RevokeAllSessionsAsync on user with no sessions is a no-op ───────

    [Fact]
    public async Task RevokeAllSessionsAsync_NoSessions_NoException()
    {
        var (ctx, store) = Create();

        var act = async () => await store.RevokeAllSessionsAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();

        await ctx.DisposeAsync();
    }

    // ── 11.5 RevokeAllSessionsAsync skips already-revoked sessions ────────────

    [Fact]
    public async Task RevokeAllSessionsAsync_AlreadyRevokedSession_NoError()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);

        var s1 = ActiveSession(userId);
        var s2 = ActiveSession(userId);
        var s3 = ActiveSession(userId);
        s3.IsRevoked = true;
        s3.RevokedAt = DateTime.UtcNow.AddMinutes(-5);
        var originalRevokedAt = s3.RevokedAt;

        ctx.UserSessions.AddRange(s1, s2, s3);
        await ctx.SaveChangesAsync();

        var act = async () => await store.RevokeAllSessionsAsync(userId);
        await act.Should().NotThrowAsync();

        ctx.ChangeTracker.Clear();
        var all = await ctx.UserSessions.ToListAsync();
        all.Should().OnlyContain(s => s.IsRevoked);

        // Already-revoked session's RevokedAt should not be modified
        var s3Loaded = await ctx.UserSessions.FirstAsync(s => s.Id == s3.Id);
        s3Loaded.RevokedAt.Should().Be(originalRevokedAt);

        await ctx.DisposeAsync();
    }
}
