using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Stores;
using HPD.Auth.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Auth.Infrastructure.Tests.Stores;

/// <summary>
/// Section 7: AuditLogStore — Query Behavior
/// </summary>
public class AuditLogStoreQueryTests
{
    private static AuditLogStore CreateStore(HPDAuthDbContext ctx)
        => new(ctx, NullLogger<AuditLogStore>.Instance);

    private static async Task SeedLogsAsync(HPDAuthDbContext ctx, IEnumerable<AuditLog> logs)
    {
        foreach (var log in logs)
            ctx.AuditLogs.Add(log);
        await ctx.SaveChangesAsync();
    }

    private static AuditLog MakeLog(Guid? userId = null, string action = "user.login", string category = "authentication", DateTime? timestamp = null, Guid? instanceId = null)
        => new AuditLog
        {
            Id = Guid.NewGuid(),
            InstanceId = instanceId ?? Guid.Empty,
            UserId = userId,
            Action = action,
            Category = category,
            Timestamp = timestamp ?? DateTime.UtcNow,
            Success = true,
        };

    // ── 7.1 QueryAsync filters by UserId ─────────────────────────────────────

    [Fact]
    public async Task QueryAsync_FilterByUserId_ReturnsMatchingLogs()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        await SeedLogsAsync(ctx, [
            MakeLog(userId1), MakeLog(userId1), MakeLog(userId2),
        ]);

        var result = await store.QueryAsync(new AuditLogQuery(UserId: userId1));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(l => l.UserId == userId1);
    }

    // ── 7.2 QueryAsync filters by Action ─────────────────────────────────────

    [Fact]
    public async Task QueryAsync_FilterByAction_ReturnsMatchingLogs()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        await SeedLogsAsync(ctx, [
            MakeLog(action: "user.login"),
            MakeLog(action: "user.login"),
            MakeLog(action: "token.refresh"),
        ]);

        var result = await store.QueryAsync(new AuditLogQuery(Action: "user.login"));

        result.Should().HaveCount(2);
    }

    // ── 7.3 QueryAsync filters by Category ───────────────────────────────────

    [Fact]
    public async Task QueryAsync_FilterByCategory_ReturnsMatchingLogs()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        await SeedLogsAsync(ctx, [
            MakeLog(category: "authentication"),
            MakeLog(category: "authentication"),
            MakeLog(category: "admin"),
        ]);

        var result = await store.QueryAsync(new AuditLogQuery(Category: "authentication"));

        result.Should().HaveCount(2);
    }

    // ── 7.4 QueryAsync filters by date range (From) ──────────────────────────

    [Fact]
    public async Task QueryAsync_FilterByFrom_ReturnsLogsAfterFrom()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);
        var now = DateTime.UtcNow;

        await SeedLogsAsync(ctx, [
            MakeLog(timestamp: now.AddDays(-1)),
            MakeLog(timestamp: now),
            MakeLog(timestamp: now.AddDays(1)),
        ]);

        var result = await store.QueryAsync(new AuditLogQuery(From: now.AddHours(-1)));

        result.Should().HaveCount(2);
    }

    // ── 7.5 QueryAsync filters by date range (To) ────────────────────────────

    [Fact]
    public async Task QueryAsync_FilterByTo_ReturnsLogsBeforeTo()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);
        var now = DateTime.UtcNow;

        await SeedLogsAsync(ctx, [
            MakeLog(timestamp: now.AddDays(-1)),
            MakeLog(timestamp: now),
            MakeLog(timestamp: now.AddDays(1)),
        ]);

        var result = await store.QueryAsync(new AuditLogQuery(To: now.AddHours(1)));

        result.Should().HaveCount(2);
    }

    // ── 7.6 QueryAsync filters by From and To together ───────────────────────

    [Fact]
    public async Task QueryAsync_FilterByFromAndTo_ReturnsOnlyInRange()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);
        var now = DateTime.UtcNow;

        await SeedLogsAsync(ctx, [
            MakeLog(timestamp: now.AddDays(-1)),
            MakeLog(timestamp: now),
            MakeLog(timestamp: now.AddDays(1)),
        ]);

        var result = await store.QueryAsync(new AuditLogQuery(
            From: now.AddHours(-1),
            To: now.AddHours(1)));

        result.Should().HaveCount(1);
    }

    // ── 7.7 QueryAsync pagination — Page 1 and Page 2 ────────────────────────

    [Fact]
    public async Task QueryAsync_Pagination_ReturnCorrectPages()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        var logs = Enumerable.Range(0, 7).Select(_ => MakeLog()).ToList();
        await SeedLogsAsync(ctx, logs);

        var page1 = await store.QueryAsync(new AuditLogQuery(Page: 1, PageSize: 5));
        var page2 = await store.QueryAsync(new AuditLogQuery(Page: 2, PageSize: 5));

        page1.Should().HaveCount(5);
        page2.Should().HaveCount(2);

        var allIds = page1.Select(l => l.Id).Concat(page2.Select(l => l.Id)).ToList();
        allIds.Should().OnlyHaveUniqueItems();
        allIds.Should().HaveCount(7);
    }

    // ── 7.8 QueryAsync results are ordered by Timestamp descending ────────────

    [Fact]
    public async Task QueryAsync_OrderedByTimestampDescending()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);
        var now = DateTime.UtcNow;

        var t1 = now.AddMinutes(-2);
        var t2 = now.AddMinutes(-1);
        var t3 = now;

        await SeedLogsAsync(ctx, [
            MakeLog(timestamp: t1),
            MakeLog(timestamp: t2),
            MakeLog(timestamp: t3),
        ]);

        var result = await store.QueryAsync(new AuditLogQuery());

        result[0].Timestamp.Should().Be(t3);
        result[1].Timestamp.Should().Be(t2);
        result[2].Timestamp.Should().Be(t1);
    }

    // ── 7.9 QueryAsync with no filters returns all logs (up to PageSize) ──────

    [Fact]
    public async Task QueryAsync_NoFilters_ReturnsAllLogs()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        await SeedLogsAsync(ctx, Enumerable.Range(0, 10).Select(_ => MakeLog()));

        var result = await store.QueryAsync(new AuditLogQuery(PageSize: 50));

        result.Should().HaveCount(10);
    }

    // ── 7.10 QueryAsync returns empty list when no logs match ─────────────────

    [Fact]
    public async Task QueryAsync_NoMatch_ReturnsEmptyList()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        var result = await store.QueryAsync(new AuditLogQuery(Action: "nonexistent.action"));

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    // ── 7.11 QueryAsync respects tenant query filter ──────────────────────────

    [Fact]
    public async Task QueryAsync_TenantFilter_ReturnsOnlyCurrentTenantLogs()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        await using (var setupA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA))
        {
            setupA.AuditLogs.Add(MakeLog(instanceId: tenantA.InstanceId));
            await setupA.SaveChangesAsync();
        }
        await using (var setupB = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantB))
        {
            setupB.AuditLogs.Add(MakeLog(instanceId: tenantB.InstanceId));
            await setupB.SaveChangesAsync();
        }

        await using var ctxA = HPDAuthDbContextFactory.CreateInMemory(dbName, tenantA);
        var store = CreateStore(ctxA);

        var result = await store.QueryAsync(new AuditLogQuery());

        result.Should().HaveCount(1);
        result[0].InstanceId.Should().Be(tenantA.InstanceId);
    }
}
