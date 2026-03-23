using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Stores;
using HPD.Auth.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace HPD.Auth.Infrastructure.Tests.Stores;

/// <summary>
/// Section 8: SessionStore — Create Behavior
/// </summary>
public class SessionStoreCreateTests
{
    private static (HPDAuthDbContext ctx, SessionStore store) Create(ITenantContext? tenant = null)
    {
        tenant ??= new SingleTenantContext();
        var ctx = HPDAuthDbContextFactory.CreateIsolated(tenant);
        var store = new SessionStore(ctx, tenant);
        return (ctx, store);
    }

    // ── 8.1 Session is created with correct defaults ──────────────────────────

    [Fact]
    public async Task CreateSessionAsync_SetsCorrectDefaults()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);
        var sessionCtx = new SessionContext(IpAddress: "1.2.3.4", UserAgent: "Mozilla/5.0", AAL: "aal1");

        var before = DateTime.UtcNow;
        var session = await store.CreateSessionAsync(userId, sessionCtx);
        var after = DateTime.UtcNow;

        session.UserId.Should().Be(userId);
        session.IpAddress.Should().Be("1.2.3.4");
        session.UserAgent.Should().Be("Mozilla/5.0");
        session.AAL.Should().Be("aal1");
        session.IsRevoked.Should().BeFalse();
        session.SessionState.Should().Be("active");
        session.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        session.ExpiresAt.Should().BeCloseTo(session.CreatedAt.AddDays(14), TimeSpan.FromSeconds(1));
        session.InstanceId.Should().Be(Guid.Empty);

        await ctx.DisposeAsync();
    }

    // ── 8.2 Session lifetime is overridden when Lifetime is specified ─────────

    [Fact]
    public async Task CreateSessionAsync_CustomLifetime_SetsCorrectExpiry()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);
        var sessionCtx = new SessionContext(null, null, Lifetime: TimeSpan.FromHours(1));

        var before = DateTime.UtcNow;
        var session = await store.CreateSessionAsync(userId, sessionCtx);
        var after = DateTime.UtcNow;

        var expectedExpiry = before.AddHours(1);
        session.ExpiresAt.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(5));

        await ctx.DisposeAsync();
    }

    // ── 8.3 Session is persisted to the database ──────────────────────────────

    [Fact]
    public async Task CreateSessionAsync_SessionPersistedToDatabase()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);

        var session = await store.CreateSessionAsync(userId, new SessionContext(null, null));

        var found = await ctx.UserSessions.FindAsync(session.Id);
        found.Should().NotBeNull();
        found!.Id.Should().Be(session.Id);

        await ctx.DisposeAsync();
    }

    // ── 8.4 AAL is stored correctly for all levels ────────────────────────────

    [Fact]
    public async Task CreateSessionAsync_AllAALLevels_StoredCorrectly()
    {
        var (ctx, store) = Create();
        var userId = Guid.NewGuid();
        await UserFactory.SeedUserAsync(ctx, userId);

        var s1 = await store.CreateSessionAsync(userId, new SessionContext(null, null, AAL: "aal1"));
        var s2 = await store.CreateSessionAsync(userId, new SessionContext(null, null, AAL: "aal2"));
        var s3 = await store.CreateSessionAsync(userId, new SessionContext(null, null, AAL: "aal3"));

        s1.AAL.Should().Be("aal1");
        s2.AAL.Should().Be("aal2");
        s3.AAL.Should().Be("aal3");

        await ctx.DisposeAsync();
    }
}
